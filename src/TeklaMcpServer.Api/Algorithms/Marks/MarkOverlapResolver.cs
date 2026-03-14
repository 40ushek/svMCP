using System;
using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Algorithms.Marks;

public sealed class MarkOverlapResolver
{
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
                CanMove = p.CanMove
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

                var overlapX = Math.Min(a.X + (a.Width / 2.0), b.X + (b.Width / 2.0))
                    - Math.Max(a.X - (a.Width / 2.0), b.X - (b.Width / 2.0));
                var overlapY = Math.Min(a.Y + (a.Height / 2.0), b.Y + (b.Height / 2.0))
                    - Math.Max(a.Y - (a.Height / 2.0), b.Y - (b.Height / 2.0));

                if (overlapX <= 0 || overlapY <= 0)
                    continue;

                if (TryResolveAlongAxis(a, b, overlapX, overlapY, options, out movedAny))
                    continue;

                var moveX = overlapX <= overlapY;
                var push = (moveX ? overlapX : overlapY) + options.Gap;
                var split = GetSplit(a, b);
                if (split.Total == 0)
                    continue;

                if (moveX)
                {
                    var direction = b.X >= a.X ? 1.0 : -1.0;
                    a.X -= direction * push * split.MoveA;
                    b.X += direction * push * split.MoveB;
                }
                else
                {
                    var direction = b.Y >= a.Y ? 1.0 : -1.0;
                    a.Y -= direction * push * split.MoveA;
                    b.Y += direction * push * split.MoveB;
                }

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

            var overlapX = Math.Min(a.X + (a.Width / 2.0), b.X + (b.Width / 2.0))
                - Math.Max(a.X - (a.Width / 2.0), b.X - (b.Width / 2.0));
            var overlapY = Math.Min(a.Y + (a.Height / 2.0), b.Y + (b.Height / 2.0))
                - Math.Max(a.Y - (a.Height / 2.0), b.Y - (b.Height / 2.0));

            if (overlapX > 0 && overlapY > 0)
                overlaps++;
        }

        return overlaps;
    }

    private static bool TryResolveAlongAxis(
        MarkLayoutPlacement a,
        MarkLayoutPlacement b,
        double overlapX,
        double overlapY,
        MarkLayoutOptions options,
        out bool movedAny)
    {
        movedAny = false;

        if (a.HasLeaderLine || b.HasLeaderLine || !a.HasAxis || !b.HasAxis)
            return false;

        var dot = (a.AxisDx * b.AxisDx) + (a.AxisDy * b.AxisDy);
        if (Math.Abs(dot) < 0.95)
            return false;

        var split = GetSplit(a, b);
        if (split.Total == 0)
            return true;

        var bAxisDx = dot < 0 ? -b.AxisDx : b.AxisDx;
        var bAxisDy = dot < 0 ? -b.AxisDy : b.AxisDy;
        var axisDx = a.AxisDx + bAxisDx;
        var axisDy = a.AxisDy + bAxisDy;
        if (!TryNormalize(ref axisDx, ref axisDy))
            return false;

        var projectedHalfA = ProjectHalfExtent(a.Width, a.Height, axisDx, axisDy);
        var projectedHalfB = ProjectHalfExtent(b.Width, b.Height, axisDx, axisDy);
        var centerA = Dot(a.X, a.Y, axisDx, axisDy);
        var centerB = Dot(b.X, b.Y, axisDx, axisDy);
        var axisOverlap = (projectedHalfA + projectedHalfB) - Math.Abs(centerA - centerB);

        if (axisOverlap <= 0)
            return true;

        var push = Math.Max(axisOverlap, Math.Min(overlapX, overlapY)) + options.Gap;
        var direction = centerB >= centerA ? 1.0 : -1.0;

        a.X -= axisDx * direction * push * split.MoveA;
        a.Y -= axisDy * direction * push * split.MoveA;
        b.X += axisDx * direction * push * split.MoveB;
        b.Y += axisDy * direction * push * split.MoveB;
        movedAny = true;
        return true;
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

    private static bool TryNormalize(ref double dx, ref double dy)
    {
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length < 0.001)
            return false;

        dx /= length;
        dy /= length;
        return true;
    }
}
