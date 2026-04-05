using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class DimensionAiAssistedOrchestrator
{
    public DimensionAiOrchestrationPlanResult Build(DimensionReductionDebugResult debug, int? viewId)
    {
        var effectiveViewId = viewId ?? debug.DecisionContext.View.ViewId;
        var result = new DimensionAiOrchestrationPlanResult
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

    private static DimensionAiOrchestrationPlanStep CreateCombineStep(
        DimensionOrchestrationActionPacket packet,
        IReadOnlyDictionary<int, DimensionReductionItemDebugInfo> itemsById,
        IReadOnlyDictionary<int, DimensionContext> contextsById,
        DimensionViewContext viewContext,
        int stepOrder)
    {
        var step = CreateBaseStep(packet, itemsById, contextsById, viewContext, stepOrder, DimensionAiAssistedAction.Combine);
        step.ToolName = "combine_dimensions";
        step.PreviewOnly = true;
        step.ToolArguments = new DimensionAiOrchestrationToolArguments
        {
            ViewId = packet.ViewId,
            PreviewOnly = true
        };
        step.ApplyToolArguments = new DimensionAiOrchestrationToolArguments
        {
            ViewId = packet.ViewId,
            PreviewOnly = false
        };
        step.ToolArguments.DimensionIds.AddRange(packet.DimensionIds);
        step.ApplyToolArguments.DimensionIds.AddRange(packet.DimensionIds);
        return step;
    }

    private static DimensionAiOrchestrationPlanStep CreateArrangeFollowUpStep(
        DimensionOrchestrationActionPacket packet,
        IReadOnlyDictionary<int, DimensionReductionItemDebugInfo> itemsById,
        IReadOnlyDictionary<int, DimensionContext> contextsById,
        DimensionViewContext viewContext,
        int stepOrder)
    {
        var step = CreateBaseStep(packet, itemsById, contextsById, viewContext, stepOrder, DimensionAiAssistedAction.Arrange);
        step.Reason = "post_combine_arrange_followup";
        step.Source = "ai_orchestrator";
        step.ToolName = "arrange_dimensions";
        step.PreviewOnly = false;
        step.ToolArguments = new DimensionAiOrchestrationToolArguments
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

    private static DimensionAiOrchestrationPlanStep CreateReviewStep(
        DimensionOrchestrationActionPacket packet,
        IReadOnlyDictionary<int, DimensionReductionItemDebugInfo> itemsById,
        IReadOnlyDictionary<int, DimensionContext> contextsById,
        DimensionViewContext viewContext,
        int stepOrder)
    {
        var step = CreateBaseStep(packet, itemsById, contextsById, viewContext, stepOrder, DimensionAiAssistedAction.ReviewOnly);
        step.PreviewOnly = true;
        return step;
    }

    private static DimensionAiOrchestrationPlanStep CreateBaseStep(
        DimensionOrchestrationActionPacket packet,
        IReadOnlyDictionary<int, DimensionReductionItemDebugInfo> itemsById,
        IReadOnlyDictionary<int, DimensionContext> contextsById,
        DimensionViewContext viewContext,
        int stepOrder,
        DimensionAiAssistedAction action)
    {
        itemsById.TryGetValue(packet.PrimaryDimensionId, out var primaryItem);
        contextsById.TryGetValue(packet.PrimaryDimensionId, out var primaryContext);
        var step = new DimensionAiOrchestrationPlanStep
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

    private static DimensionAiOrchestrationEvidence CreateEvidence(
        DimensionOrchestrationEvidence evidence,
        DimensionContext? context,
        DimensionViewContext viewContext)
    {
        var viewPlacement = DimensionViewPlacementInfoBuilder.Build(context, viewContext);
        var partsBoundsGap = DimensionPartsBoundsGapPolicy.Evaluate(viewPlacement);
        return new DimensionAiOrchestrationEvidence
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
            OffsetFromPartsBounds = viewPlacement.OffsetFromPartsBounds,
            ReferenceLineLength = viewPlacement.ReferenceLineLength,
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
