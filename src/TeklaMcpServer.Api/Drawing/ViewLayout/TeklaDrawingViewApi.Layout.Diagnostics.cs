using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using TeklaMcpServer.Api.Diagnostics;
using TeklaMcpServer.Api.Drawing;

namespace TeklaMcpServer.Api.Drawing.ViewLayout;

public sealed partial class TeklaDrawingViewApi
{
    private static void TraceScaleSelectionInputs(
        DrawingLayoutWorkspace workspace,
        IReadOnlyList<View> views,
        IReadOnlyList<View> scaleDriverViews,
        IReadOnlyList<double> candidates,
        double sheetW,
        double sheetH,
        double margin,
        double gap,
        double availW,
        double availH,
        double currentScale,
        double minDenom,
        DrawingScalePolicy scalePolicy,
        DrawingLayoutApplyMode applyMode)
    {
        var scaleDriverIds = scaleDriverViews
            .Select(v => v.GetIdentifier().ID)
            .ToHashSet();
        var sb = new StringBuilder();
        sb.AppendFormat(
            CultureInfo.InvariantCulture,
            "policy={0} applyMode={1} sheet={2:F1}x{3:F1} margin={4:F1} gap={5:F1} usable={6:F1}x{7:F1} reserved={8} currentScale={9:F2} minDenom={10:F3} candidates=[{11}]",
            scalePolicy,
            applyMode,
            sheetW,
            sheetH,
            margin,
            gap,
            availW,
            availH,
            workspace.ReservedAreas.Count,
            currentScale,
            minDenom,
            string.Join(",", candidates.Select(c => c.ToString("0.###", CultureInfo.InvariantCulture))));

        foreach (var view in views)
        {
            var viewId = view.GetIdentifier().ID;
            var kind = workspace.GetSemanticKind(viewId);
            var projectionStrength = workspace.GetProjectionStrength(viewId);
            var scaleFlexibility = workspace.GetScaleFlexibility(viewId);
            var isDriver = scaleDriverIds.Contains(viewId) ? 1 : 0;
            var frame = workspace.GetSelectedFrameSize(viewId, view.Width, view.Height);
            var frameWidth = frame.Width;
            var frameHeight = frame.Height;

            sb.AppendFormat(
                CultureInfo.InvariantCulture,
                " | view={0}:{1}:{2}:projection={3}:scaleFlex={4}:scale={5:F2}:driver={6}:frame={7:F2}x{8:F2}:origin={9:F2},{10:F2}",
                viewId,
                view.ViewType,
                kind,
                projectionStrength,
                scaleFlexibility,
                view.Attributes.Scale,
                isDriver,
                frameWidth,
                frameHeight,
                view.Origin?.X ?? 0,
                view.Origin?.Y ?? 0);
        }

        var line = sb.ToString();
        PerfTrace.Write("api-view", "fit_scale_inputs", 0, line);
        DrawingProjectionAlignmentService.Log($"[fit_scale_inputs] {line}");
    }

    private static void TraceScaleCandidate(
        double candidateScale,
        IReadOnlyList<View> views,
        IReadOnlyList<(double w, double h)> frames,
        bool fits,
        IReadOnlyList<DrawingFitConflict>? oversizeConflicts = null)
    {
        var sb = new StringBuilder();
        sb.AppendFormat(
            CultureInfo.InvariantCulture,
            "candidate=1:{0} fits={1} oversizeConflicts={2}",
            candidateScale.ToString("0.###", CultureInfo.InvariantCulture),
            fits ? 1 : 0,
            oversizeConflicts?.Count ?? 0);

        for (int i = 0; i < views.Count && i < frames.Count; i++)
        {
            var frame = frames[i];
            sb.AppendFormat(
                CultureInfo.InvariantCulture,
                " | view={0}:{1}:frame={2:F2}x{3:F2}:scale={4:F2}",
                views[i].GetIdentifier().ID,
                views[i].ViewType,
                frame.w,
                frame.h,
                views[i].Attributes.Scale);
        }

        PerfTrace.Write("api-view", "fit_scale_candidate", 0, sb.ToString());
    }

