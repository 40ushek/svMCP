using System;
using System.Collections.Generic;
using System.Linq;
using TeklaMcpServer.Api.Algorithms.Geometry;

namespace TeklaMcpServer.Api.Algorithms.Marks;

internal readonly struct AxisMarkSeparationCleanupResult
{
    public AxisMarkSeparationCleanupResult(
        int iterations,
        int movedMarks,
        int beforeOverlaps,
        int afterOverlaps)
    {
        Iterations = iterations;
        MovedMarks = movedMarks;
        BeforeOverlaps = beforeOverlaps;
        AfterOverlaps = afterOverlaps;
    }

    public int Iterations { get; }
    public int MovedMarks { get; }
    public int BeforeOverlaps { get; }
    public int AfterOverlaps { get; }
}

internal static class AxisMarkSeparationCleanup
{
    public static AxisMarkSeparationCleanupResult Resolve(
        IReadOnlyList<ForceDirectedMarkItem> items,
        double gap,
        int maxIterations = 10)
    {
        var beforeOverlaps = CountOverlaps(items);
        if (beforeOverlaps == 0 || maxIterations <= 0)
            return new AxisMarkSeparationCleanupResult(0, 0, beforeOverlaps, beforeOverlaps);

        var movedIds = new HashSet<int>();
        var iterationsUsed = 0;

        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            var pairs = GetOverlapPairs(items);
            if (pairs.Count == 0)
                break;

            iterationsUsed = iteration + 1;
            var movedAny = false;

            foreach (var pair in pairs.OrderByDescending(x => x.Depth))
            {
                var a = items[pair.IndexA];
                var b = items[pair.IndexB];
                if (!AxisMarkPairSeparation.TryCompute(
                        AxisMarkPairSeparationMark.FromForceItem(a, hasAxis: a.ConstrainToAxis),
                        AxisMarkPairSeparationMark.FromForceItem(b, hasAxis: b.ConstrainToAxis),
                        gap,
                        out var separation) ||
                    !separation.HasMovement)
                {
                    continue;
                }

                var movedA = ApplyDelta(a, separation.DeltaAx, separation.DeltaAy);
                var movedB = ApplyDelta(b, separation.DeltaBx, separation.DeltaBy);
                movedAny |= movedA || movedB;

                if (movedA)
                    movedIds.Add(a.Id);
                if (movedB)
                    movedIds.Add(b.Id);
            }

            if (!movedAny)
                break;
        }

        return new AxisMarkSeparationCleanupResult(
            iterationsUsed,
            movedIds.Count,
            beforeOverlaps,
            CountOverlaps(items));
    }

    private static bool ApplyDelta(ForceDirectedMarkItem item, double dx, double dy)
    {
        if (!item.CanMove)
            return false;

        var nextX = item.Cx + dx;
        var nextY = item.Cy + dy;
        var moved = Math.Abs(nextX - item.Cx) > 0.001 || Math.Abs(nextY - item.Cy) > 0.001;

        item.Cx = nextX;
        item.Cy = nextY;
        return moved;
    }

    private static int CountOverlaps(IReadOnlyList<ForceDirectedMarkItem> items) =>
        GetOverlapPairs(items).Count;

    private static List<(int IndexA, int IndexB, double Depth)> GetOverlapPairs(IReadOnlyList<ForceDirectedMarkItem> items)
    {
        var result = new List<(int IndexA, int IndexB, double Depth)>();
        for (var i = 0; i < items.Count; i++)
        for (var j = i + 1; j < items.Count; j++)
        {
            var firstPolygon = BuildMarkPolygon(items[i]);
            var secondPolygon = BuildMarkPolygon(items[j]);
            if (PolygonGeometry.TryGetMinimumTranslationVector(firstPolygon, secondPolygon, out _, out _, out var depth))
                result.Add((i, j, depth));
        }

        return result;
    }

    private static IReadOnlyList<double[]> BuildMarkPolygon(ForceDirectedMarkItem item)
    {
        if (item.LocalCorners.Count >= 3)
            return PolygonGeometry.Translate(item.LocalCorners, item.Cx, item.Cy);

        var halfW = item.Width / 2.0;
        var halfH = item.Height / 2.0;
        return new[]
        {
            new[] { item.Cx - halfW, item.Cy - halfH },
            new[] { item.Cx + halfW, item.Cy - halfH },
            new[] { item.Cx + halfW, item.Cy + halfH },
            new[] { item.Cx - halfW, item.Cy + halfH }
        };
    }
}
