using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Model;
using TeklaMcpServer.Api.Diagnostics;

namespace TeklaMcpServer.Api.Drawing;

public sealed partial class TeklaDrawingViewApi
{
    public FitViewsResult FitViewsToSheet(double margin, double gap, double titleBlockHeight)
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

        if (margin < 0)
            throw new System.ArgumentOutOfRangeException(nameof(margin), "margin must be >= 0.");
        if (gap < 0)
            throw new System.ArgumentOutOfRangeException(nameof(gap), "gap must be >= 0.");
        if (titleBlockHeight < 0)
            throw new System.ArgumentOutOfRangeException(nameof(titleBlockHeight), "titleBlockHeight must be >= 0.");

        var init = Stopwatch.StartNew();
        var activeDrawing = new DrawingHandler().GetActiveDrawing();
        if (activeDrawing == null)
            throw new DrawingNotOpenException();

        var views = EnumerateViews(activeDrawing).ToList();
        viewsCount = views.Count;
        if (views.Count == 0)
            throw new System.InvalidOperationException("No views found in active drawing.");

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
            margin,
            titleBlockHeight,
            viewIds);
        reservedRead.Stop();
        reservedMs = reservedRead.ElapsedMilliseconds;

        var availW = sheetW - (2 * margin);
        var availH = sheetH - (2 * margin);
        if (availW <= 0 || availH <= 0)
            throw new System.InvalidOperationException("No drawable area left after applying margin.");

        double currentScale = views.Select(v => v.Attributes.Scale).FirstOrDefault(s => s > 0);
        if (currentScale <= 0)
            currentScale = 1.0;

        const double borderEst = 20.0;
        var maxModelW = views.Max(v => v.Width * currentScale);
        var maxModelH = views.Max(v => v.Height * currentScale);
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

        foreach (var s in candidates)
        {
            candidateAttempts++;
            var candidateSw = Stopwatch.StartNew();
            foreach (var v in currentViews)
            {
                v.Attributes.Scale = s;
                v.Modify();
            }

            activeDrawing.CommitChanges();

            var reread = EnumerateViews(activeDrawing).ToList();
            var actualFrames = reread.Select(v => (w: v.Width, h: v.Height)).ToList();
            var ctx = new DrawingArrangeContext(activeDrawing, reread, sheetW, sheetH, margin, gap, reservedAreas);

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

        var offsetById = TryGetFrameOffsetsFromBoundingBoxes(currentViews, optimalScale.Value);
        if (offsetById.Count != currentViews.Count)
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
            new DrawingArrangeContext(activeDrawing, currentViews, sheetW, sheetH, margin, gap, reservedAreas));
        arrangeSw.Stop();
        arrangeMs = arrangeSw.ElapsedMilliseconds;

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

                var o = v.Origin;
                o.X = arranged[i].OriginX - off.X / optimalScale.Value;
                o.Y = arranged[i].OriginY - off.Y / optimalScale.Value;
                v.Origin = o;
                v.Modify();
                arranged[i] = new ArrangedView
                {
                    Id = arranged[i].Id,
                    ViewType = arranged[i].ViewType,
                    OriginX = o.X,
                    OriginY = o.Y
                };
            }

            adjustSw.Stop();
            postAdjustMs = adjustSw.ElapsedMilliseconds;
        }

        var projectionSw = Stopwatch.StartNew();
        var projectionAlignmentService = new DrawingProjectionAlignmentService(new Model());
        projectionResult = projectionAlignmentService.Apply(
            activeDrawing,
            currentViews,
            offsetById,
            sheetW,
            sheetH,
            margin,
            reservedAreas,
            arranged,
            preloadedAxes);
        projectionSw.Stop();
        projectionMs = projectionSw.ElapsedMilliseconds;

        var commitSw = Stopwatch.StartNew();
        activeDrawing.CommitChanges();
        commitSw.Stop();
        finalCommitMs = commitSw.ElapsedMilliseconds;
        selectedScale = optimalScale;

        var result = new FitViewsResult
        {
            OptimalScale = optimalScale.Value,
            SheetWidth = sheetW,
            SheetHeight = sheetH,
            Arranged = arranged.Count,
            Views = arranged,
            ProjectionApplied = projectionResult?.AppliedMoves ?? 0,
            ProjectionSkipped = projectionResult?.SkippedMoves ?? 0,
            ProjectionDiagnostics = projectionResult?.Diagnostics.Count > 0 ? projectionResult.Diagnostics : null
        };

        PerfTrace.Write(
            "api-view",
            "fit_views_to_sheet",
            total.ElapsedMilliseconds,
            $"views={viewsCount} candidates={candidateAttempts} selectedScale={(selectedScale.HasValue ? selectedScale.Value.ToString(CultureInfo.InvariantCulture) : "n/a")} initMs={initMs} reservedMs={reservedMs} candidateFitMs={candidateFitMs} probeMs={probeMs} arrangeMs={arrangeMs} postAdjustMs={postAdjustMs} projectionMs={projectionMs} projectionMode={(projectionResult?.Mode ?? "none")} projectionApplied={(projectionResult?.AppliedMoves ?? 0)} projectionSkipped={(projectionResult?.SkippedMoves ?? 0)} finalCommitMs={finalCommitMs}");
        return result;
    }
}