    private static void TraceRelaxedPackingFeasibility(
        double candidateScale,
        DrawingArrangeContext context,
        IReadOnlyList<(double w, double h)> frames)
    {
        if (!PerfTrace.IsActive)
            return;

        var result = DrawingPackingEstimator.CheckRelaxedMaxRectsFit(
            frames,
            context.SheetWidth,
            context.SheetHeight,
            context.Margin,
            context.Gap,
            context.ReservedAreas);

        PerfTrace.Write(
            "api-view",
            "fit_scale_relaxed_packing",
            0,
            string.Format(
                CultureInfo.InvariantCulture,
                "candidate=1:{0} fits={1} frames={2} reserved={3} usable={4:F1}x{5:F1} attempts={6} order={7} heuristic={8}",
                candidateScale.ToString("0.###", CultureInfo.InvariantCulture),
                result.Fits ? 1 : 0,
                result.FrameCount,
                result.ReservedAreaCount,
                result.AvailableWidth,
                result.AvailableHeight,
                result.Attempts,
                string.IsNullOrWhiteSpace(result.Order) ? "none" : result.Order,
                result.Fits ? result.Heuristic.ToString() : "none"));
    }

    internal static string FormatEstimateFitFailureDecision(EstimateFitFailureDecision decision)
    {
        var sb = new StringBuilder();
        sb.AppendFormat(
            CultureInfo.InvariantCulture,
            "stage={0} candidate=1:{1} fits={2} oversizeConflicts={3} diagnosedConflicts={4}",
            decision.Stage,
            decision.CandidateScale.ToString("0.###", CultureInfo.InvariantCulture),
            decision.Fits ? 1 : 0,
            decision.OversizeConflicts.Count,
            decision.DiagnosedConflicts.Count);

        foreach (var conflict in decision.OversizeConflicts)
        {
            AppendEstimateConflict(sb, "oversize", conflict);
        }

        foreach (var conflict in decision.DiagnosedConflicts)
        {
            AppendEstimateConflict(sb, "diagnosed", conflict);
        }

        return sb.ToString();
    }

    private static void TraceEstimateFailureDecision(EstimateFitFailureDecision decision)
        => PerfTrace.Write("api-view", "fit_scale_conflicts", 0, FormatEstimateFitFailureDecision(decision));

    private static void TraceScaleDecision(
        DrawingScalePolicy scalePolicy,
        DrawingLayoutApplyMode applyMode,
        IReadOnlyList<double> candidates,
        double? selectedScale,
        IReadOnlyList<EstimateFitFailureDecision> rejectedCandidates,
        string decisionLayer)
    {
        var firstRejected = rejectedCandidates.Count > 0 ? rejectedCandidates[0] : (EstimateFitFailureDecision?)null;
        var lastRejected = rejectedCandidates.Count > 0 ? rejectedCandidates[rejectedCandidates.Count - 1] : (EstimateFitFailureDecision?)null;
        var selectedIndex = -1;
        if (selectedScale.HasValue)
        {
            for (var i = 0; i < candidates.Count; i++)
            {
                if (System.Math.Abs(candidates[i] - selectedScale.Value) < 0.001)
                {
                    selectedIndex = i;
                    break;
                }
            }
        }

        PerfTrace.Write(
            "api-view",
            "fit_scale_decision",
            0,
            string.Format(
                CultureInfo.InvariantCulture,
                "policy={0} applyMode={1} layer={2} selectedScale={3} selectedIndex={4} candidates=[{5}] rejectedBeforeSelected={6} firstRejected={7} lastRejected={8} lastOversizeConflicts={9} lastDiagnosedConflicts={10}",
                scalePolicy,
                applyMode,
                decisionLayer,
                selectedScale.HasValue
                    ? "1:" + selectedScale.Value.ToString("0.###", CultureInfo.InvariantCulture)
                    : "none",
                selectedIndex,
                string.Join(",", candidates.Select(c => "1:" + c.ToString("0.###", CultureInfo.InvariantCulture))),
                rejectedCandidates.Count,
                FormatRejectedScale(firstRejected),
                FormatRejectedScale(lastRejected),
                lastRejected?.OversizeConflicts.Count ?? 0,
                lastRejected?.DiagnosedConflicts.Count ?? 0));
    }

