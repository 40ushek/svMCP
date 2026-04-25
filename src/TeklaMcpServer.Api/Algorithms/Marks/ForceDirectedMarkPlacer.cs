using System;
using System.Collections.Generic;
using System.Linq;
using TeklaMcpServer.Api.Algorithms.Geometry;

namespace TeklaMcpServer.Api.Algorithms.Marks;

internal readonly struct PartBbox
{
    public PartBbox(int modelId, double minX, double minY, double maxX, double maxY, IReadOnlyList<double[]>? polygon = null)
    {
        ModelId = modelId;
        MinX = minX;
        MinY = minY;
        MaxX = maxX;
        MaxY = maxY;
        Polygon = polygon;
    }

    public int ModelId { get; }
    public double MinX { get; }
    public double MinY { get; }
    public double MaxX { get; }
    public double MaxY { get; }
    public IReadOnlyList<double[]>? Polygon { get; }
}

internal readonly struct ForcePassOptions
{
    private const double PaperIdealDistMm = 4.0;
    private const double PaperMarkGapMm = 2.0;
    private const double PaperPartRepelRadiusMm = 8.0;
    private const double PaperPartRepelSofteningMm = 0.75;
    private const double PaperStopEpsilonMm = 0.25;

    public ForcePassOptions(
        double kAttract, double idealDist,
        double kFarAttract, double maxAttract,
        double kReturnToAxisLine,
        double kPerpRestoreAxis,
        double kRepelPart, double partRepelRadius, double partRepelSoftening,
        double kRepelMark, double markGapMm,
        double initialDt, double dtDecay, double stopEpsilon, int maxIterations)
    {
        KAttract = kAttract;
        IdealDist = idealDist;
        KFarAttract = kFarAttract;
        MaxAttract = maxAttract;
        KReturnToAxisLine = kReturnToAxisLine;
        KPerpRestoreAxis = kPerpRestoreAxis;
        KRepelPart = kRepelPart;
        PartRepelRadius = partRepelRadius;
        PartRepelSoftening = partRepelSoftening;
        KRepelMark = kRepelMark;
        MarkGapMm = markGapMm;
        InitialDt = initialDt;
        DtDecay = dtDecay;
        StopEpsilon = stopEpsilon;
        MaxIterations = maxIterations;
    }

    public double KAttract { get; }
    /// <summary>Desired distance from mark center to own part surface. Attraction pulls to this distance, not to zero.</summary>
    public double IdealDist { get; }
    public double KFarAttract { get; }
    public double MaxAttract { get; }
    public double KReturnToAxisLine { get; }
    public double KPerpRestoreAxis { get; }
    public double KRepelPart { get; }
    public double PartRepelRadius { get; }
    /// <summary>Softening epsilon for repulsion: force = KRepelPart / (dist² + ε²). Prevents singularity at dist→0.</summary>
    public double PartRepelSoftening { get; }
    public double KRepelMark { get; }
    public double MarkGapMm { get; }
    public double InitialDt { get; }
    public double DtDecay { get; }
    public double StopEpsilon { get; }
    public int MaxIterations { get; }

    // IdealDist=25, KRepel=300, KAttract=0.48 → sqrt(300/0.48)=25 ✓
    public static ForcePassOptions EquilibriumDefault { get; } = new ForcePassOptions(
        kAttract: 0.48, idealDist: 25.0, kFarAttract: 0.05, maxAttract: 50.0,
        kReturnToAxisLine: 0.0, kPerpRestoreAxis: 0.12,
        kRepelPart: 300.0, partRepelRadius: 120.0, partRepelSoftening: 5.0,
        kRepelMark: 0.0, markGapMm: 2.0,
        initialDt: 1.0, dtDecay: 0.98, stopEpsilon: 0.05, maxIterations: 100);

    public static ForcePassOptions MarkSeparationDefault { get; } = new ForcePassOptions(
        kAttract: 0.48, idealDist: 25.0, kFarAttract: 0.05, maxAttract: 50.0,
        kReturnToAxisLine: 0.12, kPerpRestoreAxis: 0.0,
        kRepelPart: 300.0, partRepelRadius: 120.0, partRepelSoftening: 5.0,
        kRepelMark: 1.0, markGapMm: 2.0,
        initialDt: 1.0, dtDecay: 0.98, stopEpsilon: 0.05, maxIterations: 100);

    public static ForcePassOptions CreateEquilibriumForViewScale(double viewScale) =>
        CreateScaledDistancePolicy(EquilibriumDefault, NormalizeViewScale(viewScale));

    public static ForcePassOptions CreateMarkSeparationForViewScale(double viewScale) =>
        CreateScaledDistancePolicy(MarkSeparationDefault, NormalizeViewScale(viewScale));

    private static ForcePassOptions CreateScaledDistancePolicy(ForcePassOptions defaults, double scale) =>
        new ForcePassOptions(
            kAttract: defaults.KAttract,
            idealDist: PaperIdealDistMm * scale,
            kFarAttract: defaults.KFarAttract,
            maxAttract: defaults.MaxAttract,
            kReturnToAxisLine: defaults.KReturnToAxisLine,
            kPerpRestoreAxis: defaults.KPerpRestoreAxis,
            kRepelPart: defaults.KRepelPart,
            partRepelRadius: PaperPartRepelRadiusMm * scale,
            partRepelSoftening: PaperPartRepelSofteningMm * scale,
            kRepelMark: defaults.KRepelMark,
            markGapMm: PaperMarkGapMm * scale,
            initialDt: defaults.InitialDt,
            dtDecay: defaults.DtDecay,
            stopEpsilon: PaperStopEpsilonMm * scale,
            maxIterations: defaults.MaxIterations);

    private static double NormalizeViewScale(double viewScale) => viewScale > 0.0 ? viewScale : 1.0;
}

internal enum ForceRelaxStopReason
{
    NotRun,
    MaxIterations,
    StopEpsilon,
    OverlapsCleared
}

internal readonly struct ForceRelaxResult
{
    public ForceRelaxResult(int iterations, ForceRelaxStopReason stopReason)
    {
        Iterations = iterations;
        StopReason = stopReason;
    }

    public int Iterations { get; }
    public ForceRelaxStopReason StopReason { get; }
}

