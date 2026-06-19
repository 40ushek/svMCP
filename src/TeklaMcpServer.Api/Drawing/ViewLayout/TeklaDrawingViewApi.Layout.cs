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


    /// <param name="margin">Margin from sheet edges in mm. Pass <c>null</c> to auto-read from drawing layout. Pass 0 for a true zero margin.</param>
    /// <param name="scalePolicy">Controls whether scales are unified, partially unified, or preserved as-is.</param>
    public FitViewsResult FitViewsToSheet(
        double? margin,
        double gap,
        double titleBlockHeight,
        DrawingScalePolicy scalePolicy = DrawingScalePolicy.UniformAllNonDetail,
        DrawingLayoutApplyMode applyMode = DrawingLayoutApplyMode.DebugPreview,
        SecondaryScalePolicy secondaryScalePolicy = SecondaryScalePolicy.SameAsMain)
    {
        var total = Stopwatch.StartNew();
        using var layoutTrace = PerfTrace.BeginViewLayoutRun(
            "fit_views_to_sheet",
            $"margin={(margin.HasValue ? margin.Value.ToString("0.###", CultureInfo.InvariantCulture) : "auto")} gap={gap:0.###} titleBlockHeight={titleBlockHeight:0.###} scalePolicy={scalePolicy} applyMode={applyMode} secondaryScalePolicy={secondaryScalePolicy}");
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
        PerfTrace.Write("api-view", "layout_stage", 0, "stage=input-validated result=ok");

        var init = Stopwatch.StartNew();

        var views = EnumerateViews(activeDrawing).ToList();
        viewsCount = views.Count;
        if (views.Count == 0)
            throw new System.InvalidOperationException("No views found in active drawing.");
        PerfTrace.Write("api-view", "layout_stage", 0, $"stage=views-loaded result=ok views={viewsCount}");
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
        layoutWorkspace.SetParentViewRelations();
        var effectiveMargin = layoutWorkspace.Margin;
        var sheetW = layoutWorkspace.SheetWidth;
        var sheetH = layoutWorkspace.SheetHeight;
        PerfTrace.Write(
            "api-view",
            "layout_stage",
            0,
            $"stage=context-built result=ok sheet={sheetW:0.###}x{sheetH:0.###} margin={effectiveMargin:0.###} reserved={layoutWorkspace.ReservedAreas.Count}");

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
                .Where(v =>
                {
                    var semanticKind = layoutWorkspace.GetSemanticKind(v.GetIdentifier().ID);
                    return semanticKind is not ViewSemanticKind.Detail and not ViewSemanticKind.Model3D;
                })
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
            PerfTrace.Write("api-view", "layout_branch", 0, "branch=scale-selection action=preserve-existing");
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
            PerfTrace.Write("api-view", "layout_branch", 0, "branch=scale-selection action=keep-current");
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
            PerfTrace.Write(
                "api-view",
                "layout_branch",
                0,
                $"branch=scale-selection action=probe-candidates candidates={candidates.Count}");
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
                    allowTeklaMutation,
                    availW,
                    availH,
                    secondaryScalePolicy);
                probeMs += probe.ElapsedMilliseconds;
                var candidateViews = probe.Views;
                var actualFrames = probe.Frames;

                if (probe.PreRejectedByEstimate)
                {
                    var preRejectOversizeConflicts = BuildOversizeConflicts(candidateViews, actualFrames, availW, availH);
                    var preRejectDecision = new EstimateFitFailureDecision(
                        stage: "candidate-pre-reject",
                        candidateScale: s,
                        fits: false,
                        oversizeConflicts: preRejectOversizeConflicts,
                        diagnosedConflicts: null);
                    rejectedScaleDecisions.Add(preRejectDecision);
                    TraceEstimateFailureDecision(preRejectDecision);
                    lastOversizeConflicts = preRejectOversizeConflicts;
                    candidateSw.Stop();
                    candidateFitMs += candidateSw.ElapsedMilliseconds;
                    continue;
                }

                var ctx = new DrawingArrangeContext(activeDrawing, layoutWorkspace, candidateViews, gap, probe.FrameSizes);

                if (probe.TeklaMutationApplied && probe.EstimatedFrameSizes != null)
                    TraceScaleCandidateApply(s, candidateViews, probe.FrameSizes, probe.EstimatedFrameSizes);

                var oversizeConflicts = BuildOversizeConflicts(candidateViews, actualFrames, availW, availH);
                if (oversizeConflicts.Count > 0)
                {
                    TraceScaleCandidateReject(s,
                        $"oversize({string.Join(",", oversizeConflicts.Select(c => $"{c.ViewId}:{c.ViewType}"))})");
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
                if (!fits && PerfTrace.IsViewLayoutDetailedTraceActive)
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
                    TraceScaleCandidateReject(s,
                        $"no-fit({string.Join(",", conflicts.Select(c => $"{c.ViewId}:{c.ViewType}"))})");
                }
                else if (!fits)
                {
                    TraceScaleCandidateReject(s, "no-fit");
                }

                if (fits)
                {
                    TraceScaleCandidateAccept(s);
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

                if (PerfTrace.IsViewLayoutDetailedTraceActive && lastDiagnosedDecision.HasValue)
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
            "layout_stage",
            0,
            $"stage=scale-selected result=ok scale=1:{optimalScale.Value:0.###} attempts={candidateAttempts}");

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
            preserveExistingScales || keepCurrentScales,
            secondaryScalePolicy));

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

        var sharedCtx = new SharedLayoutContext
        {
            Drawing              = activeDrawing,
            Workspace            = layoutWorkspace,
            CurrentViews         = currentViews,
            ArrangedViews        = arrangedViews,
            ActualRects          = actualRects,
            OffsetById           = offsetById,
            OptimalScale         = optimalScale.Value,
            EffectiveMargin      = effectiveMargin,
            Gap                  = gap,
            PreserveExistingScales = preserveExistingScales,
            KeepCurrentScales    = keepCurrentScales,
            AllowTeklaMutation   = allowTeklaMutation,
            InitMs               = initMs,
            ReservedMs           = reservedMs,
            CandidateFitMs       = candidateFitMs,
            ProbeMs              = probeMs,
            CandidateAttempts    = candidateAttempts,
            ViewsCount           = viewsCount
        };

        var variantResult = RunDefaultLayoutVariant(sharedCtx);
        var arranged = variantResult.Arranged;
        arrangeMs    = variantResult.ArrangeMs;
        postAdjustMs = variantResult.PostAdjustMs;
        projectionMs = variantResult.ProjectionMs;
        projectionResult = variantResult.ProjectionResult;
        finalCommitMs = variantResult.FinalCommitMs;
        selectedScale = optimalScale;

        var finalActualRects = allowTeklaMutation
            ? DrawingViewFrameGeometry.BuildActualViewRects(activeDrawing)
            : actualRects;
        var runtimeBaselineCandidate = DrawingLayoutCandidateBuilder.FromRuntimeLayout(
            "fit_views_to_sheet:runtime-before-final-apply",
            layoutWorkspace,
            layoutWorkspace.RuntimeViews,
            arranged,
            finalActualRects);
        var passiveSelection = new DrawingLayoutCandidateSelector().SelectBest(variantResult.Candidates);
        PerfTrace.Write(
            "api-view",
            "layout_stage",
            0,
            $"stage=candidate-selection result=done candidates=2 selected={passiveSelection.Selected?.Candidate.Name ?? "none"} feasible={(passiveSelection.Selected?.IsFeasible == true ? 1 : 0)}");
        TraceLayoutCandidateSelection(passiveSelection);
        foreach (var evaluation in passiveSelection.Evaluations)
            TraceLayoutCandidateScore(evaluation);
        var applyPlan = DrawingLayoutCandidateApplyPlanBuilder.FromEvaluation(passiveSelection.Selected);
        TraceLayoutCandidateApplyPlan(applyPlan, layoutWorkspace);
        var applyDeltas = DrawingLayoutCandidateApplyDeltaBuilder.BuildDeltas(runtimeBaselineCandidate, applyPlan);
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
            && allowTeklaMutation
            ? DrawingLayoutCandidateApplyExecutionMode.Apply
            : DrawingLayoutCandidateApplyExecutionMode.DryRun;
        PerfTrace.Write(
            "api-view",
            "layout_branch",
            0,
            $"branch=apply-mode action={selectedCandidateApplyMode} requested={applyMode} feasible={(selectedCandidateFeasible ? 1 : 0)} mutationAllowed={(allowTeklaMutation ? 1 : 0)}");
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
        PerfTrace.Write(
            "api-view",
            "layout_stage",
            0,
            $"stage=apply result={DrawingLayoutCandidateApplyExecutionReasonFormatter.ToTraceString(selectedCandidateApplyExecution.Reason)} mode={selectedCandidateApplySafety.EffectiveMode} requestedMoves={selectedCandidateApplyExecution.RequestedMoveCount} appliedMoves={selectedCandidateApplyExecution.AppliedMoveCount}");
        TraceLayoutCandidateApplyExecution(selectedCandidateApplyExecution);
        if (selectedCandidateApplySafety.EffectiveMode == DrawingLayoutCandidateApplyExecutionMode.Apply
            && selectedCandidateApplyExecution.Success
            && selectedCandidateApplyExecution.AppliedMoveCount > 0)
        {
            PerfTrace.Write("api-view", "layout_branch", 0, "branch=apply-commit action=run");
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
                        "candidate={0} outOfBounds={1} viewOverlaps={2} viewOverlapArea={3:0.###} reservedOverlaps={4} reservedOverlapArea={5:0.###} diagnostics={6}",
                        appliedActualCandidate.Name,
                        appliedActualEvaluation.Validation.OutOfBoundsCount,
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
        else
        {
            PerfTrace.Write(
                "api-view",
                "layout_branch",
                0,
                $"branch=apply-commit action=skip mode={selectedCandidateApplySafety.EffectiveMode} success={(selectedCandidateApplyExecution.Success ? 1 : 0)} appliedMoves={selectedCandidateApplyExecution.AppliedMoveCount}");
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
        PerfTrace.CompleteViewLayoutRun(
            $"views={viewsCount} arranged={arranged.Count} appliedMoves={selectedCandidateApplyExecution.AppliedMoveCount} feasible={(selectedCandidateFeasible ? 1 : 0)} totalMs={total.ElapsedMilliseconds}");
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

    private static List<ReservedRect> GetViewRects(
        DrawingLayoutWorkspace workspace,
        IReadOnlyDictionary<int, ArrangedView> arrangedById,
        List<View> views)
    {
        var rects = new List<ReservedRect>(views.Count);
        foreach (var v in views)
        {
            var id = v.GetIdentifier().ID;
            if (!arrangedById.TryGetValue(id, out var arranged))
            {
                DrawingProjectionAlignmentService.Log(
                    $"CENTER_GROUP_SKIP reason=missing-arranged view={id}");
                return new List<ReservedRect>();
            }

            var size = workspace.GetSelectedFrameSize(id, v.Width, v.Height);
            if (size.Width <= 0 || size.Height <= 0)
            {
                DrawingProjectionAlignmentService.Log(
                    $"CENTER_GROUP_SKIP reason=no-size-or-frame view={id} width={size.Width:F1} height={size.Height:F1}");
                return new List<ReservedRect>();
            }

            var rect = ViewPlacementGeometryService.CreateRectFromOrigin(
                workspace,
                v,
                arranged.OriginX,
                arranged.OriginY,
                size.Width,
                size.Height);
            rects.Add(rect);
        }

        return rects;
    }

}
