using System;
using System.Collections.Generic;
using System.Linq;
using TeklaMcpServer.Api.Algorithms.Geometry;
using TeklaMcpServer.Api.Drawing;

namespace TeklaMcpServer.Api.Algorithms.Marks;

public sealed class MarkOverlapResolver
{
    private const double MovementEpsilon = 0.001;

    public List<MarkLayoutPlacement> Resolve(
        IReadOnlyList<MarkLayoutPlacement> placements,
        MarkLayoutOptions options,
        out int iterationsUsed)
    {
        var result = placements.Select(p => p.Clone()).ToList();

        iterationsUsed = 0;

        for (var iteration = 0; iteration < options.MaxResolverIterations; iteration++)
        {
            iterationsUsed = iteration + 1;
            var movedAny = false;

            for (var i = 0; i < result.Count; i++)
            for (var j = i + 1; j < result.Count; j++)
            {
                var a = result[i];
                var b = result[j];

                if (!TryGetSeparation(a, b, out var separationAxisX, out var separationAxisY, out var separationDepth, out var overlapX, out var overlapY))
                    continue;

                if (TryResolveAlongAxis(a, b, options, out var movedAlongAxis))
                {
                    movedAny |= movedAlongAxis;
                    continue;
                }

                var split = GetSplit(a, b);
                if (split.Total == 0)
                    continue;

                var push = separationDepth + options.Gap;
                var moveX = separationAxisX * push;
                var moveY = separationAxisY * push;

                // Use MoveWithAnchorClamp so baseline marks (HasAxis && !HasLeaderLine)
                // are only pushed along their part axis, never perpendicular to it.
                var movedA2 = MoveWithAnchorClamp(a, -moveX * split.MoveA, -moveY * split.MoveA, options);
                var movedB2 = MoveWithAnchorClamp(b, +moveX * split.MoveB, +moveY * split.MoveB, options);
                movedAny |= movedA2 || movedB2;
            }

            if (!movedAny)
                break;
        }

        return result;
    }

    /// <summary>
    /// Post-process resolver for already placed marks.
    /// It only nudges marks participating in current overlap components and
    /// keeps movement local/minimal (MTV + gap).
    /// </summary>
    public List<MarkLayoutPlacement> ResolvePlacedMarks(
        IReadOnlyList<MarkLayoutPlacement> placements,
        MarkLayoutOptions options,
        out int iterationsUsed)
    {
        var result = placements.Select(p => p.Clone()).ToList();

        iterationsUsed = 0;

        for (var iteration = 0; iteration < options.MaxResolverIterations; iteration++)
        {
            var componentPairs = GetComponentPairs(result);
            if (componentPairs.Count == 0)
            {
                iterationsUsed = iteration + 1;
                break;
            }

            iterationsUsed = iteration + 1;
            var movedAny = false;

            foreach (var pair in componentPairs.OrderByDescending(x => x.Depth))
            {
                var a = result[pair.IndexA];
                var b = result[pair.IndexB];
                if (!TryGetSeparation(a, b, out var axisX, out var axisY, out var depth, out var overlapX, out var overlapY))
                    continue;

                if (TryResolveAlongAxis(a, b, options, out var movedAlongAxis))
                {
                    movedAny |= movedAlongAxis;
                    continue;
                }

                var split = GetSimpleSplit(a, b);
                if (split.Total <= 0)
                    continue;

                var push = depth + options.Gap;
                var moveX = axisX * push;
                var moveY = axisY * push;

                var movedA = MoveWithAnchorClamp(a, -moveX * split.MoveA, -moveY * split.MoveA, options);
                var movedB = MoveWithAnchorClamp(b, +moveX * split.MoveB, +moveY * split.MoveB, options);
                movedAny |= movedA || movedB;
            }

            if (!movedAny && TryNudgeOneOverlappingMark(result, options))
            {
                movedAny = true;
            }

            if (!movedAny)
                break;
        }

        return result;
    }

    public int CountOverlaps(IReadOnlyList<MarkLayoutPlacement> placements)
    {
        var overlaps = 0;

        for (var i = 0; i < placements.Count; i++)
        for (var j = i + 1; j < placements.Count; j++)
        {
            var a = placements[i];
            var b = placements[j];

            if (Overlaps(a, b))
                overlaps++;
        }

        return overlaps;
    }

    private static bool TryResolveAlongAxis(
        MarkLayoutPlacement a,
        MarkLayoutPlacement b,
        MarkLayoutOptions options,
        out bool movedAny)
    {
        movedAny = false;

        if (!AxisMarkPairSeparation.TryCompute(
                AxisMarkPairSeparationMark.FromPlacement(a),
                AxisMarkPairSeparationMark.FromPlacement(b),
                options.Gap,
                out var separation))
            return false;

        if (!separation.HasMovement)
            return true;

        if (separation.Mode == AxisMarkPairSeparationMode.ParallelAxes)
        {
            var movedA = MoveWithAnchorClamp(a, separation.DeltaAx, separation.DeltaAy, options);
            var movedB = MoveWithAnchorClamp(b, separation.DeltaBx, separation.DeltaBy, options);
            movedAny = movedA || movedB;
            return true;
        }

        var movedIndependentA = MoveWithAnchorClamp(a, separation.DeltaAx, separation.DeltaAy, options);
        var movedIndependentB = MoveWithAnchorClamp(b, separation.DeltaBx, separation.DeltaBy, options);
        movedAny = movedIndependentA || movedIndependentB;

        return true;
    }