internal readonly struct ForeignPartCleanupResult
{
    public ForeignPartCleanupResult(
        int iterations,
        int movedMarks,
        int beforePartialConflicts,
        double beforePartialSeverity,
        int afterPartialConflicts,
        double afterPartialSeverity)
    {
        Iterations = iterations;
        MovedMarks = movedMarks;
        BeforePartialConflicts = beforePartialConflicts;
        BeforePartialSeverity = beforePartialSeverity;
        AfterPartialConflicts = afterPartialConflicts;
        AfterPartialSeverity = afterPartialSeverity;
    }

    public int Iterations { get; }
    public int MovedMarks { get; }
    public int BeforePartialConflicts { get; }
    public double BeforePartialSeverity { get; }
    public int AfterPartialConflicts { get; }
    public double AfterPartialSeverity { get; }
}

internal sealed class ForceDirectedMarkPlacer
{
    private const double FarDistanceFactor = 8.0;
    private const double OutlierDistanceFactor = 15.0;
    private const double OutlierTargetFactor = 6.0;

    public void PlaceInitial(IReadOnlyList<ForceDirectedMarkItem> items, IReadOnlyList<PartBbox> allParts)
    {
        foreach (var mark in items)
        {
            if (!mark.CanMove || mark.ConstrainToAxis || mark.OwnPolygon == null || mark.OwnPolygon.Count < 2)
                continue;

            if (!LeaderAnchorResolver.TryFindNearestEdgeHit(mark.OwnPolygon, mark.Cx, mark.Cy, out var hit))
                continue;

            var dx = mark.Cx - hit.PointX;
            var dy = mark.Cy - hit.PointY;
            var dist = Math.Sqrt((dx * dx) + (dy * dy));
            if (dist <= 0.001)
                continue;

            var markSize = Math.Max(Math.Min(mark.Width, mark.Height), 1.0);
            var outlierThreshold = markSize * OutlierDistanceFactor;
            if (dist <= outlierThreshold)
                continue;

            var targetDist = markSize * OutlierTargetFactor;
            var ux = dx / dist;
            var uy = dy / dist;
            var targetX = hit.PointX + (ux * targetDist);
            var targetY = hit.PointY + (uy * targetDist);
            if (WouldOverlapForeignPart(mark, targetX, targetY, allParts))
                continue;

            if (WouldOverlapOtherMark(mark, items, targetX, targetY))
                continue;

            mark.Cx = targetX;
            mark.Cy = targetY;
        }
    }

    public ForceRelaxResult Relax(
        IReadOnlyList<ForceDirectedMarkItem> items,
        IReadOnlyList<PartBbox> allParts,
        ForcePassOptions options,
        bool includeMarkRepulsion = false,
        ISet<int>? movableIds = null,
        Action<ForceIterationDebugInfo>? debugSink = null,
        Func<int>? getRemainingOverlapCount = null,
        int overlapCheckInterval = 5,
        bool preferAxisStepForReturnToAxisMarks = false,
        double foreignPartThreshold = 0.0)
    {
        var dt = options.InitialDt;
        var iterationsUsed = 0;
        var prevStepById = new Dictionary<int, (double dx, double dy)>();
        for (var iter = 0; iter < options.MaxIterations; iter++)
        {
            iterationsUsed = iter + 1;
            var maxDisplacement = 0.0;
            var updates = new List<ForceIterationUpdate>(items.Count);

            foreach (var mark in items)
            {
                if (!mark.CanMove) continue;
                if (movableIds != null && !movableIds.Contains(mark.Id)) continue;

                var debug = ComputeForce(mark, items, allParts, options, includeMarkRepulsion);

                var dx = debug.Fx * dt;
                var dy = debug.Fy * dt;
                if (prevStepById.TryGetValue(mark.Id, out var prevStep))
                {
                    var dot = (dx * prevStep.dx) + (dy * prevStep.dy);
                    if (dot < 0.0)
                    {
                        dx *= 0.5;
                        dy *= 0.5;
                    }
                }

                var axisStepMode = false;
                var axisStepReason = "notRequested";
                if (preferAxisStepForReturnToAxisMarks &&
                    includeMarkRepulsion &&
                    mark.ReturnToAxisLine)
                {
                    if (TryPreferAxisStep(mark, items, allParts, foreignPartThreshold, dx, dy, out var axisDx, out var axisDy, out axisStepReason))
                    {
                        dx = axisDx;
                        dy = axisDy;
                        axisStepMode = true;
                    }
                }
                else if (preferAxisStepForReturnToAxisMarks && includeMarkRepulsion)
                {
                    axisStepReason = mark.ReturnToAxisLine ? "notReturnToAxisLine" : "notAxisReturnMark";
                }

                updates.Add(new ForceIterationUpdate(mark, debug, dx, dy, axisStepMode, axisStepReason));
            }

            foreach (var update in updates)
            {
                update.Mark.Cx += update.Dx;
                update.Mark.Cy += update.Dy;
                prevStepById[update.Mark.Id] = (update.Dx, update.Dy);
                var displacement = Math.Sqrt((update.Dx * update.Dx) + (update.Dy * update.Dy));
                maxDisplacement = Math.Max(maxDisplacement, displacement);
                debugSink?.Invoke(new ForceIterationDebugInfo(
                    iter + 1,
                    update.Mark.Id,
                    update.Debug.AttractFx,
                    update.Debug.AttractFy,
                    update.Debug.PartRepelFx,
                    update.Debug.PartRepelFy,
                    update.Debug.MarkRepelFx,
                    update.Debug.MarkRepelFy,
                    update.Debug.Fx,
                    update.Debug.Fy,
                    update.Dx,
                    update.Dy,
                    update.Mark.Cx,
                    update.Mark.Cy,
                    update.AxisStepMode,
                    update.AxisStepReason));
            }

            if (getRemainingOverlapCount != null &&
                overlapCheckInterval > 0 &&
                (iter + 1) % overlapCheckInterval == 0 &&
                getRemainingOverlapCount() == 0)
            {
                return new ForceRelaxResult(iterationsUsed, ForceRelaxStopReason.OverlapsCleared);
            }

            dt *= options.DtDecay;
            if (maxDisplacement < options.StopEpsilon)
                return new ForceRelaxResult(iterationsUsed, ForceRelaxStopReason.StopEpsilon);
        }

        return new ForceRelaxResult(iterationsUsed, ForceRelaxStopReason.MaxIterations);
    }

