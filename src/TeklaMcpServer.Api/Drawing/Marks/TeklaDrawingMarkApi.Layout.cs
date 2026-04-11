using System.Diagnostics;
using System.Globalization;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using TeklaMcpServer.Api.Algorithms.Marks;
using TeklaMcpServer.Api.Diagnostics;

namespace TeklaMcpServer.Api.Drawing;

public sealed partial class TeklaDrawingMarkApi
{
    public ResolveMarksResult ResolveMarkOverlaps(double margin)
    {
        var total = Stopwatch.StartNew();
        var activeDrawing = new DrawingHandler().GetActiveDrawing();
        if (activeDrawing == null)
            throw new DrawingNotOpenException();

        var previousAutoFetch = DrawingEnumeratorBase.AutoFetch;
        DrawingEnumeratorBase.AutoFetch = false;
        try
        {
            var movedIds = new List<int>();
            var totalIterations = 0;
            var totalRemainingOverlaps = 0;

            foreach (var view in EnumerateViews(activeDrawing))
            {
                var viewTotal = Stopwatch.StartNew();
                var collect = Stopwatch.StartNew();
                var markEntries = TeklaDrawingMarkLayoutAdapter.CollectEntries(view, _model);
                collect.Stop();

                if (markEntries.Count == 0)
                {
                    PerfTrace.Write("api-mark", "resolve_mark_overlaps_view", viewTotal.ElapsedMilliseconds, $"viewId={view.GetIdentifier().ID} marks=0 collectMs={collect.ElapsedMilliseconds}");
                    continue;
                }

                var placements = markEntries.Select(e => new MarkLayoutPlacement
                {
                    Id = e.Mark.GetIdentifier().ID,
                    X = e.CenterX,
                    Y = e.CenterY,
                    Width = e.Item.Width,
                    Height = e.Item.Height,
                    AnchorX = e.Item.AnchorX,
                    AnchorY = e.Item.AnchorY,
                    HasLeaderLine = e.Item.HasLeaderLine,
                    HasAxis = e.Item.HasAxis,
                    AxisDx = e.Item.AxisDx,
                    AxisDy = e.Item.AxisDy,
                    CanMove = true,
                    LocalCorners = e.Item.LocalCorners.Select(c => new[] { c[0], c[1] }).ToList()
                }).ToList();

                var resolver = new MarkOverlapResolver();
                var resolve = Stopwatch.StartNew();
                var resolved = resolver.ResolvePlacedMarks(
                    placements,
                    new MarkLayoutOptions
                    {
                        Gap = margin,
                        MaxResolverIterations = 24,
                        MaxDistanceFromAnchor = 40.0
                    },
                    out var iterations);
                resolve.Stop();
                var resolvedById = resolved.ToDictionary(x => x.Id);

                var apply = Stopwatch.StartNew();
                movedIds.AddRange(TeklaDrawingMarkLayoutAdapter.ApplyPlacements(markEntries, resolvedById, _model));
                apply.Stop();
                totalIterations += iterations;

                var finalViewOverlaps = GetMarks(view.GetIdentifier().ID).Overlaps.Count;
                totalRemainingOverlaps += finalViewOverlaps;
                PerfTrace.Write("api-mark", "resolve_mark_overlaps_view", viewTotal.ElapsedMilliseconds, $"viewId={view.GetIdentifier().ID} marks={markEntries.Count} collectMs={collect.ElapsedMilliseconds} resolveMs={resolve.ElapsedMilliseconds} applyMs={apply.ElapsedMilliseconds} iterations={iterations} finalOverlaps={finalViewOverlaps}");
            }

            movedIds = movedIds.Distinct().ToList();

            if (movedIds.Count > 0)
                activeDrawing.CommitChanges();

            return new ResolveMarksResult
            {
                MarksMovedCount = movedIds.Count,
                MovedIds = movedIds,
                Iterations = totalIterations,
                RemainingOverlaps = totalRemainingOverlaps
            };
        }
        finally
        {
            PerfTrace.Write("api-mark", "resolve_mark_overlaps_total", total.ElapsedMilliseconds, $"margin={margin.ToString(CultureInfo.InvariantCulture)}");
            DrawingEnumeratorBase.AutoFetch = previousAutoFetch;
        }
    }