    private static (double MoveA, double MoveB, double Total) GetSplit(
        MarkLayoutPlacement a,
        MarkLayoutPlacement b)
    {
        if (a.CanMove && b.CanMove)
        {
            if (a.HasLeaderLine && !b.HasLeaderLine)
                return (0.7, 0.3, 1.0);

            if (!a.HasLeaderLine && b.HasLeaderLine)
                return (0.3, 0.7, 1.0);

            if (a.HasLeaderLine && b.HasLeaderLine)
            {
                var distA = Distance(a.X, a.Y, a.AnchorX, a.AnchorY);
                var distB = Distance(b.X, b.Y, b.AnchorX, b.AnchorY);

                if (Math.Abs(distA - distB) < 2.0)
                    return (0.5, 0.5, 1.0);

                return distA > distB
                    ? (0.7, 0.3, 1.0)
                    : (0.3, 0.7, 1.0);
            }

            return (0.5, 0.5, 1.0);
        }

        if (a.CanMove)
            return (1.0, 0.0, 1.0);

        if (b.CanMove)
            return (0.0, 1.0, 1.0);

        return (0.0, 0.0, 0.0);
    }

    private static (double MoveA, double MoveB, double Total) GetSimpleSplit(
        MarkLayoutPlacement a,
        MarkLayoutPlacement b)
    {
        if (a.CanMove && b.CanMove)
        {
            if (a.HasLeaderLine && !b.HasLeaderLine)
                return (0.7, 0.3, 1.0);

            if (!a.HasLeaderLine && b.HasLeaderLine)
                return (0.3, 0.7, 1.0);

            return (0.5, 0.5, 1.0);
        }

        if (a.CanMove)
            return (1.0, 0.0, 1.0);

        if (b.CanMove)
            return (0.0, 1.0, 1.0);

        return (0.0, 0.0, 0.0);
    }

    private static bool MoveWithAnchorClamp(
        MarkLayoutPlacement placement,
        double dx,
        double dy,
        MarkLayoutOptions options)
    {
        if (!placement.CanMove)
            return false;

        var constrainedDx = dx;
        var constrainedDy = dy;

        // Keep baseline marks attached to their member direction:
        // no sideways jumps that make ownership ambiguous.
        if (!placement.HasLeaderLine && placement.HasAxis)
        {
            var axisDx = placement.AxisDx;
            var axisDy = placement.AxisDy;
            if (TryNormalize(ref axisDx, ref axisDy))
            {
                var alongAxis = Dot(constrainedDx, constrainedDy, axisDx, axisDy);
                constrainedDx = axisDx * alongAxis;
                constrainedDy = axisDy * alongAxis;
            }
        }

        var nextX = placement.X + constrainedDx;
        var nextY = placement.Y + constrainedDy;

        if (options.MaxDistanceFromAnchor > 0)
        {
            var fromAnchorX = nextX - placement.AnchorX;
            var fromAnchorY = nextY - placement.AnchorY;
            var dist = Math.Sqrt((fromAnchorX * fromAnchorX) + (fromAnchorY * fromAnchorY));
            if (dist > options.MaxDistanceFromAnchor && dist > MovementEpsilon)
            {
                var scale = options.MaxDistanceFromAnchor / dist;
                nextX = placement.AnchorX + (fromAnchorX * scale);
                nextY = placement.AnchorY + (fromAnchorY * scale);
            }
        }

        var moved = Math.Abs(nextX - placement.X) > MovementEpsilon || Math.Abs(nextY - placement.Y) > MovementEpsilon;
        placement.X = nextX;
        placement.Y = nextY;
        return moved;
    }

    private static bool TryNudgeOneOverlappingMark(List<MarkLayoutPlacement> placements, MarkLayoutOptions options)
    {
        for (var i = 0; i < placements.Count; i++)
        for (var j = i + 1; j < placements.Count; j++)
        {
            if (!TryGetSeparation(placements[i], placements[j], out _, out _, out _, out _, out _))
                continue;

            if (TryNudgeMark(placements, placements[j], options) ||
                TryNudgeMark(placements, placements[i], options))
                return true;
        }

        return false;
    }