    private static bool TryPreferAxisStep(
        ForceDirectedMarkItem mark,
        IReadOnlyList<ForceDirectedMarkItem> items,
        IReadOnlyList<PartBbox> allParts,
        double foreignPartThreshold,
        double fullDx,
        double fullDy,
        out double axisStepX,
        out double axisStepY,
        out string reason)
    {
        axisStepX = 0.0;
        axisStepY = 0.0;
        reason = "unknown";

        if (!TryGetNormalizedAxis(mark, out var axisDx, out var axisDy))
        {
            reason = "noAxis";
            return false;
        }

        var projection = (fullDx * axisDx) + (fullDy * axisDy);
        axisStepX = axisDx * projection;
        axisStepY = axisDy * projection;
        if (Math.Sqrt((axisStepX * axisStepX) + (axisStepY * axisStepY)) <= 0.001)
        {
            reason = "zeroProjection";
            return false;
        }

        var currentMarkSeverity = GetMarkOverlapSeverityForMark(items, mark.Id);
        if (currentMarkSeverity <= 0.001)
        {
            reason = "noCurrentMarkOverlap";
            return false;
        }

        var currentForeignSeverity = GetPartialSeverityForMark(
            ForeignPartOverlapAnalyzer.Analyze(items, allParts, foreignPartThreshold),
            mark.Id);

        var originalX = mark.Cx;
        var originalY = mark.Cy;
        mark.Cx = originalX + axisStepX;
        mark.Cy = originalY + axisStepY;
        try
        {
            var proposedMarkSeverity = GetMarkOverlapSeverityForMark(items, mark.Id);
            if (proposedMarkSeverity >= currentMarkSeverity - 0.001)
            {
                reason = "markSeverityNotImproved";
                return false;
            }

            var proposedForeignSeverity = GetPartialSeverityForMark(
                ForeignPartOverlapAnalyzer.Analyze(items, allParts, foreignPartThreshold),
                mark.Id);
            if (proposedForeignSeverity > currentForeignSeverity + 0.001)
            {
                reason = "foreignSeverityWorse";
                return false;
            }

            reason = "accepted";
            return true;
        }
        finally
        {
            mark.Cx = originalX;
            mark.Cy = originalY;
        }
    }

