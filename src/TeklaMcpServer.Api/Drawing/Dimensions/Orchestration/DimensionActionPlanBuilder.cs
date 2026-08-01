using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// Turns reduction debug data into an ordered plan of proposed edits. Deterministic, and it
/// applies nothing: every step is a description of a call someone else may choose to make.
///
/// The boundary is deliberate — observation feeds the builder, the builder emits a plan, and an
/// LLM or a person decides whether to run it through the existing combine/move/arrange/recreate
/// commands. A future model works on the same observation and either produces a plan of this
/// shape or picks steps out of one; it does not belong inside this class.
/// </summary>
internal sealed class DimensionActionPlanBuilder
{
    /// <summary>
    /// Provenance for steps the builder synthesizes itself rather than projecting from a packet.
    /// </summary>
    internal const string BuilderSource = "action_plan_builder";

    public DimensionActionPlanResult Build(DimensionReductionDebugResult debug, int? viewId)
    {
        var effectiveViewId = viewId ?? debug.DecisionContext.View.ViewId;
        var result = new DimensionActionPlanResult
        {
            ViewId = effectiveViewId
        };
        foreach (var warning in debug.DecisionContext.Warnings.Concat(debug.DecisionContext.View.Warnings).Distinct())
            result.Warnings.Add(warning);

        var packets = DimensionOrchestrationDebugBuilder.Build(debug, effectiveViewId).Packets;
        var itemsById = debug.Groups
            .SelectMany(static group => group.Items)
            .Where(static item => item.Item != null)
            .GroupBy(static item => item.Item.DimensionId)
            .ToDictionary(static group => group.Key, static group => group.First());
        var contextsById = debug.DecisionContext.Dimensions
            .GroupBy(static context => context.DimensionId)
            .ToDictionary(static group => group.Key, static group => group.First());

        var stepOrder = 1;

        foreach (var packet in packets.Where(static packet => packet.Action == DimensionOrchestrationAction.Combine))
        {
            var step = CreateCombineStep(packet, itemsById, contextsById, debug.DecisionContext.View, stepOrder++);
            result.Steps.Add(step);

            var arrangeStep = CreateArrangeFollowUpStep(packet, itemsById, contextsById, debug.DecisionContext.View, stepOrder++);
            result.Steps.Add(arrangeStep);
        }

        foreach (var packet in packets.Where(static packet => packet.Action == DimensionOrchestrationAction.Suppress || packet.Action == DimensionOrchestrationAction.Review))
        {
            result.Steps.Add(CreateReviewStep(packet, itemsById, contextsById, debug.DecisionContext.View, stepOrder++));
        }

        return result;
    }

    private static DimensionActionPlanStep CreateCombineStep(
        DimensionOrchestrationActionPacket packet,
        IReadOnlyDictionary<int, DimensionReductionItemDebugInfo> itemsById,
        IReadOnlyDictionary<int, DimensionContext> contextsById,
        DrawingViewContext viewContext,
        int stepOrder)
    {
        var step = CreateBaseStep(packet, itemsById, contextsById, viewContext, stepOrder, DimensionPlanAction.Combine);
        step.ToolName = "combine_dimensions";
        step.PreviewOnly = true;
        step.ToolArguments = new DimensionActionPlanToolArguments
        {
            ViewId = packet.ViewId,
            PreviewOnly = true
        };
        step.ApplyToolArguments = new DimensionActionPlanToolArguments
        {
            ViewId = packet.ViewId,
            PreviewOnly = false
        };
        step.ToolArguments.DimensionIds.AddRange(packet.DimensionIds);
        step.ApplyToolArguments.DimensionIds.AddRange(packet.DimensionIds);
        return step;
    }

    private static DimensionActionPlanStep CreateArrangeFollowUpStep(
        DimensionOrchestrationActionPacket packet,
        IReadOnlyDictionary<int, DimensionReductionItemDebugInfo> itemsById,
        IReadOnlyDictionary<int, DimensionContext> contextsById,
        DrawingViewContext viewContext,
        int stepOrder)
    {
        var step = CreateBaseStep(packet, itemsById, contextsById, viewContext, stepOrder, DimensionPlanAction.Arrange);
        step.Reason = "post_combine_arrange_followup";
        // Every other step inherits its packet's provenance; this one has no packet behind it —
        // the builder invents it to follow a combine. It names the producer, and the producer is
        // deterministic code, not a model.
        step.Source = BuilderSource;
        step.ToolName = "arrange_dimensions";
        step.PreviewOnly = false;
        step.ToolArguments = new DimensionActionPlanToolArguments
        {
            ViewId = packet.ViewId,
            TargetGap = TeklaDrawingDimensionsApi.DefaultArrangeTargetGapPaper
        };
        step.DimensionIds.Clear();
        step.DimensionIds.AddRange(packet.DimensionIds);
        step.RelatedDimensionIds.Clear();
        step.RelatedDimensionIds.AddRange(packet.RelatedDimensionIds);
        return step;
    }

