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
                var marksViewContext = new MarksViewContextBuilder().Build(view, _model);
                var markEntries = TeklaDrawingMarkLayoutAdapter.CollectEntries(view, marksViewContext);
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
                var marksViewContext = new MarksViewContextBuilder().Build(view, _model);
                var markEntries = TeklaDrawingMarkLayoutAdapter.CollectEntries(view, marksViewContext, viewContext);
                var partPolygonsByModelId = MarkSourceResolver.BuildPartPolygons(viewContext.Parts);
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
                        LeaderCrossingPenalty = 500.0,
                        ViewContext = viewContext,
                        PartPolygonsByModelId = partPolygonsByModelId
                    });
                arrange.Stop();

                var placementById = layoutResult.Placements.ToDictionary(x => x.Id);
                var apply = Stopwatch.StartNew();
                movedIds.AddRange(TeklaDrawingMarkLayoutAdapter.ApplyPlacements(markEntries, placementById, _model));
                apply.Stop();
                var leaderAnchor = Stopwatch.StartNew();
                movedIds.AddRange(TeklaDrawingMarkLayoutAdapter.OptimizeLeaderAnchors(markEntries, partPolygonsByModelId));
                leaderAnchor.Stop();
                totalIterations += layoutResult.Iterations;
                totalRemainingOverlaps += layoutResult.RemainingOverlaps;
                PerfTrace.Write("api-mark", "arrange_marks_view", viewTotal.ElapsedMilliseconds, $"viewId={view.GetIdentifier().ID} marks={markEntries.Count} collectMs={collect.ElapsedMilliseconds} arrangeMs={arrange.ElapsedMilliseconds} applyMs={apply.ElapsedMilliseconds} anchorMs={leaderAnchor.ElapsedMilliseconds} iterations={layoutResult.Iterations}");
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
            PerfTrace.Write("api-mark", "arrange_marks_total", total.ElapsedMilliseconds, $"gap={gap.ToString(CultureInfo.InvariantCulture)}");
            DrawingEnumeratorBase.AutoFetch = previousAutoFetch;
        }
    }

    public ResolveMarksResult ArrangeMarksForce(double gap)
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
                var viewContext = BuildDrawingViewContext(view);
                var marksViewContext = new MarksViewContextBuilder().Build(view, _model);
                var markEntries = TeklaDrawingMarkLayoutAdapter.CollectEntries(view, marksViewContext, viewContext);
                var partPolygonsByModelId = MarkSourceResolver.BuildPartPolygons(viewContext.Parts);
                var partBboxes = viewContext.Parts
                    .Where(static part => part.Success && part.BboxMin.Length >= 2 && part.BboxMax.Length >= 2)
                    .Select(static part => new PartBbox(part.BboxMin[0], part.BboxMin[1], part.BboxMax[0], part.BboxMax[1]))
                    .ToList();
                collect.Stop();

                if (markEntries.Count == 0)
                {
                    PerfTrace.Write("api-mark", "arrange_marks_force_view", viewTotal.ElapsedMilliseconds, $"viewId={view.GetIdentifier().ID} marks=0 collectMs={collect.ElapsedMilliseconds}");
                    continue;
                }

                var forceItems = markEntries
                    .Select(entry => new ForceDirectedMarkItem
                    {
                        Id = entry.Mark.GetIdentifier().ID,
                        Cx = entry.CenterX,
                        Cy = entry.CenterY,
                        Width = entry.Item.Width,
                        Height = entry.Item.Height,
                        CanMove = entry.Item.CanMove,
                        OwnPolygon = entry.Item.SourceModelId.HasValue &&
                                     partPolygonsByModelId.TryGetValue(entry.Item.SourceModelId.Value, out var polygon)
                            ? polygon
                            : null
                    })
                    .ToDictionary(item => item.Id);

                var force = new ForceDirectedMarkPlacer();
                force.PlaceInitial(forceItems.Values.ToList());

                var arrange = Stopwatch.StartNew();
                var relaxIterations = force.Relax(forceItems.Values.ToList(), partBboxes);
                arrange.Stop();

                var placements = markEntries
                    .Select(entry =>
                    {
                        var forceItem = forceItems[entry.Mark.GetIdentifier().ID];
                        return new MarkLayoutPlacement
                        {
                            Id = forceItem.Id,
                            X = forceItem.Cx,
                            Y = forceItem.Cy,
                            Width = entry.Item.Width,
                            Height = entry.Item.Height,
                            AnchorX = entry.Item.AnchorX,
                            AnchorY = entry.Item.AnchorY,
                            HasLeaderLine = entry.Item.HasLeaderLine,
                            HasAxis = entry.Item.HasAxis,
                            AxisDx = entry.Item.AxisDx,
                            AxisDy = entry.Item.AxisDy,
                            CanMove = entry.Item.CanMove,
                            LocalCorners = entry.Item.LocalCorners.Select(c => new[] { c[0], c[1] }).ToList()
                        };
                    })
                    .ToList();

                var resolver = new MarkOverlapResolver();
                var resolve = Stopwatch.StartNew();
                var resolvedPlacements = resolver.ResolvePlacedMarks(
                    placements,
                    new MarkLayoutOptions
                    {
                        Gap = gap,
                        MaxResolverIterations = 24,
                        MaxDistanceFromAnchor = 600.0
                    },
                    out var resolveIterations);
                resolve.Stop();

                var placementById = resolvedPlacements.ToDictionary(x => x.Id);
                var apply = Stopwatch.StartNew();
                movedIds.AddRange(TeklaDrawingMarkLayoutAdapter.ApplyPlacements(markEntries, placementById, _model));
                apply.Stop();

                var leaderAnchor = Stopwatch.StartNew();
                movedIds.AddRange(TeklaDrawingMarkLayoutAdapter.OptimizeLeaderAnchors(markEntries, partPolygonsByModelId));
                leaderAnchor.Stop();

                totalIterations += relaxIterations + resolveIterations;
                totalRemainingOverlaps += resolver.CountOverlaps(resolvedPlacements);
                PerfTrace.Write("api-mark", "arrange_marks_force_view", viewTotal.ElapsedMilliseconds, $"viewId={view.GetIdentifier().ID} marks={markEntries.Count} collectMs={collect.ElapsedMilliseconds} relaxMs={arrange.ElapsedMilliseconds} resolveMs={resolve.ElapsedMilliseconds} applyMs={apply.ElapsedMilliseconds} anchorMs={leaderAnchor.ElapsedMilliseconds} relaxIterations={relaxIterations} resolveIterations={resolveIterations}");
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
            PerfTrace.Write("api-mark", "arrange_marks_force_total", total.ElapsedMilliseconds, $"gap={gap.ToString(CultureInfo.InvariantCulture)}");
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