    public ForeignPartCleanupResult CleanupForeignPartOverlaps(
        IReadOnlyList<ForceDirectedMarkItem> items,
        IReadOnlyList<PartBbox> allParts,
        double threshold,
        double maxStep,
        int maxStepsPerMark = 25,
        Action<string>? trace = null)
    {
        var before = ForeignPartOverlapAnalyzer.Analyze(items, allParts, threshold);
        var beforePartialConflicts = before.PartialConflicts;
        var beforePartialSeverity = before.PartialSeverity;
        var movedIds = new HashSet<int>();
        var iterationsUsed = 0;
        var safeMaxStep = Math.Max(maxStep, 0.0);
        var originalPositions = items.ToDictionary(
            static item => item.Id,
            static item => (item.Cx, item.Cy));

        var maxAcceptedStepsPerMark = Math.Max(maxStepsPerMark, 0);
        const int maxConsecutiveNeutralSteps = 10;
        if (maxAcceptedStepsPerMark > 0)
        {
            var initialCandidates = before.Overlaps
                .Where(static overlap => overlap.Kind == ForeignPartOverlapKind.PartialForeignPartOverlap)
                .GroupBy(static overlap => overlap.MarkId)
                .OrderByDescending(static group => group.Sum(overlap => overlap.Depth))
                .Select(static group => group.Key)
                .ToList();

            foreach (var markId in initialCandidates)
            {
                var mark = items.FirstOrDefault(item => item.Id == markId);
                if (mark == null || !mark.CanMove)
                {
                    trace?.Invoke($"markId={markId} event=skip reason=not_movable_or_missing");
                    continue;
                }

                var originalMarkX = mark.Cx;
                var originalMarkY = mark.Cy;
                var originalMarkSummary = ForeignPartOverlapAnalyzer.Analyze(items, allParts, threshold);
                var originalMarkSeverity = GetPartialSeverityForMark(originalMarkSummary, mark.Id);
                trace?.Invoke(
                    $"markId={mark.Id} event=mark_start severity={originalMarkSeverity:F3} pos=({mark.Cx:F3},{mark.Cy:F3}) overlaps={FormatPartialOverlapsForMark(originalMarkSummary, mark.Id)}");
                var acceptedStepsForMark = 0;
                var consecutiveNeutralSteps = 0;

                while (acceptedStepsForMark < maxAcceptedStepsPerMark)
                {
                    var current = ForeignPartOverlapAnalyzer.Analyze(items, allParts, threshold);
                    var currentSeverity = GetPartialSeverityForMark(current, mark.Id);
                    if (currentSeverity <= 0.0)
                    {
                        trace?.Invoke($"markId={mark.Id} event=mark_done reason=no_partial_conflict step={acceptedStepsForMark} severity={currentSeverity:F3}");
                        break;
                    }

                    var overlaps = current.Overlaps
                        .Where(overlap =>
                            overlap.MarkId == mark.Id &&
                            overlap.Kind == ForeignPartOverlapKind.PartialForeignPartOverlap)
                        .ToList();
                    if (overlaps.Count == 0)
                    {
                        trace?.Invoke($"markId={mark.Id} event=mark_done reason=no_partial_overlaps step={acceptedStepsForMark} severity={currentSeverity:F3}");
                        break;
                    }

                    var moveX = 0.0;
                    var moveY = 0.0;
                    foreach (var overlap in overlaps)
                    {
                        moveX += -overlap.AxisX * overlap.Depth;
                        moveY += -overlap.AxisY * overlap.Depth;
                    }

                    var fullDx = 0.0;
                    var fullDy = 0.0;
                    if (!TryBuildForeignPartCleanupDelta(moveX, moveY, safeMaxStep, out fullDx, out fullDy))
                    {
                        trace?.Invoke(
                            $"markId={mark.Id} event=mark_done reason=zero_mtv step={acceptedStepsForMark} severity={currentSeverity:F3} move=({moveX:F3},{moveY:F3}) overlaps={FormatPartialOverlapsForMark(current, mark.Id)}");
                        break;
                    }

                    trace?.Invoke(
                        $"markId={mark.Id} event=step_start step={acceptedStepsForMark + 1} severity={currentSeverity:F3} neutralStreak={consecutiveNeutralSteps} move=({moveX:F3},{moveY:F3}) fullStep=({fullDx:F3},{fullDy:F3}) overlaps={FormatPartialOverlapsForMark(current, mark.Id)}");
                    var moved = false;
                    if (mark.ConstrainToAxis && TryGetNormalizedAxis(mark, out var axisDx, out var axisDy))
                    {
                        var projection = (moveX * axisDx) + (moveY * axisDy);
                        var axisMoveX = axisDx * projection;
                        var axisMoveY = axisDy * projection;
                        if (TryBuildForeignPartCleanupDelta(axisMoveX, axisMoveY, safeMaxStep, out var axisStepX, out var axisStepY))
                        {
                            moved = TryApplyForeignPartCleanupStep(
                                mark,
                                items,
                                allParts,
                                threshold,
                                axisStepX,
                                axisStepY,
                                mode: "axis",
                                trace: trace);
                        }
                        else
                        {
                            trace?.Invoke(
                                $"markId={mark.Id} event=step_reject mode=axis reason=zero_projected_step step={acceptedStepsForMark + 1} axis=({axisDx:F3},{axisDy:F3}) projection={projection:F3}");
                        }
                    }

                    if (!moved)
                    {
                        // Foreign-part cleanup may need a small off-axis nudge when the baseline axis is parallel
                        // to the conflict and the projected step cannot reduce the overlap.
                        moved = TryApplyForeignPartCleanupStep(
                            mark,
                            items,
                            allParts,
                            threshold,
                            fullDx,
                            fullDy,
                            allowEqualSeverity: true,
                            mode: "full",
                            trace: trace);
                    }

                    if (!moved)
                    {
                        trace?.Invoke($"markId={mark.Id} event=mark_done reason=no_accepted_step step={acceptedStepsForMark + 1} severity={currentSeverity:F3}");
                        break;
                    }

                    var afterStepSeverity = GetPartialSeverityForMark(
                        ForeignPartOverlapAnalyzer.Analyze(items, allParts, threshold),
                        mark.Id);
                    if (afterStepSeverity < currentSeverity - 0.001)
                    {
                        consecutiveNeutralSteps = 0;
                    }
                    else
                    {
                        consecutiveNeutralSteps++;
                    }

                    movedIds.Add(mark.Id);
                    iterationsUsed++;
                    acceptedStepsForMark++;

                    if (consecutiveNeutralSteps > maxConsecutiveNeutralSteps)
                    {
                        trace?.Invoke(
                            $"markId={mark.Id} event=mark_done reason=neutral_limit step={acceptedStepsForMark} severity={afterStepSeverity:F3} neutralStreak={consecutiveNeutralSteps}");
                        break;
                    }
                }

                if (acceptedStepsForMark > 0)
                {
                    var finalMarkSeverity = GetPartialSeverityForMark(
                        ForeignPartOverlapAnalyzer.Analyze(items, allParts, threshold),
                        mark.Id);
                    if (finalMarkSeverity >= originalMarkSeverity - 0.001)
                    {
                        trace?.Invoke(
                            $"markId={mark.Id} event=mark_rollback reason=no_final_improvement steps={acceptedStepsForMark} beforeSeverity={originalMarkSeverity:F3} afterSeverity={finalMarkSeverity:F3}");
                        mark.Cx = originalMarkX;
                        mark.Cy = originalMarkY;
                        movedIds.Remove(mark.Id);
                        iterationsUsed -= acceptedStepsForMark;
                    }
                    else
                    {
                        trace?.Invoke(
                            $"markId={mark.Id} event=mark_accept steps={acceptedStepsForMark} beforeSeverity={originalMarkSeverity:F3} afterSeverity={finalMarkSeverity:F3} pos=({mark.Cx:F3},{mark.Cy:F3})");
                    }
                }
            }
        }

        var after = ForeignPartOverlapAnalyzer.Analyze(items, allParts, threshold);
        if (movedIds.Count > 0 && after.PartialSeverity >= beforePartialSeverity - 0.001)
        {
            trace?.Invoke(
                $"event=cleanup_rollback reason=no_total_improvement beforeSeverity={beforePartialSeverity:F3} afterSeverity={after.PartialSeverity:F3} moved={movedIds.Count}");
            foreach (var item in items)
            {
                if (!originalPositions.TryGetValue(item.Id, out var original))
                    continue;

                item.Cx = original.Cx;
                item.Cy = original.Cy;
            }

            after = ForeignPartOverlapAnalyzer.Analyze(items, allParts, threshold);
            movedIds.Clear();
            iterationsUsed = 0;
        }

        return new ForeignPartCleanupResult(
            iterationsUsed,
            movedIds.Count,
            beforePartialConflicts,
            beforePartialSeverity,
            after.PartialConflicts,
            after.PartialSeverity);
    }

    private static bool TryBuildForeignPartCleanupDelta(
        double moveX,
        double moveY,
        double maxStep,
        out double dx,
        out double dy)
    {
        dx = 0.0;
        dy = 0.0;

        var length = Math.Sqrt((moveX * moveX) + (moveY * moveY));
        if (length <= 0.001)
            return false;

        var step = maxStep > 0.0 && length > maxStep
            ? maxStep / length
            : 1.0;

        dx = moveX * step;
        dy = moveY * step;
        return true;
    }