    private static string FormatRejectedScale(EstimateFitFailureDecision? decision)
    {
        if (!decision.HasValue)
            return "none";

        var value = decision.Value;
        var layer = value.OversizeConflicts.Count > 0
            ? "oversize-check"
            : value.DiagnosedConflicts.Count > 0
                ? "estimate-fit"
                : value.Stage;
        return string.Format(
            CultureInfo.InvariantCulture,
            "1:{0}:{1}:oversize={2}:diagnosed={3}",
            value.CandidateScale.ToString("0.###", CultureInfo.InvariantCulture),
            layer,
            value.OversizeConflicts.Count,
            value.DiagnosedConflicts.Count);
    }

    private static void TraceLayoutDecision(
        double selectedScale,
        DrawingLayoutCandidateSelection selection,
        DrawingLayoutCandidateApplyPlan applyPlan,
        DrawingLayoutCandidateApplySafetyDecision safetyDecision)
    {
        var selected = selection.Selected;
        var selectedName = selected == null || string.IsNullOrWhiteSpace(selected.Candidate.Name)
            ? "none"
            : selected.Candidate.Name;

        PerfTrace.Write(
            "api-view",
            "fit_layout_decision",
            0,
            string.Format(
                CultureInfo.InvariantCulture,
                "selectedScale=1:{0} selectedCandidate={1} feasible={2} score={3:0.###} selectionDiagnostics={4} canApply={5} applyAllowed={6} effectiveMode={7} safetyReason={8}",
                selectedScale.ToString("0.###", CultureInfo.InvariantCulture),
                selectedName,
                selected?.IsFeasible == true ? 1 : 0,
                selected?.Score.TotalScore ?? 0.0,
                selection.Diagnostics.Count,
                applyPlan.CanApply ? 1 : 0,
                safetyDecision.IsAllowed ? 1 : 0,
                safetyDecision.EffectiveMode,
                DrawingLayoutCandidateApplySafetyDecisionReasonFormatter.ToTraceString(safetyDecision.Reason)));
    }

    private static void AppendEstimateConflict(StringBuilder sb, string source, DrawingFitConflict conflict)
    {
        sb.AppendFormat(
            CultureInfo.InvariantCulture,
            " | source={0} view={1}:{2} zone={3} bbox={4}",
            source,
            conflict.ViewId,
            string.IsNullOrWhiteSpace(conflict.ViewType) ? "unknown" : conflict.ViewType,
            string.IsNullOrWhiteSpace(conflict.AttemptedZone) ? "unknown" : conflict.AttemptedZone,
            conflict.BBoxMinX.HasValue && conflict.BBoxMinY.HasValue && conflict.BBoxMaxX.HasValue && conflict.BBoxMaxY.HasValue
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    "[{0:F2},{1:F2},{2:F2},{3:F2}]",
                    conflict.BBoxMinX.Value,
                    conflict.BBoxMinY.Value,
                    conflict.BBoxMaxX.Value,
                    conflict.BBoxMaxY.Value)
                : "n/a");

