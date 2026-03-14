using System;
using System.Collections.Generic;
using System.Linq;
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
        var result = placements
            .Select(p => new MarkLayoutPlacement
            {
                Id = p.Id,
                X = p.X,
                Y = p.Y,
                Width = p.Width,
                Height = p.Height,
                AnchorX = p.AnchorX,
                AnchorY = p.AnchorY,
                HasLeaderLine = p.HasLeaderLine,
                HasAxis = p.HasAxis,
                AxisDx = p.AxisDx,
                AxisDy = p.AxisDy,
                CanMove = p.CanMove,
                LocalCorners = p.LocalCorners.Select(c => new[] { c[0], c[1] }).ToList()
            })
            .ToList();

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

                if (TryResolveAlongAxis(a, b, separationAxisX, separationAxisY, separationDepth, overlapX, overlapY, options, out movedAny))
                    continue;

                var split = GetSplit(a, b);
                if (split.Total == 0)
                    continue;

                var push = separationDepth + options.Gap;
                var moveX = separationAxisX * push;
                var moveY = separationAxisY * push;
                a.X -= moveX * split.MoveA;
                a.Y -= moveY * split.MoveA;
                b.X += moveX * split.MoveB;
                b.Y += moveY * split.MoveB;

                movedAny = true;
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
        var result = placements
            .Select(p => new MarkLayoutPlacement
            {
                Id = p.Id,
                X = p.X,
                Y = p.Y,
                Width = p.Width,
                Height = p.Height,
                AnchorX = p.AnchorX,
                AnchorY = p.AnchorY,
                HasLeaderLine = p.HasLeaderLine,
                HasAxis = p.HasAxis,
                AxisDx = p.AxisDx,
                AxisDy = p.AxisDy,
                CanMove = p.CanMove,
                LocalCorners = p.LocalCorners.Select(c => new[] { c[0], c[1] }).ToList()
            })
            .ToList();

        iterationsUsed = 0;

        for (var iteration = 0; iteration < options.MaxResolverIterations; iteration++)
        {
            var componentPairs = GetComponentPairs(result);
            if (componentPairs.Count == 0)
            {
                iterationsUsed = iteration;
                break;
            }

            iterationsUsed = iteration + 1;
            var movedAny = false;

            foreach (var pair in componentPairs.OrderByDescending(x => x.Depth))
            {
                var a = result[pair.IndexA];
                var b = result[pair.IndexB];
                if (!TryGetSeparation(a, b, out var axisX, out var axisY, out var depth, out _, out _))
                    continue;

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
        double separationAxisX,
        double separationAxisY,
        double separationDepth,
        double overlapX,
        double overlapY,
        MarkLayoutOptions options,
        out bool movedAny)
    {
        movedAny = false;

        if (a.HasLeaderLine || b.HasLeaderLine || !a.HasAxis || !b.HasAxis)
            return false;

        var aAxisDx = a.AxisDx;
        var aAxisDy = a.AxisDy;
        if (!TryNormalize(ref aAxisDx, ref aAxisDy))
            return false;

        var bAxisDx = b.AxisDx;
        var bAxisDy = b.AxisDy;
        if (!TryNormalize(ref bAxisDx, ref bAxisDy))
            return false;

        var dot = (aAxisDx * bAxisDx) + (aAxisDy * bAxisDy);
        var split = GetSplit(a, b);
        if (split.Total == 0)
            return true;

        if (Math.Abs(dot) < 0.95)
            return TryResolveAlongIndependentAxes(a, b, aAxisDx, aAxisDy, bAxisDx, bAxisDy, separationAxisX, separationAxisY, separationDepth, overlapX, overlapY, options, split, out movedAny);

        bAxisDx = dot < 0 ? -bAxisDx : bAxisDx;
        bAxisDy = dot < 0 ? -bAxisDy : bAxisDy;
        var axisDx = aAxisDx + bAxisDx;
        var axisDy = aAxisDy + bAxisDy;
        if (!TryNormalize(ref axisDx, ref axisDy))
            return false;

        var projectedHalfA = ProjectHalfExtent(a, axisDx, axisDy);
        var projectedHalfB = ProjectHalfExtent(b, axisDx, axisDy);
        var centerA = Dot(a.X, a.Y, axisDx, axisDy);
        var centerB = Dot(b.X, b.Y, axisDx, axisDy);
        var axisOverlap = (projectedHalfA + projectedHalfB) - Math.Abs(centerA - centerB);

        if (axisOverlap <= 0)
            return true;

        var push = axisOverlap + options.Gap;
        var direction = centerB >= centerA ? 1.0 : -1.0;

        a.X -= axisDx * direction * push * split.MoveA;
        a.Y -= axisDy * direction * push * split.MoveA;
        b.X += axisDx * direction * push * split.MoveB;
        b.Y += axisDy * direction * push * split.MoveB;
        movedAny = true;
        return true;
    }

    private static bool TryResolveAlongIndependentAxes(
        MarkLayoutPlacement a,
        MarkLayoutPlacement b,
        double aAxisDx,
        double aAxisDy,
        double bAxisDx,
        double bAxisDy,
        double separationAxisX,
        double separationAxisY,
        double separationDepth,
        double overlapX,
        double overlapY,
        MarkLayoutOptions options,
        (double MoveA, double MoveB, double Total) split,
        out bool movedAny)
    {
        movedAny = false;

        if (!a.CanMove && !b.CanMove)
            return true;

        if (a.CanMove && b.CanMove)
        {
            var desiredDx = separationAxisX * (separationDepth + options.Gap);
            var desiredDy = separationAxisY * (separationDepth + options.Gap);

            var scaledADx = aAxisDx * split.MoveA;
            var scaledADy = aAxisDy * split.MoveA;
            var scaledBDx = bAxisDx * split.MoveB;
            var scaledBDy = bAxisDy * split.MoveB;

            var determinant = (scaledADx * scaledBDy) - (scaledADy * scaledBDx);
            if (Math.Abs(determinant) < 0.001)
                return false;

            var moveA = ((desiredDx * scaledBDy) - (desiredDy * scaledBDx)) / determinant;
            var moveB = ((scaledADx * desiredDy) - (scaledADy * desiredDx)) / determinant;

            a.X -= aAxisDx * moveA * split.MoveA;
            a.Y -= aAxisDy * moveA * split.MoveA;
            b.X += bAxisDx * moveB * split.MoveB;
            b.Y += bAxisDy * moveB * split.MoveB;
            movedAny = true;
            return true;
        }

        if (a.CanMove)
        {
            var move = ResolveSingleAxisMove(aAxisDx, aAxisDy, separationAxisX, separationAxisY, separationDepth, options);
            a.X -= aAxisDx * move;
            a.Y -= aAxisDy * move;
            movedAny = true;
            return true;
        }

        if (b.CanMove)
        {
            var move = ResolveSingleAxisMove(bAxisDx, bAxisDy, separationAxisX, separationAxisY, separationDepth, options);
            b.X += bAxisDx * move;
            b.Y += bAxisDy * move;
            movedAny = true;
            return true;
        }

        return true;
    }

    private static double ResolveSingleAxisMove(
        double axisDx,
        double axisDy,
        double separationAxisX,
        double separationAxisY,
        double separationDepth,
        MarkLayoutOptions options)
    {
        var targetDx = separationAxisX * (separationDepth + options.Gap);
        var targetDy = separationAxisY * (separationDepth + options.Gap);

        var alongX = Math.Abs(axisDx) >= 0.001 ? targetDx / axisDx : 0.0;
        var alongY = Math.Abs(axisDy) >= 0.001 ? targetDy / axisDy : 0.0;

        if (Math.Abs(axisDx) < 0.001)
            return alongY;

        if (Math.Abs(axisDy) < 0.001)
            return alongX;

        return Math.Abs(alongX) >= Math.Abs(alongY) ? alongX : alongY;
    }

    private static (double MoveA, double MoveB, double Total) GetSplit(
        MarkLayoutPlacement a,
        MarkLayoutPlacement b)
    {
        if (a.CanMove && b.CanMove)
        {
            if (a.HasLeaderLine && !b.HasLeaderLine)
                return (0.35, 0.65, 1.0);

            if (!a.HasLeaderLine && b.HasLeaderLine)
                return (0.65, 0.35, 1.0);

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
                return (0.35, 0.65, 1.0);

            if (!a.HasLeaderLine && b.HasLeaderLine)
                return (0.65, 0.35, 1.0);

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
            var effectiveMaxDistance = options.MaxDistanceFromAnchor;
            if (!placement.HasLeaderLine && placement.HasAxis)
                effectiveMaxDistance = Math.Min(effectiveMaxDistance, 60.0);

            var fromAnchorX = nextX - placement.AnchorX;
            var fromAnchorY = nextY - placement.AnchorY;
            var dist = Math.Sqrt((fromAnchorX * fromAnchorX) + (fromAnchorY * fromAnchorY));
            if (dist > effectiveMaxDistance && dist > MovementEpsilon)
            {
                var scale = effectiveMaxDistance / dist;
                nextX = placement.AnchorX + (fromAnchorX * scale);
                nextY = placement.AnchorY + (fromAnchorY * scale);
            }
        }

        var moved = Math.Abs(nextX - placement.X) > MovementEpsilon || Math.Abs(nextY - placement.Y) > MovementEpsilon;
        placement.X = nextX;
        placement.Y = nextY;
        return moved;
    }

    private static List<(int IndexA, int IndexB, double Depth)> GetComponentPairs(IReadOnlyList<MarkLayoutPlacement> placements)
    {
        var edges = new List<(int A, int B, double Depth)>();
        var adjacency = new Dictionary<int, List<int>>();

        for (var i = 0; i < placements.Count; i++)
        {
            for (var j = i + 1; j < placements.Count; j++)
            {
                if (!TryGetSeparation(placements[i], placements[j], out _, out _, out var depth, out _, out _))
                    continue;

                edges.Add((i, j, depth));
                if (!adjacency.TryGetValue(i, out var ai))
                {
                    ai = new List<int>();
                    adjacency[i] = ai;
                }

                if (!adjacency.TryGetValue(j, out var aj))
                {
                    aj = new List<int>();
                    adjacency[j] = aj;
                }

                ai.Add(j);
                aj.Add(i);
            }
        }

        if (edges.Count == 0)
            return new List<(int IndexA, int IndexB, double Depth)>();

        var componentByNode = BuildComponentMap(adjacency);
        return edges
            .Where(e => componentByNode.TryGetValue(e.A, out var ca) &&
                        componentByNode.TryGetValue(e.B, out var cb) &&
                        ca == cb)
            .Select(e => (e.A, e.B, e.Depth))
            .ToList();
    }

    private static Dictionary<int, int> BuildComponentMap(Dictionary<int, List<int>> adjacency)
    {
        var result = new Dictionary<int, int>();
        var componentId = 0;

        foreach (var node in adjacency.Keys)
        {
            if (result.ContainsKey(node))
                continue;

            var stack = new Stack<int>();
            stack.Push(node);
            result[node] = componentId;

            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (!adjacency.TryGetValue(current, out var neighbors))
                    continue;

                foreach (var neighbor in neighbors)
                {
                    if (result.ContainsKey(neighbor))
                        continue;

                    result[neighbor] = componentId;
                    stack.Push(neighbor);
                }
            }

            componentId++;
        }

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

    private static double ProjectHalfExtent(double width, double height, double axisDx, double axisDy)
    {
        return (Math.Abs(axisDx) * (width / 2.0)) + (Math.Abs(axisDy) * (height / 2.0));
    }

    private static double ProjectHalfExtent(MarkLayoutPlacement placement, double axisDx, double axisDy)
    {
        if (placement.LocalCorners.Count < 3)
            return ProjectHalfExtent(placement.Width, placement.Height, axisDx, axisDy);

        return placement.LocalCorners
            .Select(c => Math.Abs(Dot(c[0], c[1], axisDx, axisDy)))
            .DefaultIfEmpty(0.0)
            .Max();
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
            return MarkGeometryHelper.PolygonsIntersect(aPolygon, bPolygon);
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
            if (!MarkGeometryHelper.TryGetMinimumTranslationVector(aPolygon, bPolygon, out separationAxisX, out separationAxisY, out separationDepth))
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
        return MarkGeometryHelper.TranslateLocalCorners(placement.LocalCorners, placement.X, placement.Y);
    }
}