    private static bool TryApplyForeignPartCleanupStep(
        ForceDirectedMarkItem mark,
        IReadOnlyList<ForceDirectedMarkItem> items,
        IReadOnlyList<PartBbox> allParts,
        double threshold,
        double dx,
        double dy,
        bool allowEqualSeverity = false,
        string mode = "step",
        Action<string>? trace = null)
    {
        var originalX = mark.Cx;
        var originalY = mark.Cy;
        var current = ForeignPartOverlapAnalyzer.Analyze(items, allParts, threshold);
        var currentPartialSeverity = GetPartialSeverityForMark(current, mark.Id);
        var currentMarkOverlapPairs = CountMarkOverlapPairs(items);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            mark.Cx = originalX + dx;
            mark.Cy = originalY + dy;

            var proposed = ForeignPartOverlapAnalyzer.Analyze(items, allParts, threshold);
            var proposedMarkOverlapPairs = CountMarkOverlapPairs(items);
            var proposedPartialSeverity = GetPartialSeverityForMark(proposed, mark.Id);
            var improvesSeverity = proposedPartialSeverity < currentPartialSeverity - 0.001;
            var keepsSeverity = allowEqualSeverity && proposedPartialSeverity <= currentPartialSeverity + 0.001;
            if ((improvesSeverity || keepsSeverity) &&
                proposedMarkOverlapPairs <= currentMarkOverlapPairs)
            {
                trace?.Invoke(
                    $"markId={mark.Id} event=step_accept mode={mode} attempt={attempt + 1} dx={dx:F3} dy={dy:F3} beforeSeverity={currentPartialSeverity:F3} afterSeverity={proposedPartialSeverity:F3} beforePairs={currentMarkOverlapPairs} afterPairs={proposedMarkOverlapPairs} allowEqual={allowEqualSeverity}");
                return true;
            }

            var reason = proposedMarkOverlapPairs > currentMarkOverlapPairs
                ? "mark_overlap_increased"
                : allowEqualSeverity
                    ? "severity_increased"
                    : "severity_not_improved";
            trace?.Invoke(
                $"markId={mark.Id} event=step_reject mode={mode} reason={reason} attempt={attempt + 1} dx={dx:F3} dy={dy:F3} beforeSeverity={currentPartialSeverity:F3} afterSeverity={proposedPartialSeverity:F3} beforePairs={currentMarkOverlapPairs} afterPairs={proposedMarkOverlapPairs} allowEqual={allowEqualSeverity}");
            mark.Cx = originalX;
            mark.Cy = originalY;
            dx *= 0.5;
            dy *= 0.5;
            if (Math.Sqrt((dx * dx) + (dy * dy)) <= 0.001)
            {
                trace?.Invoke($"markId={mark.Id} event=step_done mode={mode} reason=step_too_small attempt={attempt + 1}");
                break;
            }
        }