        foreach (var item in conflict.Conflicts)
        {
            sb.AppendFormat(
                CultureInfo.InvariantCulture,
                " conflict={0}:other={1}:target={2}",
                string.IsNullOrWhiteSpace(item.Type) ? "unknown" : item.Type,
                item.OtherViewId?.ToString(CultureInfo.InvariantCulture) ?? "n/a",
                string.IsNullOrWhiteSpace(item.Target) ? "n/a" : item.Target);
        }
    }

    private static void TracePlannedVsActualParity(
        string stage,
        DrawingLayoutWorkspace workspace,
        IReadOnlyList<ArrangedView> arranged,
        IReadOnlyDictionary<int, ReservedRect> actualRects)
    {
        foreach (var item in arranged)
        {
            if (!workspace.RuntimeViewsById.TryGetValue(item.Id, out var view))
                continue;

            var frame = workspace.GetSelectedFrameSize(item.Id, view.Width, view.Height);
            var width = frame.Width;
            var height = frame.Height;

            var plannedRect = ViewPlacementGeometryService.CreateRectFromOrigin(
                workspace,
                view,
                item.OriginX,
                item.OriginY,
                width,
                height);

            var hasActualRect = actualRects.TryGetValue(item.Id, out var actualRect);
            var plannedCenterX = (plannedRect.MinX + plannedRect.MaxX) / 2.0;
            var plannedCenterY = (plannedRect.MinY + plannedRect.MaxY) / 2.0;
            var plannedWidth = plannedRect.MaxX - plannedRect.MinX;
            var plannedHeight = plannedRect.MaxY - plannedRect.MinY;

            var actualCenterX = hasActualRect ? (actualRect.MinX + actualRect.MaxX) / 2.0 : 0.0;
            var actualCenterY = hasActualRect ? (actualRect.MinY + actualRect.MaxY) / 2.0 : 0.0;
            var actualWidth = hasActualRect ? actualRect.MaxX - actualRect.MinX : 0.0;
            var actualHeight = hasActualRect ? actualRect.MaxY - actualRect.MinY : 0.0;

            PerfTrace.Write(
                "api-view",
                "fit_layout_parity",
                0,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "stage={0} view={1}:{2} placement={3}->{4} fallback={5} plannedOrigin={6:F2},{7:F2} plannedRect=[{8:F2},{9:F2},{10:F2},{11:F2}] actualRect={12} deltaCenter=({13:F2},{14:F2}) deltaSize=({15:F2},{16:F2})",
                    stage,
                    item.Id,
                    item.ViewType,
                    string.IsNullOrWhiteSpace(item.PreferredPlacementSide) ? "none" : item.PreferredPlacementSide,
                    string.IsNullOrWhiteSpace(item.ActualPlacementSide) ? "none" : item.ActualPlacementSide,
                    item.PlacementFallbackUsed ? 1 : 0,
                    item.OriginX,
                    item.OriginY,
                    plannedRect.MinX,
                    plannedRect.MinY,
                    plannedRect.MaxX,
                    plannedRect.MaxY,
                    hasActualRect
                        ? string.Format(
                            CultureInfo.InvariantCulture,
                            "[{0:F2},{1:F2},{2:F2},{3:F2}]",
                            actualRect.MinX,
                            actualRect.MinY,
                            actualRect.MaxX,
                            actualRect.MaxY)
                        : "n/a",
                    hasActualRect ? actualCenterX - plannedCenterX : 0.0,
                    hasActualRect ? actualCenterY - plannedCenterY : 0.0,
                    hasActualRect ? actualWidth - plannedWidth : 0.0,
                    hasActualRect ? actualHeight - plannedHeight : 0.0));
        }
    }

    private static void TraceActualViewOverlaps(
        string stage,
        DrawingLayoutWorkspace workspace,
        IReadOnlyDictionary<int, ReservedRect> actualRects)
    {
        var ids = actualRects.Keys.OrderBy(static id => id).ToList();
        for (var i = 0; i < ids.Count; i++)
        {
            for (var j = i + 1; j < ids.Count; j++)
            {
                var firstId = ids[i];
                var secondId = ids[j];
                var firstRect = actualRects[firstId];
                var secondRect = actualRects[secondId];
                if (!ViewPlacementValidator.Intersects(firstRect, secondRect))
                    continue;

                PerfTrace.Write(
                    "api-view",
                    "fit_layout_actual_overlap",
                    0,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "stage={0} a={1}:{2} b={3}:{4} aRect=[{5:F2},{6:F2},{7:F2},{8:F2}] bRect=[{9:F2},{10:F2},{11:F2},{12:F2}]",
                        stage,
                        firstId,
                        workspace.GetSemanticKind(firstId),
                        secondId,
                        workspace.GetSemanticKind(secondId),
                        firstRect.MinX,
                        firstRect.MinY,
                        firstRect.MaxX,
                        firstRect.MaxY,
                        secondRect.MinX,
                        secondRect.MinY,
                        secondRect.MaxX,
                        secondRect.MaxY));

                DrawingProjectionAlignmentService.Log(string.Format(
                    CultureInfo.InvariantCulture,
                    "OVERLAP stage={0} a={1}:{2} b={3}:{4} aRect=[{5:F1},{6:F1},{7:F1},{8:F1}] bRect=[{9:F1},{10:F1},{11:F1},{12:F1}]",
                    stage,
                    firstId,
                    workspace.GetSemanticKind(firstId),
                    secondId,
                    workspace.GetSemanticKind(secondId),
                    firstRect.MinX,
                    firstRect.MinY,
                    firstRect.MaxX,
                    firstRect.MaxY,
                    secondRect.MinX,
                    secondRect.MinY,
                    secondRect.MaxX,
                    secondRect.MaxY));
            }
        }
    }

    private static void TraceLayoutFill(DrawingLayoutCandidateEvaluation evaluation)
    {
        var candidate = evaluation.Candidate;
        var breakdown = evaluation.Score.Breakdown;

        var rects = candidate.Views
            .Select(static v => v.LayoutRect)
            .Where(static r => r != null)
            .Select(static r => r!)
            .ToList();

        var bboxMinX = rects.Count > 0 ? rects.Min(static r => r.MinX) : 0.0;
        var bboxMinY = rects.Count > 0 ? rects.Min(static r => r.MinY) : 0.0;
        var bboxMaxX = rects.Count > 0 ? rects.Max(static r => r.MaxX) : 0.0;
        var bboxMaxY = rects.Count > 0 ? rects.Max(static r => r.MaxY) : 0.0;
        var bboxArea = System.Math.Max(0, bboxMaxX - bboxMinX) * System.Math.Max(0, bboxMaxY - bboxMinY);

        var scale = candidate.Views.Select(static v => v.Scale).Where(static s => s > 0).DefaultIfEmpty(0).FirstOrDefault();

        DrawingProjectionAlignmentService.Log(string.Format(
            CultureInfo.InvariantCulture,
            "LAYOUT_FILL candidate={0} scale={1:0.###} fill={2:0.###} union={3:0} available={4:0} bbox={5:0}",
            string.IsNullOrWhiteSpace(candidate.Name) ? "unnamed" : candidate.Name,
            scale,
            breakdown.FillRatioRaw,
            breakdown.TotalViewArea,
            breakdown.AvailableSheetArea,
            bboxArea));
    }

    private static void TraceLayoutCandidateScore(DrawingLayoutCandidateEvaluation evaluation)
    {
        var candidate = evaluation.Candidate;
        var score = evaluation.Score;
        var validation = evaluation.Validation;

        TraceLayoutFill(evaluation);

        PerfTrace.Write(
            "api-view",
            "fit_layout_score",
            0,
            string.Format(
                CultureInfo.InvariantCulture,
                "candidate={0} total={1:0.###} feasible={2} views={3} missingRects={4} nonDetail={5} fill={6:0.###} uniformScale={7:0.###} edgePenalty={8:0.###} preferredSidePenalty={9:0.###} compactnessPenalty={10:0.###} stackOrderPenalty={11:0.###} projectedAxisPenalty={12:0.###} viewOverlaps={13}:area={14:0.###}:penalty={15:0.###} reservedOverlaps={16}:area={17:0.###}:penalty={18:0.###} diagnostics={19}",
                string.IsNullOrWhiteSpace(candidate.Name) ? "unnamed" : candidate.Name,
                score.TotalScore,
                evaluation.IsFeasible ? 1 : 0,
                score.Breakdown.ScoredViewCount,
                validation.MissingRectCount,
                score.Breakdown.NonDetailViewCount,
                score.Breakdown.FillRatioScore,
                score.Breakdown.UniformScaleScore,
                score.Breakdown.EdgeMarginPenalty,
                score.Breakdown.PreferredSidePenalty,
                score.Breakdown.CompactnessPenalty,
                score.Breakdown.StackOrderPenalty,
                score.Breakdown.ProjectedAxisPenalty,
                score.Breakdown.ViewOverlapCount,
                score.Breakdown.ViewOverlapArea,
                score.Breakdown.ViewOverlapPenalty,
                score.Breakdown.ReservedOverlapCount,
                score.Breakdown.ReservedOverlapArea,
                score.Breakdown.ReservedOverlapPenalty,
                validation.Diagnostics.Count));

        foreach (var group in candidate.StackOrderGroups)
        {
            var inversionCount = CountStackOrderInversions(group, out var pairCount);
            PerfTrace.Write(
                "api-view",
                "fit_layout_stack_order_score",
                0,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "candidate={0} preferred={1} actual={2} viewType={3} expected={4} actualViews={5} inversions={6}/{7}",
                    string.IsNullOrWhiteSpace(candidate.Name) ? "unnamed" : candidate.Name,
                    string.IsNullOrWhiteSpace(group.PreferredPlacementSide) ? "none" : group.PreferredPlacementSide,
                    string.IsNullOrWhiteSpace(group.ActualPlacementSide) ? "none" : group.ActualPlacementSide,
                    string.IsNullOrWhiteSpace(group.ViewType) ? "none" : group.ViewType,
                    string.Join(",", group.ExpectedViewIds),
                    string.Join(",", group.ActualViewIds),
                    inversionCount,
                    pairCount));
        }

        foreach (var diagnostic in validation.Diagnostics.Where(static diagnostic =>
                     diagnostic.StartsWith("score:view-overlap:", StringComparison.Ordinal)))
        {
            PerfTrace.Write(
                "api-view",
                "fit_layout_score_diagnostic",
                0,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "candidate={0} diagnostic={1}",
                    string.IsNullOrWhiteSpace(candidate.Name) ? "unnamed" : candidate.Name,
                    diagnostic));
        }
    }

    private static int CountStackOrderInversions(
        DrawingLayoutCandidateStackOrderGroup group,
        out int pairCount)
    {
        pairCount = 0;
        var expected = group.ExpectedViewIds
            .Distinct()
            .Select((id, index) => new { id, index })
            .ToDictionary(static item => item.id, static item => item.index);
        var actual = group.ActualViewIds
            .Where(expected.ContainsKey)
            .Distinct()
            .ToList();

        var inversions = 0;
        for (var i = 0; i < actual.Count; i++)
        for (var j = i + 1; j < actual.Count; j++)
        {
            pairCount++;
            if (expected[actual[i]] > expected[actual[j]])
                inversions++;
        }

        return inversions;
    }

    private static void TraceLayoutCandidateSelection(DrawingLayoutCandidateSelection selection)
    {
        var selected = selection.Selected;
        PerfTrace.Write(
            "api-view",
            "fit_layout_candidate_selection",
            0,
            string.Format(
                CultureInfo.InvariantCulture,
                "candidates={0} selected={1} feasible={2} total={3:0.###} diagnostics={4}",
                selection.Evaluations.Count,
                selected != null
                    ? (string.IsNullOrWhiteSpace(selected.Candidate.Name) ? "unnamed" : selected.Candidate.Name)
                    : "none",
                selected?.IsFeasible == true ? 1 : 0,
                selected?.Score.TotalScore ?? 0.0,
                selection.Diagnostics.Count));

        foreach (var item in selection.Items)
        {
            var evaluation = item.Evaluation;
            var candidate = evaluation.Candidate;
            PerfTrace.Write(
                "api-view",
                "fit_layout_candidate_rank",
                0,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "index={0} rank={1} selected={2} reason={3} candidate={4} feasible={5} total={6:0.###} diagnostics={7}",
                    item.Index,
                    item.Rank,
                    item.IsSelected ? 1 : 0,
                    DrawingLayoutCandidateSelectionReasonFormatter.ToTraceString(item.Reason),
                    string.IsNullOrWhiteSpace(candidate.Name) ? "unnamed" : candidate.Name,
                    evaluation.IsFeasible ? 1 : 0,
                    evaluation.Score.TotalScore,
                    evaluation.Validation.Diagnostics.Count));
        }
    }

    private static void TraceLayoutPlannedVariant(
        IReadOnlyList<DrawingLayoutPlannedView> baseline,
        DrawingLayoutCandidateEvaluation? baselineEval,
        string variantName,
        IReadOnlyList<DrawingLayoutPlannedView> variant,
        DrawingLayoutCandidateEvaluation? variantEval)
    {
        var summary = DrawingLayoutPlannedVariantDiagnostics.BuildSummary(
            baseline,
            baselineEval,
            variant,
            variantEval);

        PerfTrace.Write(
            "api-view",
            "fit_layout_planned_variant",
            0,
            string.Format(
                CultureInfo.InvariantCulture,
                "variant={0} moved={1} detailMoved={2} maxDelta={3:F1} avgDelta={4:F1} bboxBefore=[{5}] bboxAfter=[{6}] reservedOverlapBefore={7} reservedOverlapAfter={8}",
                variantName,
                summary.MovedCount,
                summary.DetailMovedCount,
                summary.MaxDelta,
                summary.AverageDelta,
                FormatRect(summary.BoundingBoxBefore),
                FormatRect(summary.BoundingBoxAfter),
                summary.ReservedOverlapBefore,
                summary.ReservedOverlapAfter));
    }

    private static string FormatRect(ReservedRect? rect)
        => rect == null
            ? "none"
            : string.Format(
                CultureInfo.InvariantCulture,
                "{0:F1},{1:F1},{2:F1},{3:F1}",
                rect.MinX,
                rect.MinY,
                rect.MaxX,
                rect.MaxY);

    private static void TraceLayoutCandidateApplyPlan(
        DrawingLayoutCandidateApplyPlan plan,
        DrawingLayoutWorkspace? workspace = null)
    {
        PerfTrace.Write(
            "api-view",
            "fit_layout_apply_plan",
            0,
            string.Format(
                CultureInfo.InvariantCulture,
                "candidate={0} canApply={1} reason={2} moves={3}",
                string.IsNullOrWhiteSpace(plan.CandidateName) ? "none" : plan.CandidateName,
                plan.CanApply ? 1 : 0,
                DrawingLayoutCandidateApplyPlanReasonFormatter.ToTraceString(plan.Reason),
                plan.Moves.Count));

        foreach (var move in plan.Moves)
        {
            PerfTrace.Write(
                "api-view",
                "fit_layout_apply_plan_move",
                0,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "candidate={0} view={1} origin={2:F2},{3:F2} scale={4:F2} rect={5}",
                    string.IsNullOrWhiteSpace(plan.CandidateName) ? "none" : plan.CandidateName,
                    move.ViewId,
                    move.TargetOriginX,
                    move.TargetOriginY,
                    move.Scale,
                    FormatRect(move.LayoutRect)));

            if (workspace == null || !workspace.FrameOffsetsById.TryGetValue(move.ViewId, out var storedOffset))
                continue;

            var scale = move.Scale > 0 ? move.Scale : 1.0;

            var offsetX = storedOffset.X / scale;
            var offsetY = storedOffset.Y / scale;
            var expectedCenterX = move.TargetOriginX + offsetX;
            var expectedCenterY = move.TargetOriginY + offsetY;
            var expectedRect = move.LayoutRect != null
                ? new ReservedRect(
                    expectedCenterX - move.LayoutRect.Width * 0.5,
                    expectedCenterY - move.LayoutRect.Height * 0.5,
                    expectedCenterX + move.LayoutRect.Width * 0.5,
                    expectedCenterY + move.LayoutRect.Height * 0.5)
                : null;

            PerfTrace.Write(
                "api-view",
                "fit_layout_apply_plan_offset",
                0,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "candidate={0} view={1} targetOrigin=({2:F2},{3:F2}) scale={4:F2} storedOffset=({5:F2},{6:F2}) usedOffset=({7:F2},{8:F2}) expectedCenter=({9:F2},{10:F2}) plannedRect={11} expectedRect={12}",
                    string.IsNullOrWhiteSpace(plan.CandidateName) ? "none" : plan.CandidateName,
                    move.ViewId,
                    move.TargetOriginX,
                    move.TargetOriginY,
                    scale,
                    storedOffset.X,
                    storedOffset.Y,
                    offsetX,
                    offsetY,
                    expectedCenterX,
                    expectedCenterY,
                    FormatRect(move.LayoutRect),
                    FormatRect(expectedRect)));
        }
    }

    private static void TraceLayoutCandidateApplyExecution(DrawingLayoutCandidateApplyExecutionResult result)
    {
        PerfTrace.Write(
            "api-view",
            "fit_layout_apply_execution",
            0,
            string.Format(
                CultureInfo.InvariantCulture,
                "candidate={0} mode={1} success={2} reason={3} requestedMoves={4} appliedMoves={5} missingViews={6} missingViewIds=[{7}]",
                string.IsNullOrWhiteSpace(result.CandidateName) ? "none" : result.CandidateName,
                result.Mode,
                result.Success ? 1 : 0,
                DrawingLayoutCandidateApplyExecutionReasonFormatter.ToTraceString(result.Reason),
                result.RequestedMoveCount,
                result.AppliedMoveCount,
                result.MissingRuntimeViewCount,
                string.Join(",", result.MissingRuntimeViewIds)));
    }

    private static void TraceLayoutCandidateApplyDeltas(DrawingLayoutCandidateApplyDeltaSummary summary)
    {
        PerfTrace.Write(
            "api-view",
            "fit_layout_apply_delta",
            0,
            string.Format(
                CultureInfo.InvariantCulture,
                "candidate={0} baseline={1} moves={2} comparable={3} missingBaseline={4} moved={5} scaleChanged={6} maxDelta={7:F2} avgDelta={8:F2} movementTolerance={9:F3} scaleTolerance={10:F3}",
                string.IsNullOrWhiteSpace(summary.CandidateName) ? "none" : summary.CandidateName,
                string.IsNullOrWhiteSpace(summary.BaselineCandidateName) ? "none" : summary.BaselineCandidateName,
                summary.MoveCount,
                summary.ComparableMoveCount,
                summary.MissingBaselineCount,
                summary.MovedCount,
                summary.ScaleChangedCount,
                summary.MaxDelta,
                summary.AverageDelta,
                DrawingLayoutCandidateApplyTolerances.Movement,
                DrawingLayoutCandidateApplyTolerances.Scale));

        foreach (var delta in summary.Deltas.Where(static delta =>
            delta.MissingBaseline || delta.Moved || delta.ScaleChanged))
        {
            PerfTrace.Write(
                "api-view",
                "fit_layout_apply_delta_view",
                0,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "candidate={0} view={1} missingBaseline={2} moved={3} scaleChanged={4} current={5:F2},{6:F2} target={7:F2},{8:F2} delta={9:F2},{10:F2} distance={11:F2} scale={12:F2}->{13:F2}",
                    string.IsNullOrWhiteSpace(summary.CandidateName) ? "none" : summary.CandidateName,
                    delta.ViewId,
                    delta.MissingBaseline ? 1 : 0,
                    delta.Moved ? 1 : 0,
                    delta.ScaleChanged ? 1 : 0,
                    delta.CurrentOriginX,
                    delta.CurrentOriginY,
                    delta.TargetOriginX,
                    delta.TargetOriginY,
                    delta.DeltaX,
                    delta.DeltaY,
                    delta.Distance,
                    delta.CurrentScale,
                    delta.TargetScale));
        }
    }

    private static void TraceLayoutCandidateApplySafety(
        DrawingLayoutCandidateApplyDeltaSummary summary,
        DrawingLayoutCandidateApplySafetyPolicy policy,
        DrawingLayoutCandidateApplySafetyDecision decision)
    {
        PerfTrace.Write(
            "api-view",
            "fit_layout_apply_safety",
            0,
            string.Format(
                CultureInfo.InvariantCulture,
                "candidate={0} requestedMode={1} effectiveMode={2} allowed={3} reason={4} missingBaseline={5} scaleChanged={6} maxDelta={7:F2} maxAllowedDelta={8}",
                string.IsNullOrWhiteSpace(summary.CandidateName) ? "none" : summary.CandidateName,
                decision.RequestedMode,
                decision.EffectiveMode,
                decision.IsAllowed ? 1 : 0,
                DrawingLayoutCandidateApplySafetyDecisionReasonFormatter.ToTraceString(decision.Reason),
                summary.MissingBaselineCount,
                summary.ScaleChangedCount,
                summary.MaxDelta,
                double.IsPositiveInfinity(policy.MaxDelta)
                    ? "unlimited"
                    : policy.MaxDelta.ToString("F2", CultureInfo.InvariantCulture)));
    }
}
