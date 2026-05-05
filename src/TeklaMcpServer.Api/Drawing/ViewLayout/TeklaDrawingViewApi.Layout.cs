using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;
using TeklaMcpServer.Api.Diagnostics;
using TeklaMcpServer.Api.Drawing;

namespace TeklaMcpServer.Api.Drawing.ViewLayout;

public sealed partial class TeklaDrawingViewApi
{
    internal readonly struct EstimateFitFailureDecision
    {
        public EstimateFitFailureDecision(
            string stage,
            double candidateScale,
            bool fits,
            IReadOnlyList<DrawingFitConflict>? oversizeConflicts,
            IReadOnlyList<DrawingFitConflict>? diagnosedConflicts)
        {
            Stage = stage;
            CandidateScale = candidateScale;
            Fits = fits;
            OversizeConflicts = oversizeConflicts ?? System.Array.Empty<DrawingFitConflict>();
            DiagnosedConflicts = diagnosedConflicts ?? System.Array.Empty<DrawingFitConflict>();
        }

        public string Stage { get; }
        public double CandidateScale { get; }
        public bool Fits { get; }
        public IReadOnlyList<DrawingFitConflict> OversizeConflicts { get; }
        public IReadOnlyList<DrawingFitConflict> DiagnosedConflicts { get; }
    }

    private readonly struct KeepScaleFitResult
    {
        public KeepScaleFitResult(
            IReadOnlyDictionary<int, (double Width, double Height)> frameSizes,
            double optimalScale,
            long elapsedMilliseconds)
        {
            FrameSizes = frameSizes;
            OptimalScale = optimalScale;
            ElapsedMilliseconds = elapsedMilliseconds;
        }

        public IReadOnlyDictionary<int, (double Width, double Height)> FrameSizes { get; }
        public double OptimalScale { get; }
        public long ElapsedMilliseconds { get; }
    }

    private readonly struct CandidateScaleProbeResult
    {
        public CandidateScaleProbeResult(
            List<View> views,
            IReadOnlyDictionary<int, (double Width, double Height)> frameSizes,
            IReadOnlyList<(double w, double h)> frames,
            long elapsedMilliseconds)
        {
            Views = views;
            FrameSizes = frameSizes;
            Frames = frames;
            ElapsedMilliseconds = elapsedMilliseconds;
        }

        public List<View> Views { get; }
        public IReadOnlyDictionary<int, (double Width, double Height)> FrameSizes { get; }
        public IReadOnlyList<(double w, double h)> Frames { get; }
        public long ElapsedMilliseconds { get; }
    }

    internal const double ProjectionAlignmentScaleCutoff = 100.0;
    internal const double ProjectionAlignmentMixedScaleTolerance = 0.05;

    internal static bool ShouldSkipProjectionAlignment(double optimalScale)
        => optimalScale >= ProjectionAlignmentScaleCutoff;

    internal static bool ShouldSkipProjectionAlignment(
        double optimalScale,
        IReadOnlyList<View> views,
        out string mode,
        out string diagnostic)
    {
        if (ShouldSkipProjectionAlignment(optimalScale))
        {
            mode = "disabled-scale";
            diagnostic = $"projection-skip:scale-too-small:1:{optimalScale.ToString(CultureInfo.InvariantCulture)}";
            return true;
        }

        if (views.Count > 1)
        {
            var scales = views
                .Select(v => v.Attributes.Scale > 0 ? v.Attributes.Scale : 1.0)
                .Where(scale => scale > 0)
                .ToList();
            if (scales.Count > 1)
            {
                var minScale = scales.Min();
                var maxScale = scales.Max();
                var allowedMaxScale = minScale * (1.0 + ProjectionAlignmentMixedScaleTolerance);
                if (maxScale > allowedMaxScale)
                {
                    mode = "disabled-mixed-scales";
                    diagnostic =
                        $"projection-skip:mixed-scales:min=1:{minScale.ToString("0.###", CultureInfo.InvariantCulture)}" +
                        $":max=1:{maxScale.ToString("0.###", CultureInfo.InvariantCulture)}" +
                        $":tolerance={(ProjectionAlignmentMixedScaleTolerance * 100.0).ToString("0.###", CultureInfo.InvariantCulture)}%";
                    return true;
                }
            }
        }

        mode = string.Empty;
        diagnostic = string.Empty;
        return false;
    }

    private static double ResolveTargetScale(
        View view,
        ViewSemanticKind semanticKind,
        double candidateScale,
        bool uniformAllNonDetail,
        IReadOnlyDictionary<int, double> originalScales)
    {
        if (!originalScales.TryGetValue(view.GetIdentifier().ID, out var originalScale))
            originalScale = view.Attributes.Scale > 0 ? view.Attributes.Scale : 1.0;

        if (semanticKind == ViewSemanticKind.Detail)
            return originalScale;

        if (uniformAllNonDetail)
            return candidateScale;

        if (semanticKind == ViewSemanticKind.BaseProjected)
            return candidateScale;

        return originalScale;
    }

    private static List<DrawingFitConflict> BuildOversizeConflicts(
        IReadOnlyList<View> views,
        IReadOnlyList<(double w, double h)> frames,
        double availW,
        double availH)
    {
        var conflicts = new List<DrawingFitConflict>();
        for (int i = 0; i < views.Count && i < frames.Count; i++)
        {
            var frame = frames[i];
            if (frame.w <= availW && frame.h <= availH)
                continue;

            conflicts.Add(new DrawingFitConflict
            {
                ViewId = views[i].GetIdentifier().ID,
                ViewType = views[i].ViewType.ToString(),
                AttemptedZone = "sheet",
                Conflicts = new List<DrawingFitConflictItem>
                {
                    new()
                    {
                        Type = "sheet-oversize",
                        Target = $"usable={availW:F1}x{availH:F1};view={frame.w:F1}x{frame.h:F1}"
                    }
                }
            });
        }

        return conflicts;
    }

    private static List<(double w, double h)> BuildFrameList(
        IReadOnlyList<View> views,
        IReadOnlyDictionary<int, (double Width, double Height)> frameSizes)
        => views
            .Select(v =>
            {
                if (frameSizes.TryGetValue(v.GetIdentifier().ID, out var size))
                    return (w: size.Width, h: size.Height);

                return (w: v.Width, h: v.Height);
            })
            .ToList();

