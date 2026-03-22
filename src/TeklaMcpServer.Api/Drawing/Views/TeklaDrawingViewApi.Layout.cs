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

namespace TeklaMcpServer.Api.Drawing;

public sealed partial class TeklaDrawingViewApi
{
    internal const double ProjectionAlignmentScaleCutoff = 100.0;

    internal static bool ShouldSkipProjectionAlignment(double optimalScale)
        => optimalScale >= ProjectionAlignmentScaleCutoff;

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

    private static List<(double w, double h)> BuildCandidateFrames(
        IReadOnlyList<View> views,
        IReadOnlyDictionary<int, ViewSemanticKind> semanticKindById,
        IReadOnlyDictionary<int, double> originalScales,
        IReadOnlyDictionary<int, (double Width, double Height)> sourceFrameSizes,
        double candidateScale,
        bool uniformAllNonDetail)
    {
        var frames = new List<(double w, double h)>(views.Count);
        foreach (var view in views)
        {
            var viewId = view.GetIdentifier().ID;
            var sourceScale = originalScales.TryGetValue(viewId, out var originalScale) && originalScale > 0
                ? originalScale
                : (view.Attributes.Scale > 0 ? view.Attributes.Scale : 1.0);
            var targetScale = ResolveTargetScale(view, semanticKindById[viewId], candidateScale, uniformAllNonDetail, originalScales);

            if (sourceFrameSizes.TryGetValue(viewId, out var sourceFrame))
            {
                var factor = sourceScale / targetScale;
                frames.Add((sourceFrame.Width * factor, sourceFrame.Height * factor));
            }
            else
            {
                var factor = sourceScale / targetScale;
                frames.Add((view.Width * factor, view.Height * factor));
            }
        }

        return frames;
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
        var debugPreview = applyMode == DrawingLayoutApplyMode.DebugPreview;

        var activeDrawing = new DrawingHandler().GetActiveDrawing();
        if (activeDrawing == null)
            throw new DrawingNotOpenException();

        // null -> auto-read from drawing layout; read tables in the same editor-open pass
        var (autoMargin, layoutTables) = DrawingReservedAreaReader.ReadLayoutInfo();
        var effectiveMargin = margin ?? autoMargin ?? 10.0;

        var init = Stopwatch.StartNew();

        var views = EnumerateViews(activeDrawing).ToList();
        viewsCount = views.Count;
        if (views.Count == 0)
            throw new System.InvalidOperationException("No views found in active drawing.");
        var semanticKindById = views.ToDictionary(
            v => v.GetIdentifier().ID,
            v => ViewSemanticClassifier.Classify(v));
        var scaleDriverViews = uniformAllNonDetail
            ? views
                .Where(v => semanticKindById[v.GetIdentifier().ID] != ViewSemanticKind.Detail)
                .ToList()
            : views
                .Where(v => semanticKindById[v.GetIdentifier().ID] == ViewSemanticKind.BaseProjected)
                .ToList();
        if (scaleDriverViews.Count == 0)
        {
            scaleDriverViews = views
                .Where(v => semanticKindById[v.GetIdentifier().ID] != ViewSemanticKind.Detail)
                .ToList();
        }
        if (scaleDriverViews.Count == 0)
            scaleDriverViews = views;

        double sheetW = 0;
        double sheetH = 0;
        try
        {
            var ss = activeDrawing.Layout.SheetSize;
            sheetW = ss.Width;
            sheetH = ss.Height;
        }
        catch
        {
        }

        if (sheetW <= 0 || sheetH <= 0)
            throw new System.InvalidOperationException("Unable to read drawing sheet size.");

        var viewIds = views.Select(v => v.GetIdentifier().ID).ToHashSet();
        var reservedRead = Stopwatch.StartNew();
        IReadOnlyList<ReservedRect> reservedAreas = DrawingReservedAreaReader.Read(
            activeDrawing,
            effectiveMargin,
            titleBlockHeight,
            viewIds,
            layoutTables);
        reservedRead.Stop();
        reservedMs = reservedRead.ElapsedMilliseconds;

        var availW = sheetW - (2 * effectiveMargin);
        var availH = sheetH - (2 * effectiveMargin);
        if (availW <= 0 || availH <= 0)
            throw new System.InvalidOperationException("No drawable area left after applying margin.");

        double currentScale = scaleDriverViews.Select(v => v.Attributes.Scale).FirstOrDefault(s => s > 0);
        if (currentScale <= 0)
            currentScale = 1.0;

        const double borderEst = 20.0;
        var maxModelW = scaleDriverViews.Max(v => v.Width * currentScale);
        var maxModelH = scaleDriverViews.Max(v => v.Height * currentScale);
        var minDenom = System.Math.Max(
            maxModelW / System.Math.Max(availW - borderEst, 1),
            maxModelH / System.Math.Max(availH - borderEst, 1));

        var standardScales = new[] { 1.0, 2, 5, 10, 15, 20, 25, 30, 40, 50, 60, 70, 75, 80, 100, 125, 150, 175, 200, 250, 300 };
        var candidates = System.Array.FindAll(standardScales, s => s >= minDenom);
        if (candidates.Length == 0)
            candidates = new[] { standardScales[standardScales.Length - 1] };
        init.Stop();
        initMs = init.ElapsedMilliseconds;

        var originalScales = views.ToDictionary(v => v.GetIdentifier().ID, v => v.Attributes.Scale);
        // Build actual view rects once via sheet.GetAllObjects() — these always reflect the
        // physical frame position and are never stale, unlike GetAxisAlignedBoundingBox() on
        // views from GetViews() which may be stale after Modify/CommitChanges.
        var actualRects = DrawingViewSheetGeometry.BuildActualViewRects(activeDrawing);
        var originalFrameSizes = TryGetFrameSizesFromBoundingBoxes(views, actualRects);

        double? optimalScale = null;
        var currentViews = views;

        if (preserveExistingScales)
        {
            // Validate that views fit at their current scales before committing to arrange.
            var keepFrameSizes = TryGetFrameSizesFromBoundingBoxes(currentViews, actualRects);
            var keepFrames = currentViews
                .Select(v =>
                {
                    if (keepFrameSizes.TryGetValue(v.GetIdentifier().ID, out var size))
                        return (w: size.Width, h: size.Height);
                    return (w: v.Width, h: v.Height);
                })
                .ToList();
            var keepCtx = new DrawingArrangeContext(activeDrawing, currentViews, sheetW, sheetH, effectiveMargin, gap, reservedAreas, keepFrameSizes);
            var keepFitSw = Stopwatch.StartNew();
            var oversizeConflicts = BuildOversizeConflicts(currentViews, keepFrames, availW, availH);
            if (oversizeConflicts.Count > 0)
                throw new DrawingFitFailedException("One or more views are larger than the usable sheet area at current scales.", oversizeConflicts);
            var fits = _arrangementSelector.EstimateFit(keepCtx, keepFrames);
            keepFitSw.Stop();
            candidateFitMs = keepFitSw.ElapsedMilliseconds;
            candidateAttempts = 1;
            if (!fits)
            {
                var conflicts = _arrangementSelector.DiagnoseFitConflicts(keepCtx, keepFrames);
                throw new DrawingFitFailedException("Could not fit views on sheet at current scales. Use a non-preserving scale policy to allow rescaling.", conflicts);
            }

            // ShouldSkipProjectionAlignment skips when scale >= cutoff (100), meaning all views
            // are small enough that alignment corrections are negligible. Use Max() so projection
            // is skipped only when every view is at or above the cutoff — the one correct
            // condition under which alignment adds no value for any view on the sheet.
            optimalScale = currentViews.Select(v => v.Attributes.Scale).Where(s => s > 0).DefaultIfEmpty(1.0).Max();
        }
        else if (keepCurrentScales)
        {
            var keepFrameSizes = TryGetFrameSizesFromBoundingBoxes(currentViews, actualRects);
            var keepFrames = currentViews
                .Select(v =>
                {
                    if (keepFrameSizes.TryGetValue(v.GetIdentifier().ID, out var size))
                        return (w: size.Width, h: size.Height);
                    return (w: v.Width, h: v.Height);
                })
                .ToList();
            var keepCtx = new DrawingArrangeContext(activeDrawing, currentViews, sheetW, sheetH, effectiveMargin, gap, reservedAreas, keepFrameSizes);
            var keepFitSw = Stopwatch.StartNew();
            var oversizeConflicts = BuildOversizeConflicts(currentViews, keepFrames, availW, availH);
            if (oversizeConflicts.Count > 0)
                throw new DrawingFitFailedException("One or more views are larger than the usable sheet area at current scales.", oversizeConflicts);
            var fits = _arrangementSelector.EstimateFit(keepCtx, keepFrames);
            keepFitSw.Stop();
            candidateFitMs = keepFitSw.ElapsedMilliseconds;
            candidateAttempts = 1;
            if (!fits)
            {
                var conflicts = _arrangementSelector.DiagnoseFitConflicts(keepCtx, keepFrames);
                throw new DrawingFitFailedException("Could not fit views on sheet at current scales.", conflicts);
            }

            optimalScale = currentViews.Select(v => v.Attributes.Scale).Where(s => s > 0).DefaultIfEmpty(1.0).Max();
        }
        else
        {
            List<DrawingFitConflict>? lastOversizeConflicts = null;
            foreach (var s in candidates)
            {
                candidateAttempts++;
                var candidateSw = Stopwatch.StartNew();
                List<View> candidateViews;
                Dictionary<int, (double Width, double Height)> effectiveFrameSizes;
                List<(double w, double h)> actualFrames;
                DrawingArrangeContext ctx;

                if (debugPreview)
                {
                    foreach (var v in currentViews)
                    {
                        var targetScale = ResolveTargetScale(v, semanticKindById[v.GetIdentifier().ID], s, uniformAllNonDetail, originalScales);
                        if (System.Math.Abs(v.Attributes.Scale - targetScale) < 0.01)
                            continue;

                        v.Attributes.Scale = targetScale;
                        v.Modify();
                    }

                    activeDrawing.CommitChanges();
                    candidateViews = EnumerateViews(activeDrawing).ToList();
                    effectiveFrameSizes = TryGetFrameSizesFromBoundingBoxes(candidateViews, null);
                    actualFrames = candidateViews
                        .Select(v =>
                        {
                            if (effectiveFrameSizes.TryGetValue(v.GetIdentifier().ID, out var size))
                                return (w: size.Width, h: size.Height);
                            return (w: v.Width, h: v.Height);
                        })
                        .ToList();
                    ctx = new DrawingArrangeContext(activeDrawing, candidateViews, sheetW, sheetH, effectiveMargin, gap, reservedAreas, effectiveFrameSizes);
                }
                else
                {
                    candidateViews = currentViews;
                    effectiveFrameSizes = originalFrameSizes;
                    actualFrames = BuildCandidateFrames(candidateViews, semanticKindById, originalScales, originalFrameSizes, s, uniformAllNonDetail);
                    ctx = new DrawingArrangeContext(activeDrawing, candidateViews, sheetW, sheetH, effectiveMargin, gap, reservedAreas, effectiveFrameSizes);
                }

                var oversizeConflicts = BuildOversizeConflicts(candidateViews, actualFrames, availW, availH);
                if (oversizeConflicts.Count > 0)
                {
                    lastOversizeConflicts = oversizeConflicts;
                    candidateSw.Stop();
                    candidateFitMs += candidateSw.ElapsedMilliseconds;
                    continue;
                }

                if (_arrangementSelector.EstimateFit(ctx, actualFrames))
                {
                    optimalScale = s;
                    currentViews = candidateViews;
                    candidateSw.Stop();
                    candidateFitMs += candidateSw.ElapsedMilliseconds;
                    break;
                }
                currentViews = candidateViews;
                candidateSw.Stop();
                candidateFitMs += candidateSw.ElapsedMilliseconds;
            }

            if (!optimalScale.HasValue)
            {
                foreach (var v in EnumerateViews(activeDrawing))
                {
                    if (originalScales.TryGetValue(v.GetIdentifier().ID, out var orig))
                    {
                        v.Attributes.Scale = orig;
                        v.Modify();
                    }
                }

                activeDrawing.CommitChanges();
                if (lastOversizeConflicts is { Count: > 0 })
                    throw new DrawingFitFailedException("One or more views are larger than the usable sheet area for every available standard scale.", lastOversizeConflicts);

                throw new System.InvalidOperationException("Could not fit views on sheet with available standard scales.");
            }

            if (!debugPreview)
            {
                foreach (var v in currentViews)
                {
                    var targetScale = ResolveTargetScale(v, semanticKindById[v.GetIdentifier().ID], optimalScale.Value, uniformAllNonDetail, originalScales);
                    if (System.Math.Abs(v.Attributes.Scale - targetScale) < 0.01)
                        continue;

                    v.Attributes.Scale = targetScale;
                    v.Modify();
                }

                activeDrawing.CommitChanges();
                currentViews = EnumerateViews(activeDrawing).ToList();
            }
        }

        var offsetById = preserveExistingScales
            ? new System.Collections.Generic.Dictionary<int, (double X, double Y)>()
            : TryGetFrameOffsetsFromBoundingBoxes(currentViews, actualRects);
        // Do not probe temporary scales in the live drawing: interrupted runs can leave the
        // sheet in an arbitrary intermediate state. Missing offsets now simply skip correction.

        var arrangedViews = currentViews
            .Where(v => semanticKindById[v.GetIdentifier().ID] != ViewSemanticKind.Detail)
            .ToList();

        var gridApi = new TeklaDrawingGridApi();
        var preloadedAxes = arrangedViews
            .Select(v => (Id: v.GetIdentifier().ID, Result: gridApi.GetGridAxes(v.GetIdentifier().ID)))
            .Where(x => x.Result.Success)
            .ToDictionary(x => x.Id, x => (IReadOnlyList<GridAxisInfo>)x.Result.Axes);

        var arrangeSw = Stopwatch.StartNew();
        var arranged = arrangedViews.Count == 0
            ? new List<ArrangedView>()
            : _arrangementSelector.Arrange(
                new DrawingArrangeContext(activeDrawing, arrangedViews, sheetW, sheetH, effectiveMargin, gap, reservedAreas,
                    TryGetFrameSizesFromBoundingBoxes(arrangedViews, null)));
        arrangeSw.Stop();
        arrangeMs = arrangeSw.ElapsedMilliseconds;

        if (!preserveExistingScales)
        {
            foreach (var detailView in currentViews.Where(v => semanticKindById[v.GetIdentifier().ID] == ViewSemanticKind.Detail))
            {
                if (!originalScales.TryGetValue(detailView.GetIdentifier().ID, out var detailScale))
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
            var viewById = currentViews.ToDictionary(v => v.GetIdentifier().ID);

            for (int i = 0; i < arranged.Count; i++)
            {
                if (!viewById.TryGetValue(arranged[i].Id, out var v))
                    continue;
                if (!offsetById.TryGetValue(arranged[i].Id, out var off))
                    continue;

                var correctionScale = v.Attributes.Scale > 0 ? v.Attributes.Scale : optimalScale.Value;
                var corrX = off.X / correctionScale;
                var corrY = off.Y / correctionScale;
                var semanticKind = semanticKindById.TryGetValue(arranged[i].Id, out var kind)
                    ? kind
                    : ViewSemanticKind.Other;
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

        var projectionSw = Stopwatch.StartNew();
        if (ShouldSkipProjectionAlignment(optimalScale.Value))
        {
            projectionResult = new ProjectionAlignmentResult
            {
                Mode = "disabled-scale",
                SkippedMoves = 1
            };
            projectionResult.Diagnostics.Add($"projection-skip:scale-too-small:1:{optimalScale.Value.ToString(CultureInfo.InvariantCulture)}");
        }
        else
        {
            var projectionAlignmentService = new DrawingProjectionAlignmentService(new Model());
            projectionResult = projectionAlignmentService.Apply(
                activeDrawing,
                arrangedViews,
                offsetById,
                sheetW,
                sheetH,
                effectiveMargin,
                reservedAreas,
                arranged,
                preloadedAxes);
        }
        projectionSw.Stop();
        projectionMs = projectionSw.ElapsedMilliseconds;

        var commitSw = Stopwatch.StartNew();
        activeDrawing.CommitChanges();
        commitSw.Stop();
        finalCommitMs = commitSw.ElapsedMilliseconds;
        selectedScale = optimalScale;

        // Center the arranged group inside the usable area.
        var finalViews = EnumerateViews(activeDrawing).ToList();
        var finalArrangedViews = finalViews
            .Where(v => semanticKindById.TryGetValue(v.GetIdentifier().ID, out var kind) && kind != ViewSemanticKind.Detail)
            .ToList();
        arranged = TryCenterViewGroup(activeDrawing, finalArrangedViews, arranged,
            effectiveMargin, sheetW - effectiveMargin,
            effectiveMargin, sheetH - effectiveMargin,
            reservedAreas);
        finalViews = EnumerateViews(activeDrawing).ToList();
        arranged = TryRepositionDetailViews(
            activeDrawing,
            finalViews,
            arranged,
            effectiveMargin,
            sheetW - effectiveMargin,
            effectiveMargin,
            sheetH - effectiveMargin,
            gap,
            reservedAreas,
            offsetById);

        // Build reserved-areas output using already-read layoutTables (no extra editor open).
        // Read() without excludeViewIds to include view bounding boxes in the merged output.
        var mergedForOutput = DrawingReservedAreaReader.Read(activeDrawing, effectiveMargin, 0.0,
            preloadedTables: layoutTables);

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
                SheetMargin = autoMargin,
                Tables      = layoutTables,
                MergedAreas = mergedForOutput
            }
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
            if (!DrawingViewSheetGeometry.TryGetBoundingRect(v, out var rect))
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
        var detailViews = views
            .Where(v => ViewSemanticClassifier.Classify(v) == ViewSemanticKind.Detail)
            .ToList();
        if (detailViews.Count == 0)
            return arranged;

        // Build detailId → (ownerView, detailMark) using GetRelatedObjects() — more reliable than name matching.
        var detailViewIds = new System.Collections.Generic.HashSet<int>(detailViews.Select(v => v.GetIdentifier().ID));
        var detailRelationByDetailId = new System.Collections.Generic.Dictionary<int, (View OwnerView, Tekla.Structures.Drawing.DetailMark Mark)>();
        foreach (var ownerView in views)
        {
            var markEnum = ownerView.GetAllObjects(typeof(Tekla.Structures.Drawing.DetailMark));
            while (markEnum.MoveNext())
            {
                if (markEnum.Current is not Tekla.Structures.Drawing.DetailMark mark)
                    continue;
                var related = mark.GetRelatedObjects();
                while (related.MoveNext())
                {
                    if (related.Current is not View relatedView)
                        continue;
                    var relId = relatedView.GetIdentifier().ID;
                    if (detailViewIds.Contains(relId) && !detailRelationByDetailId.ContainsKey(relId))
                        detailRelationByDetailId[relId] = (ownerView, mark);
                    break;
                }
            }
        }
        if (detailRelationByDetailId.Count == 0)
            return arranged;

        var viewById = views.ToDictionary(v => v.GetIdentifier().ID);
        var blocked = new List<ReservedRect>(reserved);
        foreach (var view in views.Where(v => ViewSemanticClassifier.Classify(v) != ViewSemanticKind.Detail))
        {
            if (DrawingViewSheetGeometry.TryGetBoundingRect(view, out var rect))
                blocked.Add(rect);
        }

        var movedAny = false;
        for (var i = 0; i < detailViews.Count; i++)
        {
            var detailView = detailViews[i];
            var detailId = detailView.GetIdentifier().ID;
            if (!detailRelationByDetailId.TryGetValue(detailId, out var relation))
            {
                if (DrawingViewSheetGeometry.TryGetBoundingRect(detailView, out var currentRect))
                    blocked.Add(currentRect);
                continue;
            }

            var ownerView = relation.OwnerView;
            if (!viewById.ContainsKey(ownerView.GetIdentifier().ID))
            {
                if (DrawingViewSheetGeometry.TryGetBoundingRect(detailView, out var currentRect))
                    blocked.Add(currentRect);
                continue;
            }

            if (!DrawingViewSheetGeometry.TryGetBoundingRect(ownerView, out var ownerRect)
                || !DrawingViewSheetGeometry.TryGetBoundingRect(detailView, out var detailRect))
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
            if (TryResolveDetailAnchorSheet(ownerView, relation.Mark, out var resolvedAnchorX, out var resolvedAnchorY))
            {
                anchorX = resolvedAnchorX;
                anchorY = resolvedAnchorY;
            }

            if (!BaseProjectedDrawingArrangeStrategy.TryFindDetailRect(
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
                    anchorY,
                    out var candidateRect))
            {
                blocked.Add(detailRect);
                continue;
            }

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
            else if (DrawingViewSheetGeometry.TryGetCenterOffsetFromOrigin(detailView, out var offsetX, out var offsetY))
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
