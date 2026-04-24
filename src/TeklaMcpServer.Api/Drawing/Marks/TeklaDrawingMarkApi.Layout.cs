using System.Diagnostics;
using System.Globalization;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using TeklaMcpServer.Api.Algorithms.Geometry;
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
                    PerfTrace.Write("api-mark", "resolve_mark_overlaps_view", viewTotal.ElapsedMilliseconds, $"viewId={view.GetIdentifier().ID} scale={marksViewContext.ViewScale} marks=0 collectMs={collect.ElapsedMilliseconds}");
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
                PerfTrace.Write("api-mark", "resolve_mark_overlaps_view", viewTotal.ElapsedMilliseconds, $"viewId={view.GetIdentifier().ID} scale={marksViewContext.ViewScale} marks={markEntries.Count} collectMs={collect.ElapsedMilliseconds} resolveMs={resolve.ElapsedMilliseconds} applyMs={apply.ElapsedMilliseconds} iterations={iterations} finalOverlaps={finalViewOverlaps}");
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
                    PerfTrace.Write("api-mark", "arrange_marks_view", viewTotal.ElapsedMilliseconds, $"viewId={view.GetIdentifier().ID} scale={viewContext.ViewScale} marks=0 collectMs={collect.ElapsedMilliseconds}");
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
                PerfTrace.Write("api-mark", "arrange_marks_view", viewTotal.ElapsedMilliseconds, $"viewId={view.GetIdentifier().ID} scale={viewContext.ViewScale} marks={markEntries.Count} collectMs={collect.ElapsedMilliseconds} arrangeMs={arrange.ElapsedMilliseconds} applyMs={apply.ElapsedMilliseconds} anchorMs={leaderAnchor.ElapsedMilliseconds} iterations={layoutResult.Iterations}");
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
                    .Select(part =>
                    {
                        partPolygonsByModelId.TryGetValue(part.ModelId, out var poly);
                        return new PartBbox(part.ModelId, part.BboxMin[0], part.BboxMin[1], part.BboxMax[0], part.BboxMax[1], poly);
                    })
                    .ToList();
                collect.Stop();

                if (markEntries.Count == 0)
                {
                    PerfTrace.Write("api-mark", "arrange_marks_force_view", viewTotal.ElapsedMilliseconds, $"viewId={view.GetIdentifier().ID} scale={viewContext.ViewScale} marks=0 collectMs={collect.ElapsedMilliseconds}");
                    continue;
                }

                var initialPositions = markEntries.ToDictionary(
                    e => e.Mark.GetIdentifier().ID,
                    e => (e.CenterX, e.CenterY));

                var forceItems = markEntries
                    .Select(entry => new ForceDirectedMarkItem
                    {
                        Id = entry.Mark.GetIdentifier().ID,
                        OwnModelId = entry.Item.SourceModelId,
                        Cx = entry.CenterX,
                        Cy = entry.CenterY,
                        Width = entry.Item.Width,
                        Height = entry.Item.Height,
                        CanMove = entry.Item.CanMove,
                        ConstrainToAxis = entry.Item.HasAxis && !entry.Item.HasLeaderLine,
                        ReturnToAxisLine = false,
                        AxisDx = entry.Item.AxisDx,
                        AxisDy = entry.Item.AxisDy,
                        AxisOriginX = entry.Item.SourceCenterX ?? entry.CenterX,
                        AxisOriginY = entry.Item.SourceCenterY ?? entry.CenterY,
                        LocalCorners = entry.Item.LocalCorners.Select(c => new[] { c[0], c[1] }).ToList(),
                        OwnPolygon = entry.Item.SourceModelId.HasValue &&
                                     partPolygonsByModelId.TryGetValue(entry.Item.SourceModelId.Value, out var polygon)
                            ? polygon
                            : null
                    })
                    .ToDictionary(item => item.Id);

                var force = new ForceDirectedMarkPlacer();
                var pass1Options = ForcePassOptions.CreatePass1ForViewScale(viewContext.ViewScale);
                var pass2Options = ForcePassOptions.CreatePass2ForViewScale(viewContext.ViewScale);
                force.PlaceInitial(forceItems.Values.ToList(), partBboxes);

                var arrange = Stopwatch.StartNew();
                var pass1Result = force.Relax(forceItems.Values.ToList(), partBboxes, pass1Options,
                    debugSink: debug =>
                    {
                        if (!PerfTrace.IsActive) return;
                        PerfTrace.Write("api-mark", "arrange_marks_force_pass1_mark", 0,
                            $"viewId={view.GetIdentifier().ID} iter={debug.Iteration} markId={debug.MarkId} " +
                            $"attract=({debug.AttractFx:F3},{debug.AttractFy:F3}) " +
                            $"partRepel=({debug.PartRepelFx:F3},{debug.PartRepelFy:F3}) " +
                            $"force=({debug.Fx:F3},{debug.Fy:F3}) " +
                            $"delta=({debug.Dx:F3},{debug.Dy:F3}) " +
                            $"pos=({debug.X:F3},{debug.Y:F3})");
                    });
                var pass1Placements = BuildForcePlacements(markEntries, forceItems);
                var collidingIds = GetOverlappingMarkIds(pass1Placements);

                // Baseline marks that collide are freed for Pass 2 — solver can push them perpendicular to axis
                foreach (var id in collidingIds)
                    if (forceItems.TryGetValue(id, out var item) && item.ConstrainToAxis)
                    {
                        item.ConstrainToAxis = false;
                        item.ReturnToAxisLine = true;
                    }

                var pass2Result = new ForceRelaxResult(0, ForceRelaxStopReason.NotRun);
                if (collidingIds.Count > 0)
                {
                    pass2Result = force.Relax(
                        forceItems.Values.ToList(),
                        partBboxes,
                        pass2Options,
                        includeMarkRepulsion: true,
                        movableIds: collidingIds,
                        debugSink: debug =>
                        {
                            if (!PerfTrace.IsActive)
                                return;

                            PerfTrace.Write(
                                "api-mark",
                                "arrange_marks_force_pass2_mark",
                                0,
                                $"viewId={view.GetIdentifier().ID} iter={debug.Iteration} markId={debug.MarkId} " +
                                $"attract=({debug.AttractFx:F3},{debug.AttractFy:F3}) " +
                                $"partRepel=({debug.PartRepelFx:F3},{debug.PartRepelFy:F3}) " +
                                $"markRepel=({debug.MarkRepelFx:F3},{debug.MarkRepelFy:F3}) " +
                                $"force=({debug.Fx:F3},{debug.Fy:F3}) " +
                                $"delta=({debug.Dx:F3},{debug.Dy:F3}) " +
                                $"pos=({debug.X:F3},{debug.Y:F3})");
                        },
                        getRemainingOverlapCount: () =>
                        {
                            var currentPlacements = BuildForcePlacements(markEntries, forceItems);
                            return GetOverlappingMarkIds(currentPlacements).Count(id => collidingIds.Contains(id));
                        });
                }
                arrange.Stop();

                var placements = BuildForcePlacements(markEntries, forceItems);

                if (PerfTrace.IsActive)
                {
                    foreach (var item in forceItems.Values)
                    {
                        if (!initialPositions.TryGetValue(item.Id, out var init)) continue;
                        var netDx = item.Cx - init.CenterX;
                        var netDy = item.Cy - init.CenterY;
                        var inColliding = collidingIds.Contains(item.Id);
                        PerfTrace.Write("api-mark", "arrange_marks_force_net",  0,
                            $"viewId={view.GetIdentifier().ID} markId={item.Id} " +
                            $"initPos=({init.CenterX:F1},{init.CenterY:F1}) " +
                            $"finalPos=({item.Cx:F1},{item.Cy:F1}) " +
                            $"net=({netDx:F1},{netDy:F1}) colliding={inColliding}");
                    }
                }

                var resolver = new MarkOverlapResolver();
                var placementById = placements.ToDictionary(x => x.Id);
                var apply = Stopwatch.StartNew();
                movedIds.AddRange(TeklaDrawingMarkLayoutAdapter.ApplyPlacements(markEntries, placementById, _model, respectAxisConstraint: false));
                apply.Stop();

                var leaderAnchor = Stopwatch.StartNew();
                movedIds.AddRange(TeklaDrawingMarkLayoutAdapter.OptimizeLeaderAnchors(markEntries, partPolygonsByModelId));
                leaderAnchor.Stop();

                totalIterations += pass1Result.Iterations + pass2Result.Iterations;
                totalRemainingOverlaps += resolver.CountOverlaps(placements);
                PerfTrace.Write("api-mark", "arrange_marks_force_view", viewTotal.ElapsedMilliseconds, $"viewId={view.GetIdentifier().ID} scale={viewContext.ViewScale} forceScalePolicy=paperThresholds marks={markEntries.Count} collectMs={collect.ElapsedMilliseconds} relaxMs={arrange.ElapsedMilliseconds} applyMs={apply.ElapsedMilliseconds} anchorMs={leaderAnchor.ElapsedMilliseconds} pass1Iterations={pass1Result.Iterations} pass2Iterations={pass2Result.Iterations} collidingMarks={collidingIds.Count} pass2StopReason={pass2Result.StopReason} pass2EarlyExit={pass2Result.StopReason == ForceRelaxStopReason.OverlapsCleared}");
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

    private static List<MarkLayoutPlacement> BuildForcePlacements(
        IReadOnlyList<TeklaDrawingMarkLayoutEntry> markEntries,
        IReadOnlyDictionary<int, ForceDirectedMarkItem> forceItems)
    {
        return markEntries
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
    }

    private static HashSet<int> GetOverlappingMarkIds(IReadOnlyList<MarkLayoutPlacement> placements)
    {
        var result = new HashSet<int>();
        for (var i = 0; i < placements.Count; i++)
        for (var j = i + 1; j < placements.Count; j++)
        {
            if (!PlacementsOverlap(placements[i], placements[j]))
                continue;

            result.Add(placements[i].Id);
            result.Add(placements[j].Id);
        }

        return result;
    }

    private static bool PlacementsOverlap(MarkLayoutPlacement a, MarkLayoutPlacement b)
    {
        if (a.LocalCorners.Count >= 3 && b.LocalCorners.Count >= 3)
        {
            var aPolygon = PolygonGeometry.Translate(a.LocalCorners, a.X, a.Y);
            var bPolygon = PolygonGeometry.Translate(b.LocalCorners, b.X, b.Y);
            return PolygonGeometry.Intersects(aPolygon, bPolygon);
        }

        var overlapX = Math.Min(a.X + (a.Width / 2.0), b.X + (b.Width / 2.0))
            - Math.Max(a.X - (a.Width / 2.0), b.X - (b.Width / 2.0));
        var overlapY = Math.Min(a.Y + (a.Height / 2.0), b.Y + (b.Height / 2.0))
            - Math.Max(a.Y - (a.Height / 2.0), b.Y - (b.Height / 2.0));
        return overlapX > 0 && overlapY > 0;
    }
}