    internal static HashSet<int> CollectOversizedStandardSectionScaleDriverIds(
        Tekla.Structures.Drawing.Drawing drawing,
        DrawingLayoutWorkspace workspace,
        IReadOnlyList<View> views,
        double gap)
    {
        var baseViewSelection = BaseViewSelection.Select(views);
        var baseView = baseViewSelection.View;
        if (baseView == null)
            return new HashSet<int>();

        var baseId = baseView.GetIdentifier().ID;
        var baseFrame = workspace.GetSelectedFrameSize(baseId, baseView.Width, baseView.Height);
        var baseWidth = baseFrame.Width;
        var baseHeight = baseFrame.Height;
        var semanticViews = SemanticViewSet.Build(views);
        var sectionGroups = SectionGroupSet.Build(
            semanticViews.Sections,
            drawing,
            baseView,
            new SectionPlacementSideResolver(new Model()));

        var result = new HashSet<int>();
        CollectOversizedStandardSectionScaleDriverIds(workspace, result, sectionGroups.Top, SectionPlacementSide.Top, baseWidth, baseHeight, gap);
        CollectOversizedStandardSectionScaleDriverIds(workspace, result, sectionGroups.Bottom, SectionPlacementSide.Bottom, baseWidth, baseHeight, gap);
        CollectOversizedStandardSectionScaleDriverIds(workspace, result, sectionGroups.Left, SectionPlacementSide.Left, baseWidth, baseHeight, gap);
        CollectOversizedStandardSectionScaleDriverIds(workspace, result, sectionGroups.Right, SectionPlacementSide.Right, baseWidth, baseHeight, gap);
        return result;
    }

    private static void CollectOversizedStandardSectionScaleDriverIds(
        DrawingLayoutWorkspace workspace,
        HashSet<int> oversizedIds,
        IReadOnlyList<View> sections,
        SectionPlacementSide placementSide,
        double baseWidth,
        double baseHeight,
        double gap)
    {
        foreach (var section in sections)
        {
            var sectionId = section.GetIdentifier().ID;
            var size = workspace.GetSelectedFrameSize(sectionId, section.Width, section.Height);
            var width = size.Width;
            var height = size.Height;
            if (BaseProjectedDrawingArrangeStrategy.IsOversizedStandardSection(placementSide, baseWidth, baseHeight, width, height, gap))
                oversizedIds.Add(sectionId);
        }
    }

    internal static bool IsOversizedStandardSectionScaleDriver(
        SectionPlacementSide placementSide,
        double baseWidth,
        double baseHeight,
        double sectionWidth,
        double sectionHeight,
        double gap)
        => BaseProjectedDrawingArrangeStrategy.IsOversizedStandardSection(
            placementSide,
            baseWidth,
            baseHeight,
            sectionWidth,
            sectionHeight,
            gap);

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
            var isDriver = scaleDriverIds.Contains(viewId) ? 1 : 0;
            var frame = workspace.GetSelectedFrameSize(viewId, view.Width, view.Height);
            var frameWidth = frame.Width;
            var frameHeight = frame.Height;