        mark.Cx = originalX;
        mark.Cy = originalY;
        return false;
    }

    private static string FormatPartialOverlapsForMark(ForeignPartOverlapSummary summary, int markId)
    {
        var overlaps = summary.Overlaps
            .Where(overlap =>
                overlap.MarkId == markId &&
                overlap.Kind == ForeignPartOverlapKind.PartialForeignPartOverlap)
            .OrderByDescending(static overlap => overlap.Depth)
            .Take(6)
            .Select(static overlap => $"{overlap.PartModelId}:{overlap.Depth:F3}");

        return string.Join(",", overlaps);
    }

    private static double GetPartialSeverityForMark(ForeignPartOverlapSummary summary, int markId)
        => summary.Overlaps
            .Where(overlap =>
                overlap.MarkId == markId &&
                overlap.Kind == ForeignPartOverlapKind.PartialForeignPartOverlap)
            .Sum(static overlap => overlap.Depth);

    private static ForceComponents ComputeForce(
        ForceDirectedMarkItem mark,
        IReadOnlyList<ForceDirectedMarkItem> allMarks,
        IReadOnlyList<PartBbox> allParts,
        ForcePassOptions options,
        bool includeMarkRepulsion)
    {
        var attractFx = 0.0;
        var attractFy = 0.0;
        var partRepelFx = 0.0;
        var partRepelFy = 0.0;
        var markRepelFx = 0.0;
        var markRepelFy = 0.0;
        var hasAxis = TryGetNormalizedAxis(mark, out var axisDx, out var axisDy);

        if (mark.OwnPolygon != null && mark.OwnPolygon.Count >= 2)
        {
            if (mark.ConstrainToAxis && hasAxis)
            {
                // Axis-constrained marks: leash along axis.
                // No force while mark projection is within [polyMin, polyMax].
                // Outside: Hooke spring pulls mark back to the nearest bound.
                var markAxPos = mark.Cx * axisDx + mark.Cy * axisDy;
                var polyMin = double.MaxValue;
                var polyMax = double.MinValue;
                foreach (var pt in mark.OwnPolygon)
                {
                    var proj = pt[0] * axisDx + pt[1] * axisDy;
                    if (proj < polyMin) polyMin = proj;
                    if (proj > polyMax) polyMax = proj;
                }

                double axisForce;
                if (markAxPos < polyMin)
                    axisForce = options.KAttract * (polyMin - markAxPos);
                else if (markAxPos > polyMax)
                    axisForce = options.KAttract * (polyMax - markAxPos);
                else
                    axisForce = 0.0;

                attractFx += axisForce * axisDx;
                attractFy += axisForce * axisDy;
            }
            else
            {
                // Leader-line and free marks: piecewise spring to nearest part surface.
                // Near field uses logarithmic spring; far field adds a linear tail so very distant marks return faster.
                if (LeaderAnchorResolver.TryFindNearestEdgeHit(mark.OwnPolygon, mark.Cx, mark.Cy, out var hit))
                {
                    var dx = hit.PointX - mark.Cx;
                    var dy = hit.PointY - mark.Cy;
                    var dist = Math.Max(Math.Sqrt((dx * dx) + (dy * dy)), 0.001);
                    var markSize = Math.Max(Math.Min(mark.Width, mark.Height), 1.0);
                    var farThreshold = markSize * FarDistanceFactor;
                    var springF = ComputeAttractForce(dist, farThreshold, options);
                    if (IsInsidePolygon(mark.Cx, mark.Cy, mark.OwnPolygon))
                        springF = -springF;
                    attractFx += springF * (dx / dist);
                    attractFy += springF * (dy / dist);
                }
            }
        }

        // Repulsion from foreign part bboxes.
        foreach (var part in allParts)
        {
            if (mark.OwnModelId.HasValue && part.ModelId == mark.OwnModelId.Value)
                continue;

            var isInsidePart = part.Polygon != null &&
                               part.Polygon.Count >= 3 &&
                               IsInsidePolygon(mark.Cx, mark.Cy, part.Polygon);
            var (nx, ny) = NearestOnShape(mark.Cx, mark.Cy, part);
            var dx = mark.Cx - nx;
            var dy = mark.Cy - ny;
            var dist = Math.Sqrt((dx * dx) + (dy * dy));

            if (isInsidePart)
            {
                var insideForce = options.KRepelPart / (options.PartRepelSoftening * options.PartRepelSoftening);
                var exitDx = nx - mark.Cx;
                var exitDy = ny - mark.Cy;
                var exitDist = Math.Max(Math.Sqrt(exitDx * exitDx + exitDy * exitDy), 0.001);
                partRepelFx += insideForce * exitDx / exitDist;
                partRepelFy += insideForce * exitDy / exitDist;
                continue;
            }

            if (dist < 0.001)
            {
                // Degenerate fallback — keep previous centroid-based push if the nearest point collapses to mark center.
                var softDist2 = options.PartRepelSoftening * options.PartRepelSoftening;
                var force = options.KRepelPart / softDist2;
                if (TryGetPartCentroid(part, out var centroidX, out var centroidY))
                {
                    var partDx = mark.Cx - centroidX;
                    var partDy = mark.Cy - centroidY;
                    var partDist = Math.Max(Math.Sqrt((partDx * partDx) + (partDy * partDy)), 0.001);
                    partRepelFx += force * partDx / partDist;
                    partRepelFy += force * partDy / partDist;
                }
                continue;
            }

            var ux = dx / dist;
            var uy = dy / dist;
            var markRadius = ComputeMarkRadius(mark, ux, uy);
            var effectiveDist = Math.Max(dist - markRadius, 0.0);
            if (effectiveDist > options.PartRepelRadius) continue;

            // Inverse-square repulsion with softening: force = KRepelPart / (effectiveDist² + ε²)
            var edgeSoftDist2 = effectiveDist * effectiveDist + options.PartRepelSoftening * options.PartRepelSoftening;
            var edgeForce = options.KRepelPart / edgeSoftDist2;
            partRepelFx += edgeForce * ux;
            partRepelFy += edgeForce * uy;
        }

        if (includeMarkRepulsion)
        {
            foreach (var other in allMarks)
            {
                if (other.Id == mark.Id) continue;

                if (TryGetMarkRepulsion(mark, other, options, out var repelFx, out var repelFy))
                {
                    markRepelFx += repelFx;
                    markRepelFy += repelFy;
                }
            }
        }

        if (mark.ReturnToAxisLine && hasAxis)
        {
            ApplyAxisLineSpring(mark, options.KReturnToAxisLine, ref attractFx, ref attractFy);
        }

        if (mark.ConstrainToAxis && hasAxis)
        {
            var aProj = attractFx * axisDx + attractFy * axisDy;
            attractFx = axisDx * aProj;
            attractFy = axisDy * aProj;
            var pProj = partRepelFx * axisDx + partRepelFy * axisDy;
            partRepelFx = axisDx * pProj;
            partRepelFy = axisDy * pProj;
            var mProj = markRepelFx * axisDx + markRepelFy * axisDy;
            markRepelFx = axisDx * mProj;
            markRepelFy = axisDy * mProj;

            // Perpendicular spring to axis line for axis-constrained marks in pass 1.
            if (options.KPerpRestoreAxis > 0.0)
            {
                ApplyAxisLineSpring(mark, options.KPerpRestoreAxis, ref attractFx, ref attractFy);
            }
        }

        return new ForceComponents(
            attractFx,
            attractFy,
            partRepelFx,
            partRepelFy,
            markRepelFx,
            markRepelFy);
    }

    private static bool TryGetPolygonCentroid(ForceDirectedMarkItem mark, out double centroidX, out double centroidY)
    {
        centroidX = 0.0;
        centroidY = 0.0;

        if (mark.OwnPolygon == null || mark.OwnPolygon.Count < 3)
            return false;

        centroidX = mark.OwnPolygon.Average(static point => point[0]);
        centroidY = mark.OwnPolygon.Average(static point => point[1]);
        return true;
    }

    private static bool TryGetPartCentroid(PartBbox part, out double centroidX, out double centroidY)
    {
        centroidX = 0.0;
        centroidY = 0.0;

        if (part.Polygon != null && part.Polygon.Count >= 3)
        {
            centroidX = part.Polygon.Average(static point => point[0]);
            centroidY = part.Polygon.Average(static point => point[1]);
            return true;
        }

        centroidX = (part.MinX + part.MaxX) * 0.5;
        centroidY = (part.MinY + part.MaxY) * 0.5;
        return true;
    }

    private static (double x, double y) NearestOnShape(double px, double py, PartBbox part)
    {
        if (part.Polygon != null && part.Polygon.Count >= 3)
            return NearestOnPolygon(px, py, part.Polygon);

        var nx = Math.Max(part.MinX, Math.Min(px, part.MaxX));
        var ny = Math.Max(part.MinY, Math.Min(py, part.MaxY));
        return (nx, ny);
    }

    private static (double x, double y) NearestOnPolygon(double px, double py, IReadOnlyList<double[]> polygon)
    {
        var bestX = polygon[0][0];
        var bestY = polygon[0][1];
        var bestDist2 = double.MaxValue;
        var n = polygon.Count;
        for (var i = 0; i < n; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % n];
            var (nx, ny) = NearestOnSegment(px, py, a[0], a[1], b[0], b[1]);
            var d2 = (nx - px) * (nx - px) + (ny - py) * (ny - py);
            if (d2 < bestDist2) { bestDist2 = d2; bestX = nx; bestY = ny; }
        }

        return (bestX, bestY);
    }

    private static (double x, double y) NearestOnSegment(double px, double py, double ax, double ay, double bx, double by)
    {
        var abx = bx - ax; var aby = by - ay;
        var len2 = abx * abx + aby * aby;
        if (len2 < 1e-10) return (ax, ay);
        var t = Math.Max(0.0, Math.Min(1.0, ((px - ax) * abx + (py - ay) * aby) / len2));
        return (ax + t * abx, ay + t * aby);
    }

    private static bool IsInsidePolygon(double px, double py, IReadOnlyList<double[]> polygon)
    {
        var inside = false;
        var n = polygon.Count;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            var xi = polygon[i][0]; var yi = polygon[i][1];
            var xj = polygon[j][0]; var yj = polygon[j][1];
            if (((yi > py) != (yj > py)) && px < (xj - xi) * (py - yi) / (yj - yi) + xi)
                inside = !inside;
        }
        return inside;
    }

    private static double Clamp(double value, double min, double max) =>
        Math.Max(min, Math.Min(max, value));

    private static double ComputeAttractForce(double dist, double farThreshold, ForcePassOptions options)
    {
        var threshold = Math.Max(farThreshold, options.IdealDist + 0.001);
        double force;
        if (dist <= threshold)
        {
            force = options.KAttract * Math.Log(dist / options.IdealDist);
        }
        else
        {
            var thresholdForce = options.KAttract * Math.Log(threshold / options.IdealDist);
            force = thresholdForce + (options.KFarAttract * (dist - threshold));
        }

        return Clamp(force, -options.MaxAttract, options.MaxAttract);
    }

    private static bool TryGetNormalizedAxis(ForceDirectedMarkItem mark, out double axisDx, out double axisDy)
    {
        axisDx = mark.AxisDx;
        axisDy = mark.AxisDy;
        var length = Math.Sqrt((axisDx * axisDx) + (axisDy * axisDy));
        if (length <= 0.001)
            return false;

        axisDx /= length;
        axisDy /= length;
        return true;
    }

    private static void ApplyAxisLineSpring(
        ForceDirectedMarkItem mark,
        double stiffness,
        ref double forceX,
        ref double forceY)
    {
        if (stiffness <= 0.0 || !TryGetNormalizedAxis(mark, out var axisDx, out var axisDy))
            return;

        var normalX = -axisDy;
        var normalY = axisDx;
        var offsetX = mark.Cx - mark.AxisOriginX;
        var offsetY = mark.Cy - mark.AxisOriginY;
        var signedDistance = (offsetX * normalX) + (offsetY * normalY);
        forceX += -stiffness * signedDistance * normalX;
        forceY += -stiffness * signedDistance * normalY;
    }

    private static double ComputeMarkRadius(ForceDirectedMarkItem mark, double ux, double uy)
    {
        if (mark.LocalCorners.Count >= 3)
        {
            var maxProjection = double.MinValue;
            foreach (var corner in mark.LocalCorners)
            {
                var projection = (corner[0] * ux) + (corner[1] * uy);
                if (projection > maxProjection)
                    maxProjection = projection;
            }

            return Math.Max(maxProjection, 0.0);
        }

        return ((mark.Width * 0.5) * Math.Abs(ux)) + ((mark.Height * 0.5) * Math.Abs(uy));
    }

    private static bool TryGetMarkRepulsion(
        ForceDirectedMarkItem mark,
        ForceDirectedMarkItem other,
        ForcePassOptions options,
        out double repelFx,
        out double repelFy)
    {
        repelFx = 0.0;
        repelFy = 0.0;

        if (mark.LocalCorners.Count >= 3 && other.LocalCorners.Count >= 3)
        {
            var markPolygon = PolygonGeometry.Translate(mark.LocalCorners, mark.Cx, mark.Cy);
            var otherPolygon = PolygonGeometry.Translate(other.LocalCorners, other.Cx, other.Cy);
            if (PolygonGeometry.TryGetMinimumTranslationVector(markPolygon, otherPolygon, out var axisX, out var axisY, out var depth))
            {
                repelFx = -axisX * (depth + options.MarkGapMm) * options.KRepelMark;
                repelFy = -axisY * (depth + options.MarkGapMm) * options.KRepelMark;
                return true;
            }

            if (PolygonGeometry.TryGetPolygonGapVector(markPolygon, otherPolygon, out var gapAxisX, out var gapAxisY, out var gap))
            {
                var gapViolation = options.MarkGapMm - gap;
                if (gapViolation <= 0.0)
                    return false;

                repelFx = -gapAxisX * gapViolation * options.KRepelMark;
                repelFy = -gapAxisY * gapViolation * options.KRepelMark;
                return true;
            }

            // Touching or fully separated OBB polygons are acceptable here.
            return false;
        }

        // Degraded fallback for marks without OBB geometry.
        var ox = (mark.Width + other.Width) * 0.5 + options.MarkGapMm - Math.Abs(mark.Cx - other.Cx);
        var oy = (mark.Height + other.Height) * 0.5 + options.MarkGapMm - Math.Abs(mark.Cy - other.Cy);
        if (ox <= 0.0 || oy <= 0.0)
            return false;

        if (ox < oy)
            repelFx = (mark.Cx >= other.Cx ? 1.0 : -1.0) * options.KRepelMark * ox;
        else
            repelFy = (mark.Cy >= other.Cy ? 1.0 : -1.0) * options.KRepelMark * oy;

        return true;
    }

    private static int CountMarkOverlapPairs(IReadOnlyList<ForceDirectedMarkItem> allMarks)
    {
        var count = 0;
        for (var i = 0; i < allMarks.Count; i++)
        for (var j = i + 1; j < allMarks.Count; j++)
        {
            var firstPolygon = BuildMarkPolygon(allMarks[i], allMarks[i].Cx, allMarks[i].Cy);
            var secondPolygon = BuildMarkPolygon(allMarks[j], allMarks[j].Cx, allMarks[j].Cy);
            if (PolygonGeometry.Intersects(firstPolygon, secondPolygon))
                count++;
        }

        return count;
    }

    private static double GetMarkOverlapSeverityForMark(IReadOnlyList<ForceDirectedMarkItem> allMarks, int markId)
    {
        var mark = allMarks.FirstOrDefault(item => item.Id == markId);
        if (mark == null)
            return 0.0;

        var markPolygon = BuildMarkPolygon(mark, mark.Cx, mark.Cy);
        var severity = 0.0;
        foreach (var other in allMarks)
        {
            if (other.Id == mark.Id)
                continue;

            var otherPolygon = BuildMarkPolygon(other, other.Cx, other.Cy);
            if (PolygonGeometry.TryGetMinimumTranslationVector(markPolygon, otherPolygon, out _, out _, out var depth))
                severity += depth;
        }

        return severity;
    }

    private static bool WouldOverlapForeignPart(
        ForceDirectedMarkItem mark,
        double targetX,
        double targetY,
        IReadOnlyList<PartBbox> allParts)
    {
        var markPolygon = BuildMarkPolygon(mark, targetX, targetY);
        foreach (var part in allParts)
        {
            if (mark.OwnModelId.HasValue && part.ModelId == mark.OwnModelId.Value)
                continue;

            if (part.Polygon != null && part.Polygon.Count >= 3)
            {
                if (PolygonGeometry.Intersects(markPolygon, part.Polygon))
                    return true;

                continue;
            }

            if (PolygonIntersectsBbox(markPolygon, part))
                return true;
        }

        return false;
    }

    private static bool WouldOverlapOtherMark(
        ForceDirectedMarkItem mark,
        IReadOnlyList<ForceDirectedMarkItem> allMarks,
        double targetX,
        double targetY)
    {
        var markPolygon = BuildMarkPolygon(mark, targetX, targetY);
        foreach (var other in allMarks)
        {
            if (other.Id == mark.Id)
                continue;

            var otherPolygon = BuildMarkPolygon(other, other.Cx, other.Cy);
            if (PolygonGeometry.Intersects(markPolygon, otherPolygon))
                return true;
        }

        return false;
    }

    private static IReadOnlyList<double[]> BuildMarkPolygon(ForceDirectedMarkItem mark, double cx, double cy)
    {
        if (mark.LocalCorners.Count >= 3)
            return PolygonGeometry.Translate(mark.LocalCorners, cx, cy);

        var halfWidth = mark.Width * 0.5;
        var halfHeight = mark.Height * 0.5;
        return
        [
            [cx - halfWidth, cy - halfHeight],
            [cx + halfWidth, cy - halfHeight],
            [cx + halfWidth, cy + halfHeight],
            [cx - halfWidth, cy + halfHeight]
        ];
    }

    private static bool PolygonIntersectsBbox(IReadOnlyList<double[]> polygon, PartBbox bbox)
    {
        foreach (var point in polygon)
        {
            if (point[0] >= bbox.MinX && point[0] <= bbox.MaxX &&
                point[1] >= bbox.MinY && point[1] <= bbox.MaxY)
                return true;
        }

        var bboxPolygon = new[]
        {
            new[] { bbox.MinX, bbox.MinY },
            new[] { bbox.MaxX, bbox.MinY },
            new[] { bbox.MaxX, bbox.MaxY },
            new[] { bbox.MinX, bbox.MaxY }
        };

        return PolygonGeometry.Intersects(polygon, bboxPolygon);
    }
}

