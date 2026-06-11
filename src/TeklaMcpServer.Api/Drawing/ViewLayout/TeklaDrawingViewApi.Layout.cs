using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
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

        if (semanticKind == ViewSemanticKind.Other)
            return originalScale;

        if (uniformAllNonDetail)
            return candidateScale;

        if (semanticKind == ViewSemanticKind.BaseProjected)
            return candidateScale;

        return originalScale;
    }

    internal static bool IsUniformScaleDriverKind(ViewSemanticKind semanticKind)
        => semanticKind is ViewSemanticKind.BaseProjected or ViewSemanticKind.Section;

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
        bool uniformAllNonDetail,
        bool applyProbe)
    {
        var probeSw = Stopwatch.StartNew();
        if (!applyProbe)
        {
            var estimatedFrameSizes = EstimateCandidateFrameSizes(
                workspace,
                currentViews,
                originalFrameSizes,
                candidateScale,
                uniformAllNonDetail);
            var estimatedFrames = BuildFrameList(currentViews, estimatedFrameSizes);
            probeSw.Stop();
            PerfTrace.Write(
                "api-view",
                "fit_scale_probe",
                probeSw.ElapsedMilliseconds,
                $"mode=virtual candidateScale=1:{candidateScale.ToString("0.###", CultureInfo.InvariantCulture)} views={currentViews.Count}");
            return new CandidateScaleProbeResult(
                currentViews,
                estimatedFrameSizes,
                estimatedFrames,
                probeSw.ElapsedMilliseconds);
        }

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

    private static IReadOnlyDictionary<int, (double Width, double Height)> EstimateCandidateFrameSizes(
        DrawingLayoutWorkspace workspace,
        IReadOnlyList<View> views,
        IReadOnlyDictionary<int, (double Width, double Height)> originalFrameSizes,
        double candidateScale,
        bool uniformAllNonDetail)
    {
        var result = new Dictionary<int, (double Width, double Height)>(views.Count);
        foreach (var view in views)
        {
            var id = view.GetIdentifier().ID;
            var originalScale = workspace.OriginalScalesById.TryGetValue(id, out var storedScale) && storedScale > 0
                ? storedScale
                : (view.Attributes.Scale > 0 ? view.Attributes.Scale : 1.0);
            var targetScale = ResolveTargetScale(
                view,
                workspace.GetSemanticKind(id),
                candidateScale,
                uniformAllNonDetail,
                workspace.OriginalScalesById);
            var frame = originalFrameSizes.TryGetValue(id, out var storedFrame)
                ? storedFrame
                : (view.Width, view.Height);
            var factor = targetScale > 0 ? originalScale / targetScale : 1.0;
            result[id] = (frame.Width * factor, frame.Height * factor);
        }

        return result;
    }

    private static IReadOnlyDictionary<int, double> ResolveSelectedScales(
        DrawingLayoutWorkspace workspace,
        IReadOnlyList<View> views,
        double selectedScale,
        bool uniformAllNonDetail,
        bool preserveCurrentScales)
    {
        var result = new Dictionary<int, double>(views.Count);
        foreach (var view in views)
        {
            var id = view.GetIdentifier().ID;
            if (preserveCurrentScales)
            {
                result[id] = workspace.OriginalScalesById.TryGetValue(id, out var originalScale) && originalScale > 0
                    ? originalScale
                    : (view.Attributes.Scale > 0 ? view.Attributes.Scale : 1.0);
                continue;
            }

            result[id] = ResolveTargetScale(
                view,
                workspace.GetSemanticKind(id),
                selectedScale,
                uniformAllNonDetail,
                workspace.OriginalScalesById);
        }

        return result;
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
        var allowTeklaMutation = applyMode == DrawingLayoutApplyMode.FinalOnly;
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
            viewIds,
            includeSheetObjects: false);
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
        layoutWorkspace.SetSelectedScales(layoutWorkspace.OriginalScalesById);
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
                    IsUniformScaleDriverKind(layoutWorkspace.GetSemanticKind(v.GetIdentifier().ID))
                    && !oversizedStandardSectionScaleDriverIds.Contains(v.GetIdentifier().ID))
                .ToList()
            : views
                .Where(v => layoutWorkspace.GetSemanticKind(v.GetIdentifier().ID) == ViewSemanticKind.BaseProjected)
                .ToList();
        if (scaleDriverViews.Count == 0)
        {
            scaleDriverViews = views
                .Where(v => IsUniformScaleDriverKind(layoutWorkspace.GetSemanticKind(v.GetIdentifier().ID)))
                .ToList();
        }
        if (scaleDriverViews.Count == 0)
        {
            scaleDriverViews = views
                .Where(v => layoutWorkspace.GetSemanticKind(v.GetIdentifier().ID) != ViewSemanticKind.Detail)
                .ToList();
        }
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
        var rejectedScaleDecisions = new List<EstimateFitFailureDecision>();
        var scaleDecisionLayer = "not-evaluated";
        var currentViews = views;

        if (preserveExistingScales)
        {
            PerfTrace.Write(
                "api-view",
                "fit_scale_preserve_existing",
                0,
                $"policy={scalePolicy} mode=arrange-existing-views currentScale=1:{currentScale.ToString("0.###", CultureInfo.InvariantCulture)} views={currentViews.Count}");

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
            scaleDecisionLayer = "preserve-scales";
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
            scaleDecisionLayer = "keep-current-scales";
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
                    uniformAllNonDetail,
                    allowTeklaMutation);
                probeMs += probe.ElapsedMilliseconds;
                var candidateViews = probe.Views;
                var actualFrames = probe.Frames;
                var ctx = new DrawingArrangeContext(activeDrawing, layoutWorkspace, candidateViews, gap, probe.FrameSizes);

                var oversizeConflicts = BuildOversizeConflicts(candidateViews, actualFrames, availW, availH);
                if (oversizeConflicts.Count > 0)
                {
                    var decision = new EstimateFitFailureDecision(
                        stage: "candidate-reject",
                        candidateScale: s,
                        fits: false,
                        oversizeConflicts,
                        diagnosedConflicts: null);
                    rejectedScaleDecisions.Add(decision);
                    TraceEstimateFailureDecision(decision);
                    TraceScaleCandidate(s, candidateViews, actualFrames, fits: false, oversizeConflicts);
                    TraceRelaxedPackingFeasibility(s, ctx, actualFrames);
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
                    rejectedScaleDecisions.Add(lastDiagnosedDecision.Value);
                    TraceEstimateFailureDecision(lastDiagnosedDecision.Value);
                    TraceRelaxedPackingFeasibility(s, ctx, actualFrames);
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
                    scaleDecisionLayer = rejectedScaleDecisions.Count == 0
                        ? "scale-selection:first-candidate-fit"
                        : "scale-selection:after-rejections";
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
                    if (allowTeklaMutation && layoutWorkspace.OriginalScalesById.TryGetValue(v.GetIdentifier().ID, out var orig))
                    {
                        v.Attributes.Scale = orig;
                        v.Modify();
                    }
                }

                if (allowTeklaMutation)
                    activeDrawing.CommitChanges();
                TraceScaleDecision(
                    scalePolicy,
                    applyMode,
                    candidates,
                    selectedScale: null,
                    rejectedScaleDecisions,
                    decisionLayer: lastOversizeConflicts is { Count: > 0 }
                        ? "scale-selection:all-candidates-oversize"
                        : "scale-selection:all-candidates-rejected");

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

            currentViews = allowTeklaMutation
                ? EnumerateViews(activeDrawing).ToList()
                : currentViews;
            layoutWorkspace.SetRuntimeViews(currentViews);
        }

        TraceScaleDecision(
            scalePolicy,
            applyMode,
            candidates,
            optimalScale,
            rejectedScaleDecisions,
            scaleDecisionLayer);

        PerfTrace.Write(
            "api-view",
            "fit_scale_selected",
            0,
            $"selectedScale=1:{optimalScale.Value.ToString("0.###", CultureInfo.InvariantCulture)} attempts={candidateAttempts} policy={scalePolicy} applyMode={applyMode}");

        layoutWorkspace.SetSelectedScales(ResolveSelectedScales(
            layoutWorkspace,
            currentViews,
            optimalScale.Value,
            uniformAllNonDetail,
            preserveExistingScales || keepCurrentScales));

        actualRects = DrawingViewFrameGeometry.BuildActualViewRects(activeDrawing);
        layoutWorkspace.SetActualViewRects(actualRects);

        // Refresh SelectedFrameSizes from actual post-scale bbox. The virtual probe estimates
        // sizes by proportional scaling, which underestimates section views that have
        // scale-invariant components (marks, labels) in their bbox.
        if (allowTeklaMutation && actualRects.Count > 0)
        {
            var refreshedSizes = DrawingViewFrameGeometry.TryGetFrameSizes(currentViews, actualRects);
            if (refreshedSizes.Count > 0)
                layoutWorkspace.SetSelectedFrameSizes(refreshedSizes);
        }

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
                new DrawingArrangeContext(activeDrawing, layoutWorkspace, arrangedViews, gap, applyChanges: allowTeklaMutation));
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

                if (allowTeklaMutation)
                {
                    detailView.Attributes.Scale = detailScale;
                    detailView.Modify();
                }
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

                var fallbackScale = v.Attributes.Scale > 0 ? v.Attributes.Scale : optimalScale.Value;
                var correctionScale = layoutWorkspace.GetSelectedScale(arranged[i].Id, fallbackScale);
                var corrX = off.X / correctionScale;
                var corrY = off.Y / correctionScale;
                var semanticKind = layoutWorkspace.GetSemanticKind(arranged[i].Id);
                var selectedFrameSize = layoutWorkspace.GetSelectedFrameSize(arranged[i].Id, v.Width, v.Height);
                var maxPlausibleCorrection = System.Math.Max(selectedFrameSize.Width, selectedFrameSize.Height) * 2.0;
                // Skip only clearly broken offsets. Real Tekla view BBoxes can be
                // asymmetric enough that the center offset is slightly larger than
                // one frame dimension, especially for section/back views.
                if (semanticKind != ViewSemanticKind.Detail
                    && maxPlausibleCorrection > 0
                    && (System.Math.Abs(corrX) > maxPlausibleCorrection || System.Math.Abs(corrY) > maxPlausibleCorrection))
                {
                    PerfTrace.Write(
                        "api-view",
                        "view_frame_offset_skip",
                        0,
                        $"view={arranged[i].Id} reason=implausible offset=({corrX:F2},{corrY:F2}) limit={maxPlausibleCorrection:F2}");
                    continue;
                }

                var currentOrigin = v.Origin;
                var o = new Point(currentOrigin?.X ?? 0, currentOrigin?.Y ?? 0, currentOrigin?.Z ?? 0);
                o.X = arranged[i].OriginX - corrX;
                o.Y = arranged[i].OriginY - corrY;
                if (allowTeklaMutation)
                {
                    v.Origin = o;
                    v.Modify();
                }
                arranged[i] = new ArrangedView
                {
                    Id = arranged[i].Id,
                    ViewType = arranged[i].ViewType,
                    OriginX = o.X,
                    OriginY = o.Y,
                    PreferredPlacementSide = arranged[i].PreferredPlacementSide,
                    ActualPlacementSide = arranged[i].ActualPlacementSide,
                    PlacementFallbackUsed = arranged[i].PlacementFallbackUsed,
                    LayoutMargin = arranged[i].LayoutMargin,
                    LayoutGap = arranged[i].LayoutGap
                };
            }

            adjustSw.Stop();
            postAdjustMs = adjustSw.ElapsedMilliseconds;
        }

        var selectedLayoutMargin = ResolveSelectedLayoutMargin(effectiveMargin, arranged);
        var selectedLayoutGap = ResolveSelectedLayoutGap(gap, arranged);
        PerfTrace.Write(
            "api-view",
            "fit_layout_spacing",
            0,
            $"effectiveMargin={effectiveMargin:F1} effectiveGap={gap:F1} selectedMargin={selectedLayoutMargin:F1} selectedGap={selectedLayoutGap:F1}");

        var baselinePlannedViews = DrawingLayoutCandidateBuilder.ToPlannedViews(layoutWorkspace, currentViews, arranged);
        var plannedArrangedCandidate = DrawingLayoutCandidateBuilder.FromPlannedViews(
            "fit_views_to_sheet:planned-arranged", layoutWorkspace, baselinePlannedViews);
        DrawingLayoutCandidateBuilder.AttachFallbackStackOrderGroups(plannedArrangedCandidate, currentViews);
        var centeredPlannedViews = DrawingLayoutPlannedCenteringService.TryCenterViews(
            baselinePlannedViews, sheetW, sheetH, selectedLayoutMargin, layoutWorkspace.ReservedAreas);
        var plannedCenteredCandidate = DrawingLayoutCandidateBuilder.FromPlannedViews(
            "fit_views_to_sheet:planned-centered", layoutWorkspace, centeredPlannedViews);
        DrawingLayoutCandidateBuilder.AttachFallbackStackOrderGroups(plannedCenteredCandidate, currentViews);

        if (allowTeklaMutation)
        {
            TracePlannedVsActualParity(
                "post-arrange-pre-projection",
                layoutWorkspace,
                arranged,
                DrawingViewFrameGeometry.BuildActualViewRects(activeDrawing));
        }

        var projectionSw = Stopwatch.StartNew();
        if (!allowTeklaMutation)
        {
            projectionResult = new ProjectionAlignmentResult
            {
                Mode = "dry-run",
                SkippedMoves = 1
            };
            projectionResult.Diagnostics.Add("projection-skip:dry-run");
        }
        else if (ShouldSkipProjectionAlignment(optimalScale.Value, arrangedViews, out var projectionSkipMode, out var projectionSkipDiagnostic))
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
        if (allowTeklaMutation)
            activeDrawing.CommitChanges();
        commitSw.Stop();
        finalCommitMs = allowTeklaMutation ? commitSw.ElapsedMilliseconds : 0;
        selectedScale = optimalScale;

        var postProjectionViews = allowTeklaMutation
            ? EnumerateViews(activeDrawing).ToList()
            : currentViews;
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
                PlacementFallbackUsed = view.PlacementFallbackUsed,
                LayoutMargin = view.LayoutMargin,
                LayoutGap = view.LayoutGap
            })
            .ToList();
        var postProjectionCandidate = DrawingLayoutCandidateBuilder.FromPlannedViews(
            allowTeklaMutation
                ? "fit_views_to_sheet:post-projection"
                : "fit_views_to_sheet:planned-post-projection",
            layoutWorkspace,
            DrawingLayoutCandidateBuilder.ToPlannedViews(layoutWorkspace, postProjectionViews, postProjectionArranged));

        // Center the arranged group inside the usable area.
        var finalArrangedViews = postProjectionViews
            .Where(v => layoutWorkspace.GetSemanticKind(v.GetIdentifier().ID) != ViewSemanticKind.Detail)
            .ToList();
        arranged = TryCenterViewGroup(activeDrawing, finalArrangedViews, arranged,
            selectedLayoutMargin, sheetW - selectedLayoutMargin,
            selectedLayoutMargin, sheetH - selectedLayoutMargin,
            layoutWorkspace.ReservedAreas,
            allowTeklaMutation);
        var finalViews = allowTeklaMutation
            ? EnumerateViews(activeDrawing).ToList()
            : postProjectionViews;
        layoutWorkspace.SetRuntimeViews(finalViews);
        arranged = TryRepositionDetailViews(
            activeDrawing,
            finalViews,
            arranged,
            selectedLayoutMargin,
            sheetW - selectedLayoutMargin,
            selectedLayoutMargin,
            sheetH - selectedLayoutMargin,
            selectedLayoutGap,
            layoutWorkspace.ReservedAreas,
            offsetById,
            allowTeklaMutation);

        var finalActualRects = allowTeklaMutation
            ? DrawingViewFrameGeometry.BuildActualViewRects(activeDrawing)
            : actualRects;
        if (allowTeklaMutation)
        {
            TracePlannedVsActualParity(
                "post-commit-final",
                layoutWorkspace,
                arranged,
                finalActualRects);
        }

        var passiveCandidate = allowTeklaMutation
            ? DrawingLayoutCandidateBuilder.FromRuntimeLayout(
            "fit_views_to_sheet:final",
            layoutWorkspace,
            layoutWorkspace.RuntimeViews,
            arranged,
            finalActualRects)
            : DrawingLayoutCandidateBuilder.FromPlannedViews(
                "fit_views_to_sheet:planned-final",
                layoutWorkspace,
                DrawingLayoutCandidateBuilder.ToPlannedViews(layoutWorkspace, layoutWorkspace.RuntimeViews, arranged));
        var passiveSelection = new DrawingLayoutCandidateSelector().SelectBest(
            new[] { plannedArrangedCandidate, plannedCenteredCandidate, postProjectionCandidate, passiveCandidate });
        TraceLayoutPlannedVariant(
            baselinePlannedViews,
            passiveSelection.Evaluations.Count > 0 ? passiveSelection.Evaluations[0] : null,
            "planned-centered",
            centeredPlannedViews,
            passiveSelection.Evaluations.Count > 1 ? passiveSelection.Evaluations[1] : null);
        TraceLayoutCandidateSelection(passiveSelection);
        foreach (var evaluation in passiveSelection.Evaluations)
            TraceLayoutCandidateScore(evaluation);
        var applyPlan = DrawingLayoutCandidateApplyPlanBuilder.FromEvaluation(passiveSelection.Selected);
        TraceLayoutCandidateApplyPlan(applyPlan, layoutWorkspace);
        var applyDeltas = DrawingLayoutCandidateApplyDeltaBuilder.BuildDeltas(passiveCandidate, applyPlan);
        TraceLayoutCandidateApplyDeltas(applyDeltas);
        var selectedCandidateFeasible = passiveSelection.Selected?.IsFeasible == true;
        if (applyMode == DrawingLayoutApplyMode.FinalOnly && !selectedCandidateFeasible)
        {
            PerfTrace.Write(
                "api-view",
                "fit_layout_apply_blocked",
                0,
                $"candidate={(string.IsNullOrWhiteSpace(applyPlan.CandidateName) ? "none" : applyPlan.CandidateName)} reason=infeasible-candidate");
        }

        var selectedCandidateApplyMode = applyMode == DrawingLayoutApplyMode.FinalOnly
            && selectedCandidateFeasible
            && !allowTeklaMutation
            ? DrawingLayoutCandidateApplyExecutionMode.Apply
            : DrawingLayoutCandidateApplyExecutionMode.DryRun;
        var selectedCandidateApplyPolicy = new DrawingLayoutCandidateApplySafetyPolicy
        {
            AllowScaleChanges = !preserveExistingScales && !keepCurrentScales
        };
        var selectedCandidateApplySafety = selectedCandidateApplyPolicy.Resolve(
            selectedCandidateApplyMode,
            applyDeltas);
        TraceLayoutCandidateApplySafety(
            applyDeltas,
            selectedCandidateApplyPolicy,
            selectedCandidateApplySafety);
        TraceLayoutDecision(
            optimalScale.Value,
            passiveSelection,
            applyPlan,
            selectedCandidateApplySafety);
        var layoutDiagnostics = DrawingCaseLayoutDiagnosticsFactory.FromSelection(
            passiveSelection,
            applyPlan,
            applyDeltas,
            selectedCandidateApplySafety);
        var selectedCandidateApplyExecution = new DrawingLayoutCandidateTeklaApplyAdapter().Execute(
            applyPlan,
            layoutWorkspace.RuntimeViewsById,
            selectedCandidateApplySafety.EffectiveMode,
            activeDrawing);
        TraceLayoutCandidateApplyExecution(selectedCandidateApplyExecution);
        if (selectedCandidateApplySafety.EffectiveMode == DrawingLayoutCandidateApplyExecutionMode.Apply
            && selectedCandidateApplyExecution.Success
            && selectedCandidateApplyExecution.AppliedMoveCount > 0)
        {
            var selectedApplyCommitSw = Stopwatch.StartNew();
            activeDrawing.CommitChanges();
            selectedApplyCommitSw.Stop();

            var selectedCandidateViews = EnumerateViews(activeDrawing).ToList();
            layoutWorkspace.SetRuntimeViews(selectedCandidateViews);
            finalActualRects = DrawingViewFrameGeometry.BuildActualViewRects(activeDrawing);
            arranged = BuildArrangedFromApplyPlan(arranged, layoutWorkspace, applyPlan);
            var appliedActualCandidate = DrawingLayoutCandidateBuilder.FromRuntimeLayout(
                "fit_views_to_sheet:applied-actual",
                layoutWorkspace,
                selectedCandidateViews,
                arranged,
                finalActualRects);
            var appliedActualEvaluation = new DrawingLayoutScorer().Evaluate(appliedActualCandidate);
            TraceLayoutCandidateScore(appliedActualEvaluation);
            if (!appliedActualEvaluation.IsFeasible)
            {
                PerfTrace.Write(
                    "api-view",
                    "fit_layout_apply_actual_conflict",
                    0,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "candidate={0} viewOverlaps={1} viewOverlapArea={2:0.###} reservedOverlaps={3} reservedOverlapArea={4:0.###} diagnostics={5}",
                        appliedActualCandidate.Name,
                        appliedActualEvaluation.Validation.ViewOverlapCount,
                        appliedActualEvaluation.Validation.ViewOverlapArea,
                        appliedActualEvaluation.Validation.ReservedOverlapCount,
                        appliedActualEvaluation.Validation.ReservedOverlapArea,
                        appliedActualEvaluation.Validation.Diagnostics.Count));
            }

            PerfTrace.Write(
                "api-view",
                "fit_layout_apply_commit",
                selectedApplyCommitSw.ElapsedMilliseconds,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "candidate={0} appliedMoves={1} runtimeViews={2} actualRects={3}",
                    string.IsNullOrWhiteSpace(applyPlan.CandidateName) ? "none" : applyPlan.CandidateName,
                    selectedCandidateApplyExecution.AppliedMoveCount,
                    selectedCandidateViews.Count,
                    finalActualRects.Count));
        }

        // Build reserved-areas output using already-read layoutTables (no extra editor open)
        // and skip the full sheet-object scan; view rects are reported separately.
        var mergedForOutput = DrawingReservedAreaReader.Read(activeDrawing, effectiveMargin, 0.0,
            preloadedTables: layoutWorkspace.ReservedTables,
            includeSheetObjects: false);

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
        result.LayoutDiagnostics = layoutDiagnostics;

        PerfTrace.Write(
            "api-view",
            "fit_views_to_sheet",
            total.ElapsedMilliseconds,
            $"views={viewsCount} candidates={candidateAttempts} selectedScale={(selectedScale.HasValue ? selectedScale.Value.ToString(CultureInfo.InvariantCulture) : "n/a")} scalePolicy={scalePolicy} applyMode={applyMode} initMs={initMs} reservedMs={reservedMs} candidateFitMs={candidateFitMs} probeMs={probeMs} arrangeMs={arrangeMs} postAdjustMs={postAdjustMs} projectionMs={projectionMs} projectionMode={(projectionResult?.Mode ?? "none")} projectionApplied={(projectionResult?.AppliedMoves ?? 0)} projectionSkipped={(projectionResult?.SkippedMoves ?? 0)} finalCommitMs={finalCommitMs}");
        return result;
    }

    private static List<ArrangedView> BuildArrangedFromApplyPlan(
        IReadOnlyList<ArrangedView> existing,
        DrawingLayoutWorkspace workspace,
        DrawingLayoutCandidateApplyPlan applyPlan)
    {
        var existingById = existing.ToDictionary(static view => view.Id);
        var result = new List<ArrangedView>(applyPlan.Moves.Count);
        foreach (var move in applyPlan.Moves)
        {
            existingById.TryGetValue(move.ViewId, out var current);
            var runtimeView = workspace.TryGetRuntimeView(move.ViewId);
            result.Add(new ArrangedView
            {
                Id = move.ViewId,
                ViewType = current?.ViewType ?? runtimeView?.ViewType.ToString() ?? string.Empty,
                OriginX = move.TargetOriginX,
                OriginY = move.TargetOriginY,
                PreferredPlacementSide = current?.PreferredPlacementSide ?? string.Empty,
                ActualPlacementSide = current?.ActualPlacementSide ?? string.Empty,
                PlacementFallbackUsed = current?.PlacementFallbackUsed ?? false,
                LayoutMargin = current?.LayoutMargin ?? 0,
                LayoutGap = current?.LayoutGap ?? 0
            });
        }

        return result;
    }

    private static double ResolveSelectedLayoutMargin(double fallbackMargin, IReadOnlyList<ArrangedView> arranged)
        => arranged
            .Select(static view => view.LayoutMargin)
            .Where(static margin => margin > 0)
            .DefaultIfEmpty(fallbackMargin)
            .Max();

    private static double ResolveSelectedLayoutGap(double fallbackGap, IReadOnlyList<ArrangedView> arranged)
        => arranged
            .Select(static view => view.LayoutGap)
            .Where(static gap => gap > 0)
            .DefaultIfEmpty(fallbackGap)
            .Max();

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
        IReadOnlyDictionary<int, (double X, double Y)> preMovedFrameOffsets,
        bool applyChanges)
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

            var currentOrigin = detailView.Origin;
            if (currentOrigin == null)
            {
                blocked.Add(detailRect);
                continue;
            }
            var origin = new Point(currentOrigin.X, currentOrigin.Y, currentOrigin.Z);

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

            if (applyChanges)
            {
                detailView.Origin = origin;
                if (!detailView.Modify())
                {
                    blocked.Add(detailRect);
                    continue;
                }
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
                    PlacementFallbackUsed = arranged[ai].PlacementFallbackUsed,
                    LayoutMargin = arranged[ai].LayoutMargin,
                    LayoutGap = arranged[ai].LayoutGap
                };
                break;
            }
        }

        if (movedAny && applyChanges)
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
        IReadOnlyList<ReservedRect> reserved,
        bool applyChanges)
    {
        if (views.Count == 0)
            return arranged;

        var rects = GetViewRects(views);
        if (rects.Count != views.Count)
            return arranged;

        var dx = 0.0;
        if (ViewGroupCenteringGeometry.TryFindCenteringDelta(rects, usableMinX, usableMaxX, reserved, horizontal: true, out var foundDx))
        {
            dx = foundDx;
            rects = ViewGroupCenteringGeometry.ShiftRects(rects, dx, 0);
        }

        var dy = 0.0;
        if (ViewGroupCenteringGeometry.TryFindCenteringDelta(rects, usableMinY, usableMaxY, reserved, horizontal: false, out var foundDy))
            dy = foundDy;

        if (System.Math.Abs(dx) < 1.0 && System.Math.Abs(dy) < 1.0)
            return arranged;

        foreach (var v in views)
        {
            var currentOrigin = v.Origin;
            if (currentOrigin == null)
                continue;

            var o = new Point(currentOrigin.X, currentOrigin.Y, currentOrigin.Z);
            o.X += dx;
            o.Y += dy;
            if (applyChanges)
            {
                v.Origin = o;
                v.Modify();
            }
        }

        if (applyChanges)
            activeDrawing.CommitChanges();

        PerfTrace.Write("api-view", applyChanges ? "center_group" : "center_group_plan", 0,
            $"applied={(applyChanges ? 1 : 0)} dx={dx:F1} dy={dy:F1} usableX={usableMinX:F1}-{usableMaxX:F1} usableY={usableMinY:F1}-{usableMaxY:F1}");

        return arranged.Select(a => new ArrangedView
        {
            Id       = a.Id,
            ViewType = a.ViewType,
            OriginX  = a.OriginX + dx,
            OriginY  = a.OriginY + dy,
            PreferredPlacementSide = a.PreferredPlacementSide,
            ActualPlacementSide = a.ActualPlacementSide,
            PlacementFallbackUsed = a.PlacementFallbackUsed,
            LayoutMargin = a.LayoutMargin,
            LayoutGap = a.LayoutGap
        }).ToList();
    }

    private static double CenterX(ReservedRect rect) => (rect.MinX + rect.MaxX) / 2.0;

    private static double CenterY(ReservedRect rect) => (rect.MinY + rect.MaxY) / 2.0;
}