    public ResolveMarksResult ArrangeMarks(double gap)
    {
        var total = Stopwatch.StartNew();
        var activeDrawing = new DrawingHandler().GetActiveDrawing();
        if (activeDrawing == null)
            throw new DrawingNotOpenException();

        var previousAutoFetch = DrawingEnumeratorBase.AutoFetch;
        DrawingEnumeratorBase.AutoFetch = false;
        try
        {
            var engine = new MarkLayoutEngine();
            var movedIds = new List<int>();
            var totalIterations = 0;
            var totalRemainingOverlaps = 0;

            foreach (var view in EnumerateViews(activeDrawing))
            {
                var viewTotal = Stopwatch.StartNew();
                var collect = Stopwatch.StartNew();
                var viewContext = BuildDrawingViewContext(view);
                var markEntries = TeklaDrawingMarkLayoutAdapter.CollectEntries(view, _model, viewContext);
                collect.Stop();
                if (markEntries.Count == 0)
                {
                    PerfTrace.Write("api-mark", "arrange_marks_view", viewTotal.ElapsedMilliseconds, $"viewId={view.GetIdentifier().ID} marks=0 collectMs={collect.ElapsedMilliseconds}");
                    continue;
                }

                var arrange = Stopwatch.StartNew();
                var layoutResult = engine.Arrange(
                    markEntries.Select(x => x.Item),
                    new MarkLayoutOptions
                    {
                        Gap = gap,
                        CurrentPositionWeight = 1.2,
                        AnchorDistanceWeight = 2.5,
                        SourceDistanceWeight = 1.25,
                        SourceOutsideOwnPartPenalty = 150.0,
                        ForeignPartOverlapPenalty = 250.0,
                        MaxDistanceFromAnchor = 600.0,
                        CandidateDistanceMultipliers = new[] { 1.0, 2.0, 4.0, 8.0, 16.0 },
                        LeaderLengthWeight = 15.0,
                        ViewContext = viewContext,
                        PartPolygonsByModelId = MarkSourceResolver.BuildPartPolygons(viewContext.Parts)
                    });
                arrange.Stop();

                var placementById = layoutResult.Placements.ToDictionary(x => x.Id);
                var apply = Stopwatch.StartNew();
                movedIds.AddRange(TeklaDrawingMarkLayoutAdapter.ApplyPlacements(markEntries, placementById, _model));
                apply.Stop();
                totalIterations += layoutResult.Iterations;
                totalRemainingOverlaps += layoutResult.RemainingOverlaps;
                PerfTrace.Write("api-mark", "arrange_marks_view", viewTotal.ElapsedMilliseconds, $"viewId={view.GetIdentifier().ID} marks={markEntries.Count} collectMs={collect.ElapsedMilliseconds} arrangeMs={arrange.ElapsedMilliseconds} applyMs={apply.ElapsedMilliseconds} iterations={layoutResult.Iterations}");
            }

            if (movedIds.Count > 0)
                activeDrawing.CommitChanges();

            return new ResolveMarksResult
            {
                MarksMovedCount = movedIds.Count,
                MovedIds = movedIds,
                Iterations = totalIterations,
                RemainingOverlaps = totalRemainingOverlaps
            };
        }
        finally
        {
            PerfTrace.Write("api-mark", "arrange_marks_total", total.ElapsedMilliseconds, $"gap={gap.ToString(CultureInfo.InvariantCulture)}");
            DrawingEnumeratorBase.AutoFetch = previousAutoFetch;
        }
    }

    private DrawingViewContext BuildDrawingViewContext(View view)
    {
        var viewId = view.GetIdentifier().ID;
        var viewScale = MarksViewContextBuilder.ResolveViewScale(view);
        var builder = new DrawingViewContextBuilder(
            new TeklaDrawingPartGeometryApi(_model),
            new TeklaDrawingBoltGeometryApi(_model),
            new TeklaDrawingGridApi());
        return builder.Build(viewId, viewScale);
    }
}