internal readonly struct ForceIterationDebugInfo
{
    public ForceIterationDebugInfo(
        int iteration,
        int markId,
        double attractFx,
        double attractFy,
        double partRepelFx,
        double partRepelFy,
        double markRepelFx,
        double markRepelFy,
        double fx,
        double fy,
        double dx,
        double dy,
        double x,
        double y,
        bool axisStepMode = false,
        string axisStepReason = "")
    {
        Iteration = iteration;
        MarkId = markId;
        AttractFx = attractFx;
        AttractFy = attractFy;
        PartRepelFx = partRepelFx;
        PartRepelFy = partRepelFy;
        MarkRepelFx = markRepelFx;
        MarkRepelFy = markRepelFy;
        Fx = fx;
        Fy = fy;
        Dx = dx;
        Dy = dy;
        X = x;
        Y = y;
        AxisStepMode = axisStepMode;
        AxisStepReason = axisStepReason;
    }

    public int Iteration { get; }
    public int MarkId { get; }
    public double AttractFx { get; }
    public double AttractFy { get; }
    public double PartRepelFx { get; }
    public double PartRepelFy { get; }
    public double MarkRepelFx { get; }
    public double MarkRepelFy { get; }
    public double Fx { get; }
    public double Fy { get; }
    public double Dx { get; }
    public double Dy { get; }
    public double X { get; }
    public double Y { get; }
    public bool AxisStepMode { get; }
    public string AxisStepReason { get; }
}

