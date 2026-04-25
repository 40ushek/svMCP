using System.Diagnostics;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Model;
using TeklaMcpServer.Api.Algorithms.Geometry;
using TeklaMcpServer.Api.Algorithms.Marks;
using TeklaMcpServer.Api.Diagnostics;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class ForceMarkLayoutOrchestrator
{
    private readonly Model _model;

    public ForceMarkLayoutOrchestrator(Model model) => _model = model;

    public ResolveMarksResult Arrange(Tekla.Structures.Drawing.Drawing activeDrawing, IReadOnlyList<View> views)
    {
        var movedIds = new List<int>();
        var totalIterations = 0;
        var totalRemainingOverlaps = 0;

        foreach (var view in views)
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

            var normalizedScale = viewContext.ViewScale > 0.0 ? viewContext.ViewScale : 1.0;
            var leaderTextThreshold = 0.5 * normalizedScale;
            var leaderTextInitial = PerfTrace.IsActive
                ? LeaderTextOverlapAnalyzer.Analyze(BuildLeaderTextOverlapMarks(markEntries), leaderTextThreshold)
                : new LeaderTextOverlapSummary();
            WriteLeaderTextOverlapTrace(view.GetIdentifier().ID, "initial", leaderTextInitial);

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
                    HasLeaderLine = entry.Item.HasLeaderLine,
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
            var equilibriumOptions = ForcePassOptions.CreateEquilibriumForViewScale(viewContext.ViewScale);
            var markSeparationOptions = ForcePassOptions.CreateMarkSeparationForViewScale(viewContext.ViewScale);
            var foreignPartThreshold = 0.5 * normalizedScale;
            var foreignInitial = ForeignPartOverlapAnalyzer.Analyze(forceItems.Values.ToList(), partBboxes, foreignPartThreshold);
            WriteForeignPartOverlapTrace(view.GetIdentifier().ID, "initial", foreignInitial);
            force.PlaceInitial(forceItems.Values.ToList(), partBboxes);

            var arrange = Stopwatch.StartNew();
            var equilibriumResult = force.Relax(forceItems.Values.ToList(), partBboxes, equilibriumOptions,
                debugSink: debug =>
                {
                    if (!PerfTrace.IsActive) return;
                    PerfTrace.Write("api-mark", "arrange_marks_force_equilibrium_mark", 0,
                        $"viewId={view.GetIdentifier().ID} iter={debug.Iteration} markId={debug.MarkId} " +
                        $"attract=({debug.AttractFx:F3},{debug.AttractFy:F3}) " +
                        $"partRepel=({debug.PartRepelFx:F3},{debug.PartRepelFy:F3}) " +
                        $"force=({debug.Fx:F3},{debug.Fy:F3}) " +
                        $"delta=({debug.Dx:F3},{debug.Dy:F3}) " +
                        $"pos=({debug.X:F3},{debug.Y:F3}) " +
                        $"hasLeaderLine={debug.HasLeaderLine} " +
                        $"leaderInsideOwnPart={debug.LeaderInsideOwnPart} " +
                        $"leaderDistPaper={debug.LeaderDistPaper:F3} " +
                        $"leaderExcessPaper={debug.LeaderExcessPaper:F3} " +
                        $"leaderExtraAttract={debug.LeaderExtraAttract:F3}");
                });
            var foreignAfterEquilibrium = ForeignPartOverlapAnalyzer.Analyze(forceItems.Values.ToList(), partBboxes, foreignPartThreshold);
            WriteForeignPartOverlapTrace(view.GetIdentifier().ID, "afterEquilibrium", foreignAfterEquilibrium);
            var foreignCleanupResult = force.CleanupForeignPartOverlaps(
                forceItems.Values.ToList(),
                partBboxes,
                foreignPartThreshold,
                maxStep: 0.5 * normalizedScale,
                trace: details =>
                {
                    if (!PerfTrace.IsActive)
                        return;

                    PerfTrace.Write(
                        "api-mark",
                        "arrange_marks_force_foreign_cleanup",
                        0,
                        $"viewId={view.GetIdentifier().ID} stage=beforeMarkSeparation {details}");
                });
            var foreignAfterCleanup = ForeignPartOverlapAnalyzer.Analyze(forceItems.Values.ToList(), partBboxes, foreignPartThreshold);
            WriteForeignPartOverlapTrace(view.GetIdentifier().ID, "afterForeignCleanup", foreignAfterCleanup);

            var axisSeparationResult = AxisMarkSeparationCleanup.Resolve(
                forceItems.Values.ToList(),
                markSeparationOptions.MarkGapMm);
            var preSeparationPlacements = BuildForcePlacements(markEntries, forceItems);
            var collidingIds = GetOverlappingMarkIds(preSeparationPlacements);

            // Baseline marks that collide are freed for mark separation, so solver can push them perpendicular to axis.
            foreach (var id in collidingIds)
                if (forceItems.TryGetValue(id, out var item) && item.ConstrainToAxis)
                {
                    item.ConstrainToAxis = false;
                    item.ReturnToAxisLine = true;
                }

            var markSeparationResult = new ForceRelaxResult(0, ForceRelaxStopReason.NotRun);
            if (collidingIds.Count > 0)
            {
                markSeparationResult = force.Relax(
                    forceItems.Values.ToList(),
                    partBboxes,
                    markSeparationOptions,
                    includeMarkRepulsion: true,
                    movableIds: collidingIds,
                    debugSink: debug =>
                    {
                        if (!PerfTrace.IsActive)
                            return;

                        PerfTrace.Write(
                            "api-mark",
                            "arrange_marks_force_mark_separation_mark",
                            0,
                            $"viewId={view.GetIdentifier().ID} iter={debug.Iteration} markId={debug.MarkId} " +
                            $"attract=({debug.AttractFx:F3},{debug.AttractFy:F3}) " +
                            $"partRepel=({debug.PartRepelFx:F3},{debug.PartRepelFy:F3}) " +
                            $"markRepel=({debug.MarkRepelFx:F3},{debug.MarkRepelFy:F3}) " +
                            $"force=({debug.Fx:F3},{debug.Fy:F3}) " +
                            $"delta=({debug.Dx:F3},{debug.Dy:F3}) " +
                            $"pos=({debug.X:F3},{debug.Y:F3}) " +
                            $"axisStep={debug.AxisStepMode} " +
                            $"axisStepReason={debug.AxisStepReason}");
                    },
                    getRemainingOverlapCount: () =>
                    {
                        var currentPlacements = BuildForcePlacements(markEntries, forceItems);
                        return GetOverlappingMarkIds(currentPlacements).Count(id => collidingIds.Contains(id));
                    },
                    preferAxisStepForReturnToAxisMarks: true,
                    foreignPartThreshold: foreignPartThreshold);
            }

            var foreignAfterMarkSeparation = ForeignPartOverlapAnalyzer.Analyze(forceItems.Values.ToList(), partBboxes, foreignPartThreshold);
            WriteForeignPartOverlapTrace(view.GetIdentifier().ID, "afterMarkSeparation", foreignAfterMarkSeparation);
            var finalForeignCleanupResult = new ForeignPartCleanupResult(
                0,
                0,
                foreignAfterMarkSeparation.PartialConflicts,
                foreignAfterMarkSeparation.PartialSeverity,
                foreignAfterMarkSeparation.PartialConflicts,
                foreignAfterMarkSeparation.PartialSeverity);
            if (markSeparationResult.StopReason != ForceRelaxStopReason.NotRun)
            {
                finalForeignCleanupResult = force.CleanupForeignPartOverlaps(
                    forceItems.Values.ToList(),
                    partBboxes,
                    foreignPartThreshold,
                    maxStep: 0.5 * normalizedScale,
                    trace: details =>
                    {
                        if (!PerfTrace.IsActive)
                            return;

                        PerfTrace.Write(
                            "api-mark",
                            "arrange_marks_force_foreign_cleanup",
                            0,
                            $"viewId={view.GetIdentifier().ID} stage=finalForeignCleanup {details}");
                    });
            }
            arrange.Stop();

            var placements = BuildForcePlacements(markEntries, forceItems);
            var foreignFinal = ForeignPartOverlapAnalyzer.Analyze(forceItems.Values.ToList(), partBboxes, foreignPartThreshold);
            WriteForeignPartOverlapTrace(view.GetIdentifier().ID, "final", foreignFinal);

            if (PerfTrace.IsActive)
            {
                foreach (var item in forceItems.Values)
                {
                    if (!initialPositions.TryGetValue(item.Id, out var init)) continue;
                    var netDx = item.Cx - init.CenterX;
                    var netDy = item.Cy - init.CenterY;
                    var inColliding = collidingIds.Contains(item.Id);
                    PerfTrace.Write("api-mark", "arrange_marks_force_net", 0,
                        $"viewId={view.GetIdentifier().ID} markId={item.Id} " +
                        $"initPos=({init.CenterX:F1},{init.CenterY:F1}) " +
                        $"finalPos=({item.Cx:F1},{item.Cy:F1}) " +
                        $"net=({netDx:F1},{netDy:F1}) colliding={inColliding}");
                }
            }

            var resolver = new MarkOverlapResolver();
            var placementById = placements.ToDictionary(x => x.Id);
            var apply = Stopwatch.StartNew();
            var appliedIds = TeklaDrawingMarkLayoutAdapter.ApplyPlacements(markEntries, placementById, _model, respectAxisConstraint: false);
            movedIds.AddRange(appliedIds);
            apply.Stop();
            var leaderAnchor = Stopwatch.StartNew();
            if (appliedIds.Count > 0)
                activeDrawing.CommitChanges("(MCP) ArrangeMarksForce body");

            var refreshedMarksViewContext = new MarksViewContextBuilder().Build(view, _model);
            var refreshedMarkEntries = TeklaDrawingMarkLayoutAdapter.CollectEntries(view, refreshedMarksViewContext, viewContext);
            var leaderAnchorResult = TeklaDrawingMarkLayoutAdapter.OptimizeLeaderAnchors(
                refreshedMarkEntries,
                partPolygonsByModelId,
                _model,
                activeDrawing);
            movedIds.AddRange(leaderAnchorResult.AcceptedIds);
            leaderAnchor.Stop();

            // Always refresh to get updated leader snapshots after anchor optimization
            var leaderTextCleanup = Stopwatch.StartNew();
            var finalMarksViewContext = new MarksViewContextBuilder().Build(view, _model);
            var finalMarkEntries = TeklaDrawingMarkLayoutAdapter.CollectEntries(view, finalMarksViewContext, viewContext);
            var finalLeaderTextMarks = BuildLeaderTextOverlapMarks(finalMarkEntries);
            var leaderTextDryRun = LeaderTextCleanupDryRunner.Analyze(
                finalLeaderTextMarks, finalMarkEntries, partPolygonsByModelId,
                viewContext.ViewScale, leaderTextThreshold);
            var leaderTextCleanupResult = TeklaDrawingMarkLayoutAdapter.ApplyLeaderTextCleanup(
                finalMarkEntries, leaderTextDryRun, finalLeaderTextMarks, leaderTextThreshold, _model, activeDrawing);
            movedIds.AddRange(leaderTextCleanupResult.AcceptedIds);
            leaderTextCleanup.Stop();

            var postCleanupViewContext = new MarksViewContextBuilder().Build(view, _model);
            var postCleanupEntries = TeklaDrawingMarkLayoutAdapter.CollectEntries(view, postCleanupViewContext, viewContext);
            var postCleanupLeaderTextMarks = BuildLeaderTextOverlapMarks(postCleanupEntries);

            var leaderTextFinal = PerfTrace.IsActive
                ? LeaderTextOverlapAnalyzer.Analyze(postCleanupLeaderTextMarks, leaderTextThreshold)
                : new LeaderTextOverlapSummary();
            WriteLeaderTextOverlapTrace(view.GetIdentifier().ID, "final", leaderTextFinal);

            if (PerfTrace.IsActive)
            {
                PerfTrace.Write("api-mark", "leader_text_cleanup_dry_run_summary", 0,
                    $"viewId={view.GetIdentifier().ID} conflictingMarks={leaderTextDryRun.ConflictingMarks} improvableMarks={leaderTextDryRun.ImprovableMarks} totalCurrentSeverity={leaderTextDryRun.TotalCurrentSeverity:F3} totalBestCaseSeverity={leaderTextDryRun.TotalBestCaseSeverity:F3} accepted={leaderTextCleanupResult.AcceptedIds.Count} rejected={leaderTextCleanupResult.RejectedIds.Count} cleanupMs={leaderTextCleanup.ElapsedMilliseconds}");
                foreach (var m in leaderTextDryRun.Marks)
                    PerfTrace.Write("api-mark", "leader_text_cleanup_dry_run_mark", 0,
                        $"viewId={view.GetIdentifier().ID} markId={m.MarkId} currentSeverity={m.CurrentSeverity:F3} hasImprovement={m.HasImprovement} bestKind={m.BestKind} bestDelta={m.BestDeltaSeverity:F3} bestProjected={m.BestProjectedSeverity:F3} bestAnchor=({m.BestAnchorX:F1},{m.BestAnchorY:F1})");
            }

            totalIterations += equilibriumResult.Iterations + foreignCleanupResult.Iterations + axisSeparationResult.Iterations + markSeparationResult.Iterations + finalForeignCleanupResult.Iterations;
            totalRemainingOverlaps += resolver.CountOverlaps(BuildPlacementsFromEntries(postCleanupEntries));
            PerfTrace.Write("api-mark", "arrange_marks_force_view", viewTotal.ElapsedMilliseconds, $"viewId={view.GetIdentifier().ID} scale={viewContext.ViewScale} forceScalePolicy=paperThresholds marks={markEntries.Count} collectMs={collect.ElapsedMilliseconds} relaxMs={arrange.ElapsedMilliseconds} applyMs={apply.ElapsedMilliseconds} anchorMs={leaderAnchor.ElapsedMilliseconds} anchorAccepted={leaderAnchorResult.AcceptedIds.Count} anchorRejected={leaderAnchorResult.RejectedIds.Count} leaderCleanupMs={leaderTextCleanup.ElapsedMilliseconds} leaderCleanupAccepted={leaderTextCleanupResult.AcceptedIds.Count} leaderCleanupRejected={leaderTextCleanupResult.RejectedIds.Count} equilibriumIterations={equilibriumResult.Iterations} foreignCleanupIterations={foreignCleanupResult.Iterations} foreignCleanupMoved={foreignCleanupResult.MovedMarks} foreignCleanupBeforePartial={foreignCleanupResult.BeforePartialConflicts} foreignCleanupBeforeSeverity={foreignCleanupResult.BeforePartialSeverity:F3} foreignCleanupAfterPartial={foreignCleanupResult.AfterPartialConflicts} foreignCleanupAfterSeverity={foreignCleanupResult.AfterPartialSeverity:F3} axisSeparationIterations={axisSeparationResult.Iterations} axisSeparationMoved={axisSeparationResult.MovedMarks} axisSeparationBeforeOverlaps={axisSeparationResult.BeforeOverlaps} axisSeparationAfterOverlaps={axisSeparationResult.AfterOverlaps} markSeparationIterations={markSeparationResult.Iterations} collidingMarks={collidingIds.Count} markSeparationStopReason={markSeparationResult.StopReason} markSeparationEarlyExit={markSeparationResult.StopReason == ForceRelaxStopReason.OverlapsCleared} finalForeignCleanupIterations={finalForeignCleanupResult.Iterations} finalForeignCleanupMoved={finalForeignCleanupResult.MovedMarks} finalForeignCleanupBeforePartial={finalForeignCleanupResult.BeforePartialConflicts} finalForeignCleanupBeforeSeverity={finalForeignCleanupResult.BeforePartialSeverity:F3} finalForeignCleanupAfterPartial={finalForeignCleanupResult.AfterPartialConflicts} finalForeignCleanupAfterSeverity={finalForeignCleanupResult.AfterPartialSeverity:F3} foreignThreshold={foreignPartThreshold:F3} leaderTextThreshold={leaderTextThreshold:F3} leaderTextInitialCrossings={leaderTextInitial.TotalCrossings} leaderTextInitialOwn={leaderTextInitial.OwnCrossings} leaderTextInitialForeign={leaderTextInitial.ForeignCrossings} leaderTextInitialSeverity={leaderTextInitial.Severity:F3} leaderTextFinalCrossings={leaderTextFinal.TotalCrossings} leaderTextFinalOwn={leaderTextFinal.OwnCrossings} leaderTextFinalForeign={leaderTextFinal.ForeignCrossings} leaderTextFinalSeverity={leaderTextFinal.Severity:F3} foreignInitialConflicts={foreignInitial.Conflicts} foreignInitialSeverity={foreignInitial.Severity:F3} foreignInitialInsideConflicts={foreignInitial.MarkInsideConflicts} foreignInitialInsideSeverity={foreignInitial.MarkInsideSeverity:F3} foreignInitialPartialConflicts={foreignInitial.PartialConflicts} foreignInitialPartialSeverity={foreignInitial.PartialSeverity:F3} foreignInitialPartInsideConflicts={foreignInitial.PartInsideConflicts} foreignInitialPartInsideSeverity={foreignInitial.PartInsideSeverity:F3} foreignAfterEquilibriumConflicts={foreignAfterEquilibrium.Conflicts} foreignAfterEquilibriumSeverity={foreignAfterEquilibrium.Severity:F3} foreignAfterEquilibriumInsideConflicts={foreignAfterEquilibrium.MarkInsideConflicts} foreignAfterEquilibriumInsideSeverity={foreignAfterEquilibrium.MarkInsideSeverity:F3} foreignAfterEquilibriumPartialConflicts={foreignAfterEquilibrium.PartialConflicts} foreignAfterEquilibriumPartialSeverity={foreignAfterEquilibrium.PartialSeverity:F3} foreignAfterEquilibriumPartInsideConflicts={foreignAfterEquilibrium.PartInsideConflicts} foreignAfterEquilibriumPartInsideSeverity={foreignAfterEquilibrium.PartInsideSeverity:F3} foreignAfterCleanupConflicts={foreignAfterCleanup.Conflicts} foreignAfterCleanupSeverity={foreignAfterCleanup.Severity:F3} foreignAfterCleanupInsideConflicts={foreignAfterCleanup.MarkInsideConflicts} foreignAfterCleanupInsideSeverity={foreignAfterCleanup.MarkInsideSeverity:F3} foreignAfterCleanupPartialConflicts={foreignAfterCleanup.PartialConflicts} foreignAfterCleanupPartialSeverity={foreignAfterCleanup.PartialSeverity:F3} foreignAfterCleanupPartInsideConflicts={foreignAfterCleanup.PartInsideConflicts} foreignAfterCleanupPartInsideSeverity={foreignAfterCleanup.PartInsideSeverity:F3} foreignAfterMarkSeparationConflicts={foreignAfterMarkSeparation.Conflicts} foreignAfterMarkSeparationSeverity={foreignAfterMarkSeparation.Severity:F3} foreignAfterMarkSeparationInsideConflicts={foreignAfterMarkSeparation.MarkInsideConflicts} foreignAfterMarkSeparationInsideSeverity={foreignAfterMarkSeparation.MarkInsideSeverity:F3} foreignAfterMarkSeparationPartialConflicts={foreignAfterMarkSeparation.PartialConflicts} foreignAfterMarkSeparationPartialSeverity={foreignAfterMarkSeparation.PartialSeverity:F3} foreignAfterMarkSeparationPartInsideConflicts={foreignAfterMarkSeparation.PartInsideConflicts} foreignAfterMarkSeparationPartInsideSeverity={foreignAfterMarkSeparation.PartInsideSeverity:F3} foreignFinalConflicts={foreignFinal.Conflicts} foreignFinalSeverity={foreignFinal.Severity:F3} foreignFinalInsideConflicts={foreignFinal.MarkInsideConflicts} foreignFinalInsideSeverity={foreignFinal.MarkInsideSeverity:F3} foreignFinalPartialConflicts={foreignFinal.PartialConflicts} foreignFinalPartialSeverity={foreignFinal.PartialSeverity:F3} foreignFinalPartInsideConflicts={foreignFinal.PartInsideConflicts} foreignFinalPartInsideSeverity={foreignFinal.PartInsideSeverity:F3}");
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

    private static void WriteForeignPartOverlapTrace(
        int viewId,
        string stage,
        ForeignPartOverlapSummary summary)
    {
        if (!PerfTrace.IsActive)
            return;

        foreach (var overlap in summary.Overlaps)
        {
            PerfTrace.Write(
                "api-mark",
                "arrange_marks_force_foreign_part",
                0,
                $"viewId={viewId} stage={stage} markId={overlap.MarkId} partModelId={overlap.PartModelId} kind={overlap.Kind} depth={overlap.Depth:F3}");
        }
    }

    private static void WriteLeaderTextOverlapTrace(
        int viewId,
        string stage,
        LeaderTextOverlapSummary summary)
    {
        if (!PerfTrace.IsActive)
            return;

        foreach (var conflict in summary.Conflicts)
        {
            PerfTrace.Write(
                "api-mark",
                "arrange_marks_force_leader_text",
                0,
                $"viewId={viewId} stage={stage} markId={conflict.MarkId} crossedMarkId={conflict.CrossedMarkId} own={conflict.IsOwn} segmentIndex={conflict.SegmentIndex} severity={conflict.Severity:F3}");
        }
    }

    private static List<LeaderTextOverlapMark> BuildLeaderTextOverlapMarks(
        IReadOnlyList<TeklaDrawingMarkLayoutEntry> entries)
    {
        var result = new List<LeaderTextOverlapMark>();
        foreach (var entry in entries)
        {
            var textPolygon = entry.MarkContext.Geometry.Corners
                .Select(static corner => new[] { corner.X, corner.Y })
                .ToList();
            if (textPolygon.Count < 3)
                continue;

            result.Add(new LeaderTextOverlapMark
            {
                MarkId = entry.Mark.GetIdentifier().ID,
                TextPolygon = textPolygon,
                LeaderPolyline = BuildPrimaryLeaderPolyline(entry.MarkContext.LeaderSnapshot)
            });
        }

        return result;
    }

    private static List<double[]> BuildPrimaryLeaderPolyline(LeaderSnapshot? snapshot)
    {
        var result = new List<double[]>();
        var leaderLine = snapshot?.LeaderLines
            .FirstOrDefault(static line => string.Equals(line.Type, "NormalLeaderLine", StringComparison.Ordinal))
            ?? snapshot?.LeaderLines.FirstOrDefault();

        if (leaderLine?.StartPoint == null || leaderLine.EndPoint == null)
            return result;

        result.Add([leaderLine.StartPoint.X, leaderLine.StartPoint.Y]);
        result.AddRange(leaderLine.ElbowPoints
            .OrderBy(static point => point.Order)
            .Select(static point => new[] { point.X, point.Y }));
        result.Add([leaderLine.EndPoint.X, leaderLine.EndPoint.Y]);
        return result;
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

    private static List<MarkLayoutPlacement> BuildPlacementsFromEntries(
        IReadOnlyList<TeklaDrawingMarkLayoutEntry> markEntries)
    {
        return markEntries
            .Select(entry => new MarkLayoutPlacement
            {
                Id = entry.Mark.GetIdentifier().ID,
                X = entry.CenterX,
                Y = entry.CenterY,
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
