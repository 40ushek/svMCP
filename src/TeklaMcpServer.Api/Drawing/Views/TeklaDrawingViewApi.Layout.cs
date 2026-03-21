using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Model;
using TeklaMcpServer.Api.Diagnostics;

namespace TeklaMcpServer.Api.Drawing;

public sealed partial class TeklaDrawingViewApi
{
    internal const double ProjectionAlignmentScaleCutoff = 100.0;

    internal static bool ShouldSkipProjectionAlignment(double optimalScale)
        => optimalScale >= ProjectionAlignmentScaleCutoff;

    /// <param name="margin">Margin from sheet edges in mm. Pass <c>null</c> to auto-read from drawing layout. Pass 0 for a true zero margin.</param>
    /// <param name="scalePolicy">Controls whether scales are unified, partially unified, or preserved as-is.</param>
    public FitViewsResult FitViewsToSheet(double? margin, double gap, double titleBlockHeight, DrawingScalePolicy scalePolicy = DrawingScalePolicy.UniformAllNonDetail)
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
            v => ViewSemanticClassifier.Classify(v.ViewType));
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

        double? optimalScale = null;
        var currentViews = views;

        if (preserveExistingScales)
        {
            // Validate that views fit at their current scales before committing to arrange.
            var keepFrameSizes = TryGetFrameSizesFromBoundingBoxes(currentViews);
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
            var fits = _arrangementSelector.EstimateFit(keepCtx, keepFrames);
            keepFitSw.Stop();
            candidateFitMs = keepFitSw.ElapsedMilliseconds;
            candidateAttempts = 1;
            if (!fits)
                throw new System.InvalidOperationException("Could not fit views on sheet at current scales. Use a non-preserving scale policy to allow rescaling.");

            // ShouldSkipProjectionAlignment skips when scale >= cutoff (100), meaning all views
            // are small enough that alignment corrections are negligible. Use Max() so projection
            // is skipped only when every view is at or above the cutoff — the one correct
            // condition under which alignment adds no value for any view on the sheet.
            optimalScale = currentViews.Select(v => v.Attributes.Scale).Where(s => s > 0).DefaultIfEmpty(1.0).Max();
        }
        else
        {
            foreach (var s in candidates)
            {
                candidateAttempts++;
                var candidateSw = Stopwatch.StartNew();
                foreach (var v in currentViews)
                {
                    var semanticKind = semanticKindById[v.GetIdentifier().ID];
                    if (semanticKind == ViewSemanticKind.Detail)
                        continue;

                    if (!uniformAllNonDetail && semanticKind != ViewSemanticKind.BaseProjected)
                        continue;

                    v.Attributes.Scale = s;
                    v.Modify();
                }

                activeDrawing.CommitChanges();

                var reread = EnumerateViews(activeDrawing).ToList();
                var effectiveFrameSizes = TryGetFrameSizesFromBoundingBoxes(reread);
                var actualFrames = reread
                    .Select(v =>
                    {
                        if (effectiveFrameSizes.TryGetValue(v.GetIdentifier().ID, out var size))
                            return (w: size.Width, h: size.Height);
                        return (w: v.Width, h: v.Height);
                    })
                    .ToList();
                var ctx = new DrawingArrangeContext(activeDrawing, reread, sheetW, sheetH, effectiveMargin, gap, reservedAreas, effectiveFrameSizes);

                if (_arrangementSelector.EstimateFit(ctx, actualFrames))
                {
                    optimalScale = s;
                    currentViews = reread;
                    candidateSw.Stop();
                    candidateFitMs += candidateSw.ElapsedMilliseconds;
                    break;
                }

                currentViews = reread;
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
                throw new System.InvalidOperationException("Could not fit views on sheet with available standard scales.");
            }
        }

        var offsetById = preserveExistingScales
            ? new System.Collections.Generic.Dictionary<int, (double X, double Y)>()
            : TryGetFrameOffsetsFromBoundingBoxes(currentViews, optimalScale.Value);
        if (!preserveExistingScales && offsetById.Count != currentViews.Count)
        {
            var originsAtOptimal = currentViews
                .Select(v => (X: v.Origin?.X ?? 0, Y: v.Origin?.Y ?? 0))
                .ToList();

            var probeScale = standardScales.FirstOrDefault(s => System.Math.Abs(s - optimalScale.Value) > 0.5);
            if (probeScale > 0)
            {
                var probeSw = Stopwatch.StartNew();
                foreach (var v in currentViews)
                {
                    v.Attributes.Scale = probeScale;
                    v.Modify();
                }

                activeDrawing.CommitChanges();
                var probeViews = EnumerateViews(activeDrawing).ToList();

                var denom = 1.0 / optimalScale.Value - 1.0 / probeScale;
                for (int i = 0; i < currentViews.Count && i < probeViews.Count; i++)
                {
                    var dX = (probeViews[i].Origin?.X ?? 0) - originsAtOptimal[i].X;
                    var dY = (probeViews[i].Origin?.Y ?? 0) - originsAtOptimal[i].Y;
                    var viewId = currentViews[i].GetIdentifier().ID;
                    offsetById[viewId] = (dX / denom, dY / denom);
                }

                foreach (var v in probeViews)
                {
                    v.Attributes.Scale = optimalScale.Value;
                    v.Modify();
                }

                activeDrawing.CommitChanges();
                currentViews = EnumerateViews(activeDrawing).ToList();
                probeSw.Stop();
                probeMs = probeSw.ElapsedMilliseconds;
            }
        }

        var gridApi = new TeklaDrawingGridApi();
        var preloadedAxes = currentViews
            .Select(v => (Id: v.GetIdentifier().ID, Result: gridApi.GetGridAxes(v.GetIdentifier().ID)))
            .Where(x => x.Result.Success)
            .ToDictionary(x => x.Id, x => (IReadOnlyList<GridAxisInfo>)x.Result.Axes);

        var arrangeSw = Stopwatch.StartNew();
        var arranged = _arrangementSelector.Arrange(
            new DrawingArrangeContext(activeDrawing, currentViews, sheetW, sheetH, effectiveMargin, gap, reservedAreas, TryGetFrameSizesFromBoundingBoxes(currentViews)));
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

                var corrX = off.X / optimalScale.Value;
                var corrY = off.Y / optimalScale.Value;
                // Skip implausibly large corrections: they indicate a bad frame-offset
                // estimate (e.g. from probe-scale extrapolation on distant-origin views)
                // and would displace the view far from where the packer intended.
                if (System.Math.Abs(corrX) > v.Width || System.Math.Abs(corrY) > v.Height)
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
                currentViews,
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
        arranged = TryCenterViewGroup(activeDrawing, finalViews, arranged,
            effectiveMargin, sheetW - effectiveMargin,
            effectiveMargin, sheetH - effectiveMargin,
            reservedAreas);

        // Build reserved-areas output using already-read layoutTables (no extra editor open).
        // Read() without excludeViewIds to include view bounding boxes in the merged output.
        var mergedForOutput = DrawingReservedAreaReader.Read(activeDrawing, effectiveMargin, 0.0,
            preloadedTables: layoutTables);

        var result = new FitViewsResult
        {
            OptimalScale = optimalScale.Value,
            ScalePreserved = preserveExistingScales,
            ScalePolicy = scalePolicy.ToString(),
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
            $"views={viewsCount} candidates={candidateAttempts} selectedScale={(selectedScale.HasValue ? selectedScale.Value.ToString(CultureInfo.InvariantCulture) : "n/a")} scalePolicy={scalePolicy} initMs={initMs} reservedMs={reservedMs} candidateFitMs={candidateFitMs} probeMs={probeMs} arrangeMs={arrangeMs} postAdjustMs={postAdjustMs} projectionMs={projectionMs} projectionMode={(projectionResult?.Mode ?? "none")} projectionApplied={(projectionResult?.AppliedMoves ?? 0)} projectionSkipped={(projectionResult?.SkippedMoves ?? 0)} finalCommitMs={finalCommitMs}");
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
            if (v is not IAxisAlignedBoundingBox bounded)
                return new List<ReservedRect>();

            var box = bounded.GetAxisAlignedBoundingBox();
            if (box == null)
                return new List<ReservedRect>();

            rects.Add(new ReservedRect(box.MinPoint.X, box.MinPoint.Y, box.MaxPoint.X, box.MaxPoint.Y));
        }

        return rects;
    }

    private static List<ReservedRect> ShiftRects(IReadOnlyList<ReservedRect> rects, double dx, double dy)
        => rects.Select(r => new ReservedRect(r.MinX + dx, r.MinY + dy, r.MaxX + dx, r.MaxY + dy)).ToList();

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
}