internal readonly struct ForceIterationUpdate
{
    public ForceIterationUpdate(ForceDirectedMarkItem mark, ForceComponents debug, double dx, double dy, bool axisStepMode = false, string axisStepReason = "")
    {
        Mark = mark;
        Debug = debug;
        Dx = dx;
        Dy = dy;
        AxisStepMode = axisStepMode;
        AxisStepReason = axisStepReason;
    }

    public ForceDirectedMarkItem Mark { get; }
    public ForceComponents Debug { get; }
    public double Dx { get; }
    public double Dy { get; }
    public bool AxisStepMode { get; }
    public string AxisStepReason { get; }
}

internal readonly struct ForceComponents
{
    public ForceComponents(
        double attractFx,
        double attractFy,
        double partRepelFx,
        double partRepelFy,
        double markRepelFx,
        double markRepelFy)
    {
        AttractFx = attractFx;
        AttractFy = attractFy;
        PartRepelFx = partRepelFx;
        PartRepelFy = partRepelFy;
        MarkRepelFx = markRepelFx;
        MarkRepelFy = markRepelFy;
    }

    public double AttractFx { get; }
    public double AttractFy { get; }
    public double PartRepelFx { get; }
    public double PartRepelFy { get; }
    public double MarkRepelFx { get; }
    public double MarkRepelFy { get; }
    public double Fx => AttractFx + PartRepelFx + MarkRepelFx;
    public double Fy => AttractFy + PartRepelFy + MarkRepelFy;
}