            sb.AppendFormat(
                CultureInfo.InvariantCulture,
                " | view={0}:{1}:{2}:scale={3:F2}:driver={4}:frame={5:F2}x{6:F2}:origin={7:F2},{8:F2}",
                viewId,
                view.ViewType,
                kind,
                view.Attributes.Scale,
                isDriver,
                frameWidth,
                frameHeight,
                view.Origin?.X ?? 0,
                view.Origin?.Y ?? 0);
        }

        PerfTrace.Write("api-view", "fit_scale_inputs", 0, sb.ToString());
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

            var plannedRect = ViewPlacementGeometryService.CreateCandidateRect(
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

    private static void TraceLayoutCandidateScore(DrawingLayoutCandidateEvaluation evaluation)
    {
        var candidate = evaluation.Candidate;
        var score = evaluation.Score;
        var validation = evaluation.Validation;

        PerfTrace.Write(
            "api-view",
            "fit_layout_score",
            0,
            string.Format(
                CultureInfo.InvariantCulture,
                "candidate={0} total={1:0.###} feasible={2} views={3} missingRects={4} nonDetail={5} fill={6:0.###} uniformScale={7:0.###} viewOverlaps={8}:area={9:0.###}:penalty={10:0.###} reservedOverlaps={11}:area={12:0.###}:penalty={13:0.###} diagnostics={14}",
                string.IsNullOrWhiteSpace(candidate.Name) ? "unnamed" : candidate.Name,
                score.TotalScore,
                evaluation.IsFeasible ? 1 : 0,
                score.Breakdown.ScoredViewCount,
                validation.MissingRectCount,
                score.Breakdown.NonDetailViewCount,
                score.Breakdown.FillRatioScore,
                score.Breakdown.UniformScaleScore,
                score.Breakdown.ViewOverlapCount,
                score.Breakdown.ViewOverlapArea,
                score.Breakdown.ViewOverlapPenalty,
                score.Breakdown.ReservedOverlapCount,
                score.Breakdown.ReservedOverlapArea,
                score.Breakdown.ReservedOverlapPenalty,
                validation.Diagnostics.Count));
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
                    string.IsNullOrWhiteSpace(item.Reason) ? "unknown" : item.Reason,
                    string.IsNullOrWhiteSpace(candidate.Name) ? "unnamed" : candidate.Name,
                    evaluation.IsFeasible ? 1 : 0,
                    evaluation.Score.TotalScore,
                    evaluation.Validation.Diagnostics.Count));
        }
    }

    private KeepScaleFitResult ValidateCurrentScaleFit(
        Tekla.Structures.Drawing.Drawing drawing,
        DrawingLayoutWorkspace workspace,
        IReadOnlyList<View> currentViews,
        IReadOnlyDictionary<int, ReservedRect> actualRects,
        double gap,
        double availW,
        double availH,
        double currentScale,
        string stage,
        string oversizeMessage,
        string fitFailedMessage)
    {
        var keepFrameSizes = DrawingViewFrameGeometry.TryGetFrameSizes(currentViews, actualRects);
        var keepFrames = BuildFrameList(currentViews, keepFrameSizes);
        var keepCtx = new DrawingArrangeContext(drawing, workspace, currentViews, gap, keepFrameSizes);
        var keepFitSw = Stopwatch.StartNew();
        var oversizeConflicts = BuildOversizeConflicts(currentViews, keepFrames, availW, availH);
        if (oversizeConflicts.Count > 0)
        {
            TraceEstimateFailureDecision(new EstimateFitFailureDecision(
                stage,
                currentScale,
                fits: false,
                oversizeConflicts,
                diagnosedConflicts: null));
            throw new DrawingFitFailedException(oversizeMessage, oversizeConflicts);
        }

        var fits = _arrangementSelector.EstimateFit(keepCtx, keepFrames);
        TraceScaleCandidate(currentScale, currentViews, keepFrames, fits, oversizeConflicts);
        keepFitSw.Stop();

        if (!fits)
        {
            var conflicts = _arrangementSelector.DiagnoseFitConflicts(keepCtx, keepFrames);
            TraceEstimateFailureDecision(new EstimateFitFailureDecision(
                stage,
                currentScale,
                fits: false,
                oversizeConflicts: null,
                diagnosedConflicts: conflicts));
            throw new DrawingFitFailedException(fitFailedMessage, conflicts);
        }

        var optimalScale = currentViews
            .Select(v => v.Attributes.Scale)
            .Where(s => s > 0)
            .DefaultIfEmpty(1.0)
            .Max();
        return new KeepScaleFitResult(keepFrameSizes, optimalScale, keepFitSw.ElapsedMilliseconds);
    }

    private static CandidateScaleProbeResult ProbeCandidateScale(
        Tekla.Structures.Drawing.Drawing drawing,
        DrawingLayoutWorkspace workspace,
        List<View> currentViews,
        IReadOnlyDictionary<int, (double Width, double Height)> originalFrameSizes,
        double candidateScale,
        bool uniformAllNonDetail)
    {
        var probeSw = Stopwatch.StartNew();
        var anyScaleChanged = false;
        foreach (var view in currentViews)
        {
            var targetScale = ResolveTargetScale(
                view,
                workspace.GetSemanticKind(view.GetIdentifier().ID),
                candidateScale,
                uniformAllNonDetail,
                workspace.OriginalScalesById);
            if (System.Math.Abs(view.Attributes.Scale - targetScale) < 0.01)
                continue;

            view.Attributes.Scale = targetScale;
            view.Modify();
            anyScaleChanged = true;
        }

        if (anyScaleChanged)
            drawing.CommitChanges();
        probeSw.Stop();

        var candidateViews = anyScaleChanged
            ? EnumerateViews(drawing).ToList()
            : currentViews;
        var effectiveFrameSizes = anyScaleChanged
            ? DrawingViewFrameGeometry.TryGetFrameSizes(candidateViews)
            : originalFrameSizes;
        var actualFrames = BuildFrameList(candidateViews, effectiveFrameSizes);
        return new CandidateScaleProbeResult(
            candidateViews,
            effectiveFrameSizes,
            actualFrames,
            probeSw.ElapsedMilliseconds);
    }

    /// <param name="margin">Margin from sheet edges in mm. Pass <c>null</c> to auto-read from drawing layout. Pass 0 for a true zero margin.</param>
    /// <param name="scalePolicy">Controls whether scales are unified, partially unified, or preserved as-is.</param>
    public FitViewsResult FitViewsToSheet(
        double? margin,
        double gap,
        double titleBlockHeight,
        DrawingScalePolicy scalePolicy = DrawingScalePolicy.UniformAllNonDetail,
        DrawingLayoutApplyMode applyMode = DrawingLayoutApplyMode.DebugPreview)
    {
        var total = Stopwatch.StartNew();
        long initMs = 0;
        long reservedMs = 0;
        long candidateFitMs = 0;
        long probeMs = 0;
        long arrangeMs = 0;
        long postAdjustMs = 0;
        long projectionMs = 0;
        long finalCommitMs = 0;
        var viewsCount = 0;
        var candidateAttempts = 0;
        double? selectedScale = null;
        ProjectionAlignmentResult? projectionResult = null;

        if (margin.HasValue && margin.Value < 0)
            throw new System.ArgumentOutOfRangeException(nameof(margin), "margin must be >= 0.");
        if (gap < 0)
            throw new System.ArgumentOutOfRangeException(nameof(gap), "gap must be >= 0.");
        if (titleBlockHeight < 0)
            throw new System.ArgumentOutOfRangeException(nameof(titleBlockHeight), "titleBlockHeight must be >= 0.");

        var preserveExistingScales = scalePolicy == DrawingScalePolicy.PreserveExistingScales;
        var uniformAllNonDetail = scalePolicy == DrawingScalePolicy.UniformAllNonDetail;
        var keepCurrentScales = scalePolicy == DrawingScalePolicy.UniformMainWithSectionExceptions;
        var activeDrawing = new DrawingHandler().GetActiveDrawing();
        if (activeDrawing == null)
            throw new DrawingNotOpenException();

        var init = Stopwatch.StartNew();

        var views = EnumerateViews(activeDrawing).ToList();
        viewsCount = views.Count;
        if (views.Count == 0)
            throw new System.InvalidOperationException("No views found in active drawing.");
        var actualRects = DrawingViewFrameGeometry.BuildActualViewRects(activeDrawing);
        var viewsResult = BuildViewsResult(activeDrawing, views, actualRects);
        List<View> scaleDriverViews;

        var viewIds = views.Select(v => v.GetIdentifier().ID).ToHashSet();
        var reservedRead = Stopwatch.StartNew();
        var drawingContext = new DrawingLayoutContextBuilder().Build(
            activeDrawing,
            viewsResult,
            margin,
            titleBlockHeight,
            viewIds);
        reservedRead.Stop();
        reservedMs = reservedRead.ElapsedMilliseconds;
        var layoutWorkspace = DrawingLayoutWorkspace.From(drawingContext, views);
        layoutWorkspace.SetActualViewRects(actualRects);
        var effectiveMargin = layoutWorkspace.Margin;
        var sheetW = layoutWorkspace.SheetWidth;
        var sheetH = layoutWorkspace.SheetHeight;

        if (sheetW <= 0 || sheetH <= 0)
            throw new System.InvalidOperationException("Unable to read drawing sheet size.");

        var availW = sheetW - (2 * effectiveMargin);
        var availH = sheetH - (2 * effectiveMargin);
        if (availW <= 0 || availH <= 0)
            throw new System.InvalidOperationException("No drawable area left after applying margin.");

        layoutWorkspace.SetOriginalScales(views.ToDictionary(v => v.GetIdentifier().ID, v => v.Attributes.Scale));
        // Build actual view rects once via sheet.GetAllObjects() — these always reflect the
        // physical frame position and are never stale, unlike GetAxisAlignedBoundingBox() on
        // views from GetViews() which may be stale after Modify/CommitChanges.
        var originalFrameSizes = DrawingViewFrameGeometry.TryGetFrameSizes(views, actualRects);
        layoutWorkspace.SetSelectedFrameSizes(originalFrameSizes);
        var oversizedStandardSectionScaleDriverIds = uniformAllNonDetail
            ? CollectOversizedStandardSectionScaleDriverIds(activeDrawing, layoutWorkspace, views, gap)
            : new HashSet<int>();
        scaleDriverViews = uniformAllNonDetail
            ? views
                .Where(v =>
                    layoutWorkspace.GetSemanticKind(v.GetIdentifier().ID) != ViewSemanticKind.Detail
                    && !oversizedStandardSectionScaleDriverIds.Contains(v.GetIdentifier().ID))
                .ToList()
            : views
                .Where(v => layoutWorkspace.GetSemanticKind(v.GetIdentifier().ID) == ViewSemanticKind.BaseProjected)
                .ToList();
        if (scaleDriverViews.Count == 0)
        {
            scaleDriverViews = views
                .Where(v => layoutWorkspace.GetSemanticKind(v.GetIdentifier().ID) != ViewSemanticKind.Detail)
                .ToList();
        }
        if (scaleDriverViews.Count == 0)
            scaleDriverViews = views;
        var scaleDrivers = scaleDriverViews
            .Select(v =>
            {
                var viewId = v.GetIdentifier().ID;
                var scale = v.Attributes.Scale > 0 ? v.Attributes.Scale : 1.0;
                var frame = layoutWorkspace.GetSelectedFrameSize(viewId, v.Width, v.Height);
                return new DrawingScaleDriver(frame.Width, frame.Height, scale);
            })
            .ToList();
        var scaleSelection = DrawingScaleCandidateSelector.Select(scaleDrivers, availW, availH);
        var currentScale = scaleSelection.CurrentScale;
        var minDenom = scaleSelection.MinDenom;
        var candidates = scaleSelection.Candidates;
        init.Stop();
        initMs = init.ElapsedMilliseconds;
        TraceScaleSelectionInputs(
            layoutWorkspace,
            views,
            scaleDriverViews,
            candidates,
            sheetW,
            sheetH,
            effectiveMargin,
            gap,
            availW,
            availH,
            currentScale,
            minDenom,
            scalePolicy,
            applyMode);

        double? optimalScale = null;
        var currentViews = views;

        if (preserveExistingScales)
        {
            // Validate that views fit at their current scales before committing to arrange.
            var keepResult = ValidateCurrentScaleFit(
                activeDrawing,
                layoutWorkspace,
                currentViews,
                actualRects,
                gap,
                availW,
                availH,
                currentScale,
                stage: "preserve-scales",
                oversizeMessage: "One or more views are larger than the usable sheet area at current scales.",
                fitFailedMessage: "Could not fit views on sheet at current scales. Use a non-preserving scale policy to allow rescaling.");
            candidateFitMs = keepResult.ElapsedMilliseconds;
            candidateAttempts = 1;

            // ShouldSkipProjectionAlignment skips when scale >= cutoff (100), meaning all views
            // are small enough that alignment corrections are negligible. Use Max() so projection
            // is skipped only when every view is at or above the cutoff — the one correct
            // condition under which alignment adds no value for any view on the sheet.
            optimalScale = keepResult.OptimalScale;
            layoutWorkspace.SetSelectedFrameSizes(keepResult.FrameSizes);
        }
        else if (keepCurrentScales)
        {
            var keepResult = ValidateCurrentScaleFit(
                activeDrawing,
                layoutWorkspace,
                currentViews,
                actualRects,
                gap,
                availW,
                availH,
                currentScale,
                stage: "keep-current-scales",
                oversizeMessage: "One or more views are larger than the usable sheet area at current scales.",
                fitFailedMessage: "Could not fit views on sheet at current scales.");
            candidateFitMs = keepResult.ElapsedMilliseconds;
            candidateAttempts = 1;

            optimalScale = keepResult.OptimalScale;
            layoutWorkspace.SetSelectedFrameSizes(keepResult.FrameSizes);
        }
        else
        {
            List<DrawingFitConflict>? lastOversizeConflicts = null;
            EstimateFitFailureDecision? lastDiagnosedDecision = null;
            foreach (var s in candidates)
            {
                candidateAttempts++;
                var candidateSw = Stopwatch.StartNew();
                var probe = ProbeCandidateScale(
                    activeDrawing,
                    layoutWorkspace,
                    currentViews,
                    originalFrameSizes,
                    s,
                    uniformAllNonDetail);
                probeMs += probe.ElapsedMilliseconds;
                var candidateViews = probe.Views;
                var actualFrames = probe.Frames;
                var ctx = new DrawingArrangeContext(activeDrawing, layoutWorkspace, candidateViews, gap, probe.FrameSizes);

                var oversizeConflicts = BuildOversizeConflicts(candidateViews, actualFrames, availW, availH);
                if (oversizeConflicts.Count > 0)
                {
                    TraceEstimateFailureDecision(new EstimateFitFailureDecision(
                        stage: "candidate-reject",
                        candidateScale: s,
                        fits: false,
                        oversizeConflicts,
                        diagnosedConflicts: null));
                    TraceScaleCandidate(s, candidateViews, actualFrames, fits: false, oversizeConflicts);
                    lastOversizeConflicts = oversizeConflicts;
                    candidateSw.Stop();
                    candidateFitMs += candidateSw.ElapsedMilliseconds;
                    continue;
                }

                var fits = _arrangementSelector.EstimateFit(ctx, actualFrames);
                TraceScaleCandidate(s, candidateViews, actualFrames, fits);
                if (!fits && PerfTrace.IsActive)
                {
                    var conflicts = _arrangementSelector.DiagnoseFitConflicts(ctx, actualFrames);
                    lastDiagnosedDecision = new EstimateFitFailureDecision(
                        stage: "candidate-reject",
                        candidateScale: s,
                        fits: false,
                        oversizeConflicts: null,
                        diagnosedConflicts: conflicts);
                    TraceEstimateFailureDecision(lastDiagnosedDecision.Value);
                }

                if (fits)
                {
                    optimalScale = s;
                    currentViews = candidateViews;
                    layoutWorkspace.SetRuntimeViews(currentViews);
                    var selectedFrameSizesById = candidateViews
                        .Select((v, i) => new { Id = v.GetIdentifier().ID, Frame = actualFrames[i] })
                        .ToDictionary(x => x.Id, x => (x.Frame.w, x.Frame.h));
                    layoutWorkspace.SetSelectedFrameSizes(selectedFrameSizesById);
                    candidateSw.Stop();
                    candidateFitMs += candidateSw.ElapsedMilliseconds;
                    break;
                }
                currentViews = candidateViews;
                layoutWorkspace.SetRuntimeViews(currentViews);
                candidateSw.Stop();
                candidateFitMs += candidateSw.ElapsedMilliseconds;
            }

            if (!optimalScale.HasValue)
            {
                foreach (var v in EnumerateViews(activeDrawing))
                {
                    if (layoutWorkspace.OriginalScalesById.TryGetValue(v.GetIdentifier().ID, out var orig))
                    {
                        v.Attributes.Scale = orig;
                        v.Modify();
                    }
                }

                activeDrawing.CommitChanges();
                if (lastOversizeConflicts is { Count: > 0 })
                    throw new DrawingFitFailedException("One or more views are larger than the usable sheet area for every available standard scale.", lastOversizeConflicts);

                if (PerfTrace.IsActive && lastDiagnosedDecision.HasValue)
                {
                    TraceEstimateFailureDecision(new EstimateFitFailureDecision(
                        stage: "candidate-final-reject",
                        candidateScale: lastDiagnosedDecision.Value.CandidateScale,
                        fits: false,
                        oversizeConflicts: lastDiagnosedDecision.Value.OversizeConflicts,
                        diagnosedConflicts: lastDiagnosedDecision.Value.DiagnosedConflicts));
                }

                throw new System.InvalidOperationException("Could not fit views on sheet with available standard scales.");
            }

            currentViews = EnumerateViews(activeDrawing).ToList();
            layoutWorkspace.SetRuntimeViews(currentViews);
        }

        PerfTrace.Write(
            "api-view",
            "fit_scale_selected",
            0,
            $"selectedScale=1:{optimalScale.Value.ToString("0.###", CultureInfo.InvariantCulture)} attempts={candidateAttempts} policy={scalePolicy} applyMode={applyMode}");

        actualRects = DrawingViewFrameGeometry.BuildActualViewRects(activeDrawing);
        layoutWorkspace.SetActualViewRects(actualRects);

        var offsetById = DrawingViewFrameGeometry.TryGetFrameOffsets(currentViews, actualRects);
        layoutWorkspace.SetFrameOffsets(offsetById);
        // Read offsets from actual sheet geometry after the final scale state is already applied.
        // Keep-scale mode still needs real frame offsets; otherwise projection-pass collision checks
        // degrade to origin-centered boxes and may allow one view to move inside another.

        var arrangedViews = currentViews
            .Where(v => layoutWorkspace.GetSemanticKind(v.GetIdentifier().ID) != ViewSemanticKind.Detail)
            .ToList();

        var gridApi = new TeklaDrawingGridApi();
        var preloadedAxes = arrangedViews
            .Select(v => (Id: v.GetIdentifier().ID, Result: gridApi.GetGridAxes(v.GetIdentifier().ID)))
            .Where(x => x.Result.Success)
            .ToDictionary(x => x.Id, x => (IReadOnlyList<GridAxisInfo>)x.Result.Axes);
        layoutWorkspace.SetGridAxes(preloadedAxes);

        var arrangeSw = Stopwatch.StartNew();
        var arranged = arrangedViews.Count == 0
            ? new List<ArrangedView>()
            : _arrangementSelector.Arrange(
                new DrawingArrangeContext(activeDrawing, layoutWorkspace, arrangedViews, gap));
        arrangeSw.Stop();
        arrangeMs = arrangeSw.ElapsedMilliseconds;

        if (!preserveExistingScales)
        {
            foreach (var detailView in currentViews.Where(v => layoutWorkspace.GetSemanticKind(v.GetIdentifier().ID) == ViewSemanticKind.Detail))
            {
                if (!layoutWorkspace.OriginalScalesById.TryGetValue(detailView.GetIdentifier().ID, out var detailScale))
                    continue;

                if (detailScale <= 0 || System.Math.Abs(detailView.Attributes.Scale - detailScale) < 0.01)
                    continue;

                detailView.Attributes.Scale = detailScale;
                detailView.Modify();
            }
        }

        if (offsetById.Count > 0)
        {
            var adjustSw = Stopwatch.StartNew();

            for (int i = 0; i < arranged.Count; i++)
            {
                if (!layoutWorkspace.RuntimeViewsById.TryGetValue(arranged[i].Id, out var v))
                    continue;
                if (!offsetById.TryGetValue(arranged[i].Id, out var off))
                    continue;

                var correctionScale = v.Attributes.Scale > 0 ? v.Attributes.Scale : optimalScale.Value;
                var corrX = off.X / correctionScale;
                var corrY = off.Y / correctionScale;
                var semanticKind = layoutWorkspace.GetSemanticKind(arranged[i].Id);
                // Skip implausibly large corrections: they indicate a bad frame-offset
                // estimate (e.g. from probe-scale extrapolation on distant-origin views)
                // and would displace the view far from where the packer intended.
                if (semanticKind != ViewSemanticKind.Detail
                    && (System.Math.Abs(corrX) > v.Width || System.Math.Abs(corrY) > v.Height))
                    continue;

                var o = v.Origin;
                o.X = arranged[i].OriginX - corrX;
                o.Y = arranged[i].OriginY - corrY;
                v.Origin = o;
                v.Modify();
                arranged[i] = new ArrangedView
                {
                    Id = arranged[i].Id,
                    ViewType = arranged[i].ViewType,
                    OriginX = o.X,
                    OriginY = o.Y,
                    PreferredPlacementSide = arranged[i].PreferredPlacementSide,
                    ActualPlacementSide = arranged[i].ActualPlacementSide,
                    PlacementFallbackUsed = arranged[i].PlacementFallbackUsed
                };
            }

            adjustSw.Stop();
            postAdjustMs = adjustSw.ElapsedMilliseconds;
        }

        TracePlannedVsActualParity(
            "post-arrange-pre-projection",
            layoutWorkspace,
            arranged,
            DrawingViewFrameGeometry.BuildActualViewRects(activeDrawing));

        var projectionSw = Stopwatch.StartNew();
        if (ShouldSkipProjectionAlignment(optimalScale.Value, arrangedViews, out var projectionSkipMode, out var projectionSkipDiagnostic))
        {
            projectionResult = new ProjectionAlignmentResult
            {
                Mode = projectionSkipMode,
                SkippedMoves = 1
            };
            if (!string.IsNullOrWhiteSpace(projectionSkipDiagnostic))
                projectionResult.Diagnostics.Add(projectionSkipDiagnostic);
        }
        else
        {
            var projectionAlignmentService = new DrawingProjectionAlignmentService(new Model());
            projectionResult = projectionAlignmentService.Apply(
                activeDrawing,
                layoutWorkspace,
                arrangedViews,
                arranged);
        }
        projectionSw.Stop();
        projectionMs = projectionSw.ElapsedMilliseconds;

        var commitSw = Stopwatch.StartNew();
        activeDrawing.CommitChanges();
        commitSw.Stop();
        finalCommitMs = commitSw.ElapsedMilliseconds;
        selectedScale = optimalScale;

        var postProjectionViews = EnumerateViews(activeDrawing).ToList();
        layoutWorkspace.SetRuntimeViews(postProjectionViews);
        var postProjectionArranged = arranged
            .Select(static view => new ArrangedView
            {
                Id = view.Id,
                ViewType = view.ViewType,
                OriginX = view.OriginX,
                OriginY = view.OriginY,
                PreferredPlacementSide = view.PreferredPlacementSide,
                ActualPlacementSide = view.ActualPlacementSide,
                PlacementFallbackUsed = view.PlacementFallbackUsed
            })
            .ToList();
        var postProjectionActualRects = DrawingViewFrameGeometry.BuildActualViewRects(activeDrawing);
        var postProjectionCandidate = DrawingLayoutCandidateBuilder.FromRuntimeLayout(
            "fit_views_to_sheet:post-projection",
            layoutWorkspace,
            postProjectionViews,
            postProjectionArranged,
            postProjectionActualRects);

        // Center the arranged group inside the usable area.
        var finalArrangedViews = postProjectionViews
            .Where(v => layoutWorkspace.GetSemanticKind(v.GetIdentifier().ID) != ViewSemanticKind.Detail)
            .ToList();
        arranged = TryCenterViewGroup(activeDrawing, finalArrangedViews, arranged,
            effectiveMargin, sheetW - effectiveMargin,
            effectiveMargin, sheetH - effectiveMargin,
            layoutWorkspace.ReservedAreas);
        var finalViews = EnumerateViews(activeDrawing).ToList();
        layoutWorkspace.SetRuntimeViews(finalViews);
        arranged = TryRepositionDetailViews(
            activeDrawing,
            finalViews,
            arranged,
            effectiveMargin,
            sheetW - effectiveMargin,
            effectiveMargin,
            sheetH - effectiveMargin,
            gap,
            layoutWorkspace.ReservedAreas,
            offsetById);

        var finalActualRects = DrawingViewFrameGeometry.BuildActualViewRects(activeDrawing);
        TracePlannedVsActualParity(
            "post-commit-final",
            layoutWorkspace,
            arranged,
            finalActualRects);

        var passiveCandidate = DrawingLayoutCandidateBuilder.FromRuntimeLayout(
            "fit_views_to_sheet:final",
            layoutWorkspace,
            layoutWorkspace.RuntimeViews,
            arranged,
            finalActualRects);
        var passiveSelection = new DrawingLayoutCandidateSelector().SelectBest(
            new[] { postProjectionCandidate, passiveCandidate });
        TraceLayoutCandidateSelection(passiveSelection);
        foreach (var evaluation in passiveSelection.Evaluations)
            TraceLayoutCandidateScore(evaluation);

        // Build reserved-areas output using already-read layoutTables (no extra editor open).
        // Read() without excludeViewIds to include view bounding boxes in the merged output.
        var mergedForOutput = DrawingReservedAreaReader.Read(activeDrawing, effectiveMargin, 0.0,
            preloadedTables: layoutWorkspace.ReservedTables);

        var result = new FitViewsResult
        {
            OptimalScale = optimalScale.Value,
            ScalePreserved = preserveExistingScales,
            ScalePolicy = scalePolicy.ToString(),
            ApplyMode = applyMode.ToString(),
            SheetWidth = sheetW,
            SheetHeight = sheetH,
            Margin = effectiveMargin,
            Arranged = arranged.Count,
            Views = arranged,
            ProjectionApplied = projectionResult?.AppliedMoves ?? 0,
            ProjectionSkipped = projectionResult?.SkippedMoves ?? 0,
            ProjectionDiagnostics = projectionResult?.Diagnostics.Count > 0 ? projectionResult.Diagnostics : null,
            ReservedAreas = new DrawingReservedAreasResult
            {
                SheetWidth  = sheetW,
                SheetHeight = sheetH,
                Margin      = effectiveMargin,
                SheetMargin = layoutWorkspace.SheetMargin,
                Tables      = layoutWorkspace.ReservedTables,
                MergedAreas = mergedForOutput
            }
        };

        total.Stop();
        result.TotalMs = total.ElapsedMilliseconds;
        result.PhaseMs = new System.Collections.Generic.Dictionary<string, long>
        {
            ["init"]        = initMs,
            ["reserved"]    = reservedMs,
            ["probe"]       = probeMs,
            ["candidateFit"]= candidateFitMs,
            ["arrange"]     = arrangeMs,
            ["postAdjust"]  = postAdjustMs,
            ["projection"]  = projectionMs,
            ["finalCommit"] = finalCommitMs,
        };

        PerfTrace.Write(
            "api-view",
            "fit_views_to_sheet",
            total.ElapsedMilliseconds,
            $"views={viewsCount} candidates={candidateAttempts} selectedScale={(selectedScale.HasValue ? selectedScale.Value.ToString(CultureInfo.InvariantCulture) : "n/a")} scalePolicy={scalePolicy} applyMode={applyMode} initMs={initMs} reservedMs={reservedMs} candidateFitMs={candidateFitMs} probeMs={probeMs} arrangeMs={arrangeMs} postAdjustMs={postAdjustMs} projectionMs={projectionMs} projectionMode={(projectionResult?.Mode ?? "none")} projectionApplied={(projectionResult?.AppliedMoves ?? 0)} projectionSkipped={(projectionResult?.SkippedMoves ?? 0)} finalCommitMs={finalCommitMs}");
        return result;
    }

    internal static bool TryFindCenteringDelta(
        IReadOnlyList<ReservedRect> rects,
        double usableMin,
        double usableMax,
        IReadOnlyList<ReservedRect> reserved,
        bool horizontal,
        out double delta)
    {
        delta = 0;
        if (rects.Count == 0)
            return false;

        var groupMin = horizontal ? rects.Min(r => r.MinX) : rects.Min(r => r.MinY);
        var groupMax = horizontal ? rects.Max(r => r.MaxX) : rects.Max(r => r.MaxY);
        var groupSize = groupMax - groupMin;

        var targetMin = usableMin + (usableMax - usableMin - groupSize) / 2.0;
        var desired = targetMin - groupMin;

        desired = desired < 0
            ? System.Math.Max(desired, usableMin - groupMin)
            : System.Math.Min(desired, usableMax - groupMax);

        if (System.Math.Abs(desired) < 1.0)
            return false;

        double lo = 0;
        double hi = System.Math.Abs(desired);
        var sign = System.Math.Sign(desired);
        while (hi - lo > 0.5)
        {
            var mid = (lo + hi) / 2.0;
            var feasible = true;
            foreach (var r in rects)
            {
                var shifted = horizontal
                    ? new ReservedRect(r.MinX + sign * mid, r.MinY, r.MaxX + sign * mid, r.MaxY)
                    : new ReservedRect(r.MinX, r.MinY + sign * mid, r.MaxX, r.MaxY + sign * mid);
                foreach (var res in reserved)
                {
                    if (shifted.MinX < res.MaxX && shifted.MaxX > res.MinX &&
                        shifted.MinY < res.MaxY && shifted.MaxY > res.MinY)
                    {
                        feasible = false;
                        break;
                    }
                }

                if (!feasible)
                    break;
            }

            if (feasible) lo = mid; else hi = mid;
        }

        if (lo < 1.0)
            return false;

        delta = sign * lo;
        return true;
    }

    private static List<ReservedRect> GetViewRects(List<View> views)
    {
        var rects = new List<ReservedRect>(views.Count);
        foreach (var v in views)
        {
            if (!DrawingViewFrameGeometry.TryGetBoundingRect(v, out var rect))
                return new List<ReservedRect>();

            rects.Add(rect);
        }

        return rects;
    }

    private static List<ReservedRect> ShiftRects(IReadOnlyList<ReservedRect> rects, double dx, double dy)
        => rects.Select(r => new ReservedRect(r.MinX + dx, r.MinY + dy, r.MaxX + dx, r.MaxY + dy)).ToList();

    private List<ArrangedView> TryRepositionDetailViews(
        Tekla.Structures.Drawing.Drawing activeDrawing,
        List<View> views,
        List<ArrangedView> arranged,
        double usableMinX,
        double usableMaxX,
        double usableMinY,
        double usableMaxY,
        double gap,
        IReadOnlyList<ReservedRect> reserved,
        IReadOnlyDictionary<int, (double X, double Y)> preMovedFrameOffsets)
    {
        var topology = ViewTopologyGraph.Build(views);
        var detailViews = topology.SemanticViews.Details.ToList();
        if (detailViews.Count == 0)
            return arranged;

        var relations = topology.DetailRelations;
        if (relations.Count == 0)
            return arranged;

        var viewById = views.ToDictionary(v => v.GetIdentifier().ID);
        var blocked = new List<ReservedRect>(reserved);
        foreach (var view in views.Where(v => topology.SemanticViews.GetKind(v.GetIdentifier().ID) != ViewSemanticKind.Detail))
        {
            if (DrawingViewFrameGeometry.TryGetBoundingRect(view, out var rect))
                blocked.Add(rect);
        }

        var movedAny = false;
        for (var i = 0; i < detailViews.Count; i++)
        {
            var detailView = detailViews[i];
            var detailId = detailView.GetIdentifier().ID;
            if (!relations.TryGet(detailId, out var relation))
            {
                if (DrawingViewFrameGeometry.TryGetBoundingRect(detailView, out var currentRect))
                    blocked.Add(currentRect);
                continue;
            }

            var ownerView = relation.OwnerView;
            if (!viewById.ContainsKey(ownerView.GetIdentifier().ID))
            {
                if (DrawingViewFrameGeometry.TryGetBoundingRect(detailView, out var currentRect))
                    blocked.Add(currentRect);
                continue;
            }

            if (!DrawingViewFrameGeometry.TryGetBoundingRect(ownerView, out var ownerRect)
                || !DrawingViewFrameGeometry.TryGetBoundingRect(detailView, out var detailRect))
            {
                continue;
            }

            var detailWidth = detailRect.MaxX - detailRect.MinX;
            var detailHeight = detailRect.MaxY - detailRect.MinY;
            if (detailWidth <= 0 || detailHeight <= 0)
            {
                blocked.Add(detailRect);
                continue;
            }

            var anchorX = CenterX(ownerRect);
            var anchorY = CenterY(ownerRect);
            if (relation.AnchorX.HasValue)
                anchorX = relation.AnchorX.Value;
            if (relation.AnchorY.HasValue)
                anchorY = relation.AnchorY.Value;

            var decision = BaseProjectedDrawingArrangeStrategy.ProbeDetailPlacement(
                ownerRect,
                detailWidth,
                detailHeight,
                gap * 2.0,
                usableMinX,
                usableMaxX,
                usableMinY,
                usableMaxY,
                blocked,
                anchorX,
                anchorY);
            if (!decision.Success)
            {
                blocked.Add(detailRect);
                continue;
            }

            var candidateRect = decision.Rect;

            var targetCenterX = (candidateRect.MinX + candidateRect.MaxX) * 0.5;
            var targetCenterY = (candidateRect.MinY + candidateRect.MaxY) * 0.5;
            var currentCenterX = (detailRect.MinX + detailRect.MaxX) * 0.5;
            var currentCenterY = (detailRect.MinY + detailRect.MaxY) * 0.5;
            if (System.Math.Abs(currentCenterX - targetCenterX) < 0.5
                && System.Math.Abs(currentCenterY - targetCenterY) < 0.5)
            {
                blocked.Add(detailRect);
                continue;
            }

            var origin = detailView.Origin;
            if (origin == null)
            {
                blocked.Add(detailRect);
                continue;
            }

            // Use the frame offset captured BEFORE any moves in this fit cycle.
            // Re-reading the bbox here would return a stale value (center == origin, offset = 0)
            // because Tekla doesn't update the bbox immediately after Modify/CommitChanges.
            // offsetById stores (center - origin) * scale, so sheet-space offset = stored / scale.
            var detailScale = detailView.Attributes.Scale > 0 ? detailView.Attributes.Scale : 1.0;
            if (preMovedFrameOffsets.TryGetValue(detailId, out var preOffset))
            {
                origin.X = targetCenterX - preOffset.X / detailScale;
                origin.Y = targetCenterY - preOffset.Y / detailScale;
            }
            else if (DrawingViewFrameGeometry.TryGetCenterOffsetFromOrigin(detailView, out var offsetX, out var offsetY))
            {
                origin.X = targetCenterX - offsetX;
                origin.Y = targetCenterY - offsetY;
            }
            else
            {
                origin.X = targetCenterX;
                origin.Y = targetCenterY;
            }

            detailView.Origin = origin;
            if (!detailView.Modify())
            {
                blocked.Add(detailRect);
                continue;
            }

            movedAny = true;
            blocked.Add(candidateRect);
            for (var ai = 0; ai < arranged.Count; ai++)
            {
                if (arranged[ai].Id != detailId)
                    continue;

                arranged[ai] = new ArrangedView
                {
                    Id = arranged[ai].Id,
                    ViewType = arranged[ai].ViewType,
                    OriginX = origin.X,
                    OriginY = origin.Y,
                    PreferredPlacementSide = arranged[ai].PreferredPlacementSide,
                    ActualPlacementSide = arranged[ai].ActualPlacementSide,
                    PlacementFallbackUsed = arranged[ai].PlacementFallbackUsed
                };
                break;
            }
        }

        if (movedAny)
            activeDrawing.CommitChanges();

        return arranged;
    }

    private static bool TryResolveDetailAnchorSheet(View ownerView, DetailMarkInfo detailMark, out double anchorX, out double anchorY)
    {
        if (TryResolveDetailAnchorSheet(ownerView, detailMark.LabelPoint, out anchorX, out anchorY))
            return true;
        if (TryResolveDetailAnchorSheet(ownerView, detailMark.BoundaryPoint, out anchorX, out anchorY))
            return true;
        return TryResolveDetailAnchorSheet(ownerView, detailMark.CenterPoint, out anchorX, out anchorY);
    }

    private static bool TryResolveDetailAnchorSheet(View ownerView, Tekla.Structures.Drawing.DetailMark detailMark, out double anchorX, out double anchorY)
    {
        if (detailMark.LabelPoint != null && TryResolveDetailAnchorSheet(ownerView, new[] { detailMark.LabelPoint.X, detailMark.LabelPoint.Y }, out anchorX, out anchorY))
            return true;
        if (detailMark.BoundaryPoint != null && TryResolveDetailAnchorSheet(ownerView, new[] { detailMark.BoundaryPoint.X, detailMark.BoundaryPoint.Y }, out anchorX, out anchorY))
            return true;
        if (detailMark.CenterPoint != null && TryResolveDetailAnchorSheet(ownerView, new[] { detailMark.CenterPoint.X, detailMark.CenterPoint.Y }, out anchorX, out anchorY))
            return true;
        anchorX = 0;
        anchorY = 0;
        return false;
    }

    private static bool TryResolveDetailAnchorSheet(View ownerView, double[] point, out double anchorX, out double anchorY)
    {
        anchorX = 0;
        anchorY = 0;
        if (point == null || point.Length < 2)
            return false;

        return BaseProjectedDrawingArrangeStrategy.TryProjectViewLocalPointToSheet(
            ownerView,
            new Point(point[0], point[1], 0),
            out anchorX,
            out anchorY);
    }

    private static Point? TrySectionMarkMidPoint(Tekla.Structures.Drawing.SectionMark sectionMark)
    {
        try
        {
            var lp = sectionMark.LeftPoint;
            var rp = sectionMark.RightPoint;
            if (lp != null && rp != null)
                return new Point((lp.X + rp.X) * 0.5, (lp.Y + rp.Y) * 0.5, 0);
            return lp ?? rp;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// After packing, the view group may be biased toward one side because reserved areas
    /// only block a corner. Shift the whole group toward the center of the usable area
    /// on X and Y independently, without overlapping reserved areas.
    /// </summary>
    private static List<ArrangedView> TryCenterViewGroup(
        Tekla.Structures.Drawing.Drawing activeDrawing,
        List<View> views,
        List<ArrangedView> arranged,
        double usableMinX, double usableMaxX,
        double usableMinY, double usableMaxY,
        IReadOnlyList<ReservedRect> reserved)
    {
        if (views.Count == 0)
            return arranged;

        var rects = GetViewRects(views);
        if (rects.Count != views.Count)
            return arranged;

        var dx = 0.0;
        if (TryFindCenteringDelta(rects, usableMinX, usableMaxX, reserved, horizontal: true, out var foundDx))
        {
            dx = foundDx;
            rects = ShiftRects(rects, dx, 0);
        }

        var dy = 0.0;
        if (TryFindCenteringDelta(rects, usableMinY, usableMaxY, reserved, horizontal: false, out var foundDy))
            dy = foundDy;

        if (System.Math.Abs(dx) < 1.0 && System.Math.Abs(dy) < 1.0)
            return arranged;

        foreach (var v in views)
        {
            var o = v.Origin;
            o.X += dx;
            o.Y += dy;
            v.Origin = o;
            v.Modify();
        }

        activeDrawing.CommitChanges();

        PerfTrace.Write("api-view", "center_group", 0,
            $"dx={dx:F1} dy={dy:F1} usableX={usableMinX:F1}-{usableMaxX:F1} usableY={usableMinY:F1}-{usableMaxY:F1}");

        return arranged.Select(a => new ArrangedView
        {
            Id       = a.Id,
            ViewType = a.ViewType,
            OriginX  = a.OriginX + dx,
            OriginY  = a.OriginY + dy,
            PreferredPlacementSide = a.PreferredPlacementSide,
            ActualPlacementSide = a.ActualPlacementSide,
            PlacementFallbackUsed = a.PlacementFallbackUsed
        }).ToList();
    }

    private static double CenterX(ReservedRect rect) => (rect.MinX + rect.MaxX) / 2.0;

    private static double CenterY(ReservedRect rect) => (rect.MinY + rect.MaxY) / 2.0;
}