    private static DimensionActionPlanStep CreateReviewStep(
        DimensionOrchestrationActionPacket packet,
        IReadOnlyDictionary<int, DimensionReductionItemDebugInfo> itemsById,
        IReadOnlyDictionary<int, DimensionContext> contextsById,
        DrawingViewContext viewContext,
        int stepOrder)
    {
        var step = CreateBaseStep(packet, itemsById, contextsById, viewContext, stepOrder, DimensionPlanAction.ReviewOnly);
        step.PreviewOnly = true;
        return step;
    }

    private static DimensionActionPlanStep CreateBaseStep(
        DimensionOrchestrationActionPacket packet,
        IReadOnlyDictionary<int, DimensionReductionItemDebugInfo> itemsById,
        IReadOnlyDictionary<int, DimensionContext> contextsById,
        DrawingViewContext viewContext,
        int stepOrder,
        DimensionPlanAction action)
    {
        itemsById.TryGetValue(packet.PrimaryDimensionId, out var primaryItem);
        contextsById.TryGetValue(packet.PrimaryDimensionId, out var primaryContext);
        var step = new DimensionActionPlanStep
        {
            StepOrder = stepOrder,
            Action = action,
            PrimaryDimensionId = packet.PrimaryDimensionId,
            ViewId = packet.ViewId,
            DimensionType = packet.DimensionType,
            Reason = packet.Reason,
            Source = packet.Source,
            Evidence = CreateEvidence(packet.Evidence, primaryContext ?? primaryItem?.Context, viewContext)
        };

        step.DimensionIds.AddRange(packet.DimensionIds);
        step.RelatedDimensionIds.AddRange(packet.RelatedDimensionIds);
        return step;
    }

    private static DimensionActionPlanEvidence CreateEvidence(
        DimensionOrchestrationEvidence evidence,
        DimensionContext? context,
        DrawingViewContext viewContext)
    {
        var viewPlacement = DimensionViewPlacementInfoBuilder.Build(context, viewContext);
        var partsBoundsGap = DimensionPartsBoundsGapPolicy.Evaluate(viewPlacement);
        return new DimensionActionPlanEvidence
        {
            LayoutPolicyStatus = evidence.LayoutPolicyStatus,
            LayoutRecommendedAction = evidence.LayoutRecommendedAction,
            LayoutCombineClassification = evidence.LayoutCombineClassification,
            ReductionStatus = evidence.ReductionStatus,
            ReductionReason = evidence.ReductionReason,
            CombineConnectivityMode = evidence.CombineConnectivityMode,
            PreferredDimensionId = evidence.PreferredDimensionId,
            RepresentativeDimensionId = evidence.RepresentativeDimensionId,
            LineDirection = CopyVector(context?.AnnotationLineDirection),
            NormalDirection = CopyVector(context?.AnnotationNormalDirection),
            StartAlong = context?.AnnotationStartAlong,
            EndAlong = context?.AnnotationEndAlong,
            GeometryBand = CopyBand(context?.AnnotationGeometry.LocalBand),
            SegmentGeometryCount = context?.AnnotationSegmentGeometryCount ?? 0,
            HasTextBounds = context?.AnnotationHasTextBounds ?? false,
            HasPartsBounds = viewPlacement.HasPartsBounds,
            PartsBoundsSide = viewPlacement.PartsBoundsSide,
            IsOutsidePartsBounds = viewPlacement.IsOutsidePartsBounds,
            IntersectsPartsBounds = viewPlacement.IntersectsPartsBounds,
            OffsetFromPartsBounds = viewPlacement.OffsetFromPartsBounds ?? 0,
            ReferenceLineLength = viewPlacement.ReferenceLineLength ?? 0,
            Distance = viewPlacement.Distance,
            TopDirection = viewPlacement.TopDirection,
            ViewScale = viewPlacement.ViewScale,
            CanEvaluatePartsBoundsGap = partsBoundsGap.CanEvaluate,
            CurrentPartsBoundsGapDrawing = partsBoundsGap.CurrentGapDrawing,
            TargetPartsBoundsGapPaper = partsBoundsGap.TargetGapPaper,
            TargetPartsBoundsGapDrawing = partsBoundsGap.TargetGapDrawing,
            RequiresPartsBoundsGapCorrection = partsBoundsGap.RequiresOutwardCorrection,
            SuggestedOutwardDeltaFromPartsBounds = partsBoundsGap.SuggestedOutwardDeltaDrawing
        };
    }

    private static DrawingVectorInfo? CopyVector(DrawingVectorInfo? vector)
    {
        if (vector == null)
            return null;

        return new DrawingVectorInfo
        {
            X = vector.X,
            Y = vector.Y
        };
    }

    private static DimensionGeometryBand? CopyBand(DimensionGeometryBand? band)
    {
        if (band == null)
            return null;

        return new DimensionGeometryBand
        {
            StartAlong = band.StartAlong,
            EndAlong = band.EndAlong,
            MinOffset = band.MinOffset,
            MaxOffset = band.MaxOffset
        };
    }
}