    private static bool TryNudgeMark(
        IReadOnlyList<MarkLayoutPlacement> placements,
        MarkLayoutPlacement mark,
        MarkLayoutOptions options)
    {
        if (!mark.CanMove)
            return false;

        var originalX = mark.X;
        var originalY = mark.Y;
        var baseStep = Math.Max(Math.Min(mark.Width, mark.Height) * 0.2, options.Gap + 2.0);
        var candidates = new List<(double Dx, double Dy)>();

        if (!mark.HasLeaderLine && mark.HasAxis)
        {
            var axisDx = mark.AxisDx;
            var axisDy = mark.AxisDy;
            if (!TryNormalize(ref axisDx, ref axisDy))
                return false;

            for (var ring = 1; ring <= 6; ring++)
            {
                var step = baseStep * ring;
                candidates.Add((axisDx * step, axisDy * step));
                candidates.Add((-axisDx * step, -axisDy * step));
            }
        }
        else
        {
            var dirs = new[]
            {
                (1.0, 0.0), (-1.0, 0.0), (0.0, 1.0), (0.0, -1.0)
            };
            for (var ring = 1; ring <= 5; ring++)
            {
                var step = baseStep * ring;
                foreach (var dir in dirs)
                    candidates.Add((dir.Item1 * step, dir.Item2 * step));
            }
        }

        foreach (var candidate in candidates)
        {
            mark.X = originalX;
            mark.Y = originalY;
            if (!MoveWithAnchorClamp(mark, candidate.Dx, candidate.Dy, options))
                continue;

            if (HasAnyOverlap(placements, mark))
                continue;

            return true;
        }

        mark.X = originalX;
        mark.Y = originalY;
        return false;
    }

    private static bool HasAnyOverlap(IReadOnlyList<MarkLayoutPlacement> placements, MarkLayoutPlacement mark)
    {
        foreach (var other in placements)
        {
            if (ReferenceEquals(other, mark))
                continue;

            if (Overlaps(mark, other))
                return true;
        }

        return false;
    }

    private static List<(int IndexA, int IndexB, double Depth)> GetComponentPairs(IReadOnlyList<MarkLayoutPlacement> placements)
    {
        var result = new List<(int IndexA, int IndexB, double Depth)>();
        for (var i = 0; i < placements.Count; i++)
            for (var j = i + 1; j < placements.Count; j++)
                if (TryGetSeparation(placements[i], placements[j], out _, out _, out var depth, out _, out _))
                    result.Add((i, j, depth));
        return result;
    }

    private static double Distance(double x1, double y1, double x2, double y2)
    {
        var dx = x1 - x2;
        var dy = y1 - y2;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static double Dot(double x, double y, double axisDx, double axisDy)
    {
        return (x * axisDx) + (y * axisDy);
    }

    private static bool TryNormalize(ref double dx, ref double dy)
    {
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length < 0.001)
            return false;

        dx /= length;
        dy /= length;
        return true;
    }

    private static bool Overlaps(MarkLayoutPlacement a, MarkLayoutPlacement b)
    {
        if (a.LocalCorners.Count >= 3 && b.LocalCorners.Count >= 3)
        {
            var aPolygon = TranslateCorners(a);
            var bPolygon = TranslateCorners(b);
            return PolygonGeometry.Intersects(aPolygon, bPolygon);
        }

        var overlapX = Math.Min(a.X + (a.Width / 2.0), b.X + (b.Width / 2.0))
            - Math.Max(a.X - (a.Width / 2.0), b.X - (b.Width / 2.0));
        var overlapY = Math.Min(a.Y + (a.Height / 2.0), b.Y + (b.Height / 2.0))
            - Math.Max(a.Y - (a.Height / 2.0), b.Y - (b.Height / 2.0));

        return overlapX > 0 && overlapY > 0;
    }

    private static bool TryGetSeparation(
        MarkLayoutPlacement a,
        MarkLayoutPlacement b,
        out double separationAxisX,
        out double separationAxisY,
        out double separationDepth,
        out double overlapX,
        out double overlapY)
    {
        separationAxisX = 0.0;
        separationAxisY = 0.0;
        separationDepth = 0.0;
        overlapX = 0.0;
        overlapY = 0.0;

        if (a.LocalCorners.Count >= 3 && b.LocalCorners.Count >= 3)
        {
            var aPolygon = TranslateCorners(a);
            var bPolygon = TranslateCorners(b);
            if (!PolygonGeometry.TryGetMinimumTranslationVector(aPolygon, bPolygon, out separationAxisX, out separationAxisY, out separationDepth))
                return false;

            overlapX = Math.Abs(separationAxisX * separationDepth);
            overlapY = Math.Abs(separationAxisY * separationDepth);
            return true;
        }

        overlapX = Math.Min(a.X + (a.Width / 2.0), b.X + (b.Width / 2.0))
            - Math.Max(a.X - (a.Width / 2.0), b.X - (b.Width / 2.0));
        overlapY = Math.Min(a.Y + (a.Height / 2.0), b.Y + (b.Height / 2.0))
            - Math.Max(a.Y - (a.Height / 2.0), b.Y - (b.Height / 2.0));

        if (overlapX <= 0 || overlapY <= 0)
            return false;

        var moveX = overlapX <= overlapY;
        separationAxisX = moveX ? (b.X >= a.X ? 1.0 : -1.0) : 0.0;
        separationAxisY = moveX ? 0.0 : (b.Y >= a.Y ? 1.0 : -1.0);
        separationDepth = moveX ? overlapX : overlapY;
        return true;
    }

    private static List<double[]> TranslateCorners(MarkLayoutPlacement placement)
    {
        return PolygonGeometry.Translate(placement.LocalCorners, placement.X, placement.Y);
    }
}
