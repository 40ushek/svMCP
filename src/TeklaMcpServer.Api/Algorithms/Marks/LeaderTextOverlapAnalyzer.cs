using System;
using System.Collections.Generic;
using System.Linq;
using TeklaMcpServer.Api.Algorithms.Geometry;

namespace TeklaMcpServer.Api.Algorithms.Marks;

internal sealed class LeaderTextOverlapMark
{
    public int MarkId { get; set; }

    public List<double[]> TextPolygon { get; set; } = [];

    public List<double[]> LeaderPolyline { get; set; } = [];
}

internal sealed class LeaderTextOverlapConflict
{
    public int MarkId { get; set; }

    public int CrossedMarkId { get; set; }

    public bool IsOwn { get; set; }

    public int SegmentIndex { get; set; }

    public double Severity { get; set; }
}

internal sealed class LeaderTextOverlapSummary
{
    public int TotalCrossings => Conflicts.Count;

    public int OwnCrossings { get; set; }

    public int ForeignCrossings { get; set; }

    public double Severity { get; set; }

    public List<LeaderTextOverlapConflict> Conflicts { get; } = [];
}

internal static class LeaderTextOverlapAnalyzer
{
    private const double Epsilon = 0.000001;

    public static LeaderTextOverlapSummary Analyze(
        IReadOnlyList<LeaderTextOverlapMark> marks,
        double ownEndIgnoreDistance)
    {
        if (marks == null)
            throw new ArgumentNullException(nameof(marks));

        var summary = new LeaderTextOverlapSummary();
        var safeOwnEndIgnoreDistance = Math.Max(0.0, ownEndIgnoreDistance);

        foreach (var mark in marks)
        {
            if (mark.LeaderPolyline.Count < 2)
                continue;

            for (var segmentIndex = 0; segmentIndex < mark.LeaderPolyline.Count - 1; segmentIndex++)
            {
                var start = mark.LeaderPolyline[segmentIndex];
                var end = mark.LeaderPolyline[segmentIndex + 1];
                if (start.Length < 2 || end.Length < 2)
                    continue;

                foreach (var crossed in marks)
                {
                    if (crossed.TextPolygon.Count < 3)
                        continue;

                    var isOwn = crossed.MarkId == mark.MarkId;
                    var overlapLength = ComputeSegmentInsideLength(
                        start[0],
                        start[1],
                        end[0],
                        end[1],
                        crossed.TextPolygon);
                    if (overlapLength <= Epsilon)
                        continue;

                    if (isOwn &&
                        IsOwnEndpointTouch(mark, segmentIndex, overlapLength, safeOwnEndIgnoreDistance))
                    {
                        continue;
                    }

                    summary.Conflicts.Add(new LeaderTextOverlapConflict
                    {
                        MarkId = mark.MarkId,
                        CrossedMarkId = crossed.MarkId,
                        IsOwn = isOwn,
                        SegmentIndex = segmentIndex,
                        Severity = overlapLength
                    });
                    summary.Severity += overlapLength;
                    if (isOwn)
                        summary.OwnCrossings++;
                    else
                        summary.ForeignCrossings++;
                }
            }
        }

        return summary;
    }

    private static bool IsOwnEndpointTouch(
        LeaderTextOverlapMark mark,
        int segmentIndex,
        double overlapLength,
        double ownEndIgnoreDistance)
    {
        return segmentIndex == mark.LeaderPolyline.Count - 2 &&
               overlapLength <= ownEndIgnoreDistance + Epsilon;
    }

    private static double ComputeSegmentInsideLength(
        double startX,
        double startY,
        double endX,
        double endY,
        IReadOnlyList<double[]> polygon)
    {
        var dx = endX - startX;
        var dy = endY - startY;
        var segmentLength = Math.Sqrt((dx * dx) + (dy * dy));
        if (segmentLength <= Epsilon)
            return 0.0;

        var parameters = new List<double> { 0.0, 1.0 };
        for (var i = 0; i < polygon.Count; i++)
        {
            var edgeStart = polygon[i];
            var edgeEnd = polygon[(i + 1) % polygon.Count];
            if (edgeStart.Length < 2 || edgeEnd.Length < 2)
                continue;

            if (TryIntersectSegments(
                    startX,
                    startY,
                    endX,
                    endY,
                    edgeStart[0],
                    edgeStart[1],
                    edgeEnd[0],
                    edgeEnd[1],
                    out var t))
            {
                parameters.Add(t);
            }
        }

        parameters = parameters
            .Where(static t => t >= -Epsilon && t <= 1.0 + Epsilon)
            .Select(static t => Math.Max(0.0, Math.Min(1.0, t)))
            .OrderBy(static t => t)
            .Aggregate(new List<double>(), static (acc, t) =>
            {
                if (acc.Count == 0 || Math.Abs(acc[acc.Count - 1] - t) > Epsilon)
                    acc.Add(t);
                return acc;
            });

        var length = 0.0;
        for (var i = 0; i < parameters.Count - 1; i++)
        {
            var t0 = parameters[i];
            var t1 = parameters[i + 1];
            if (t1 - t0 <= Epsilon)
                continue;

            var midT = (t0 + t1) * 0.5;
            var midX = startX + (dx * midT);
            var midY = startY + (dy * midT);
            if (PolygonGeometry.ContainsPoint(polygon, midX, midY))
                length += (t1 - t0) * segmentLength;
        }

        return length;
    }

    private static bool TryIntersectSegments(
        double firstStartX,
        double firstStartY,
        double firstEndX,
        double firstEndY,
        double secondStartX,
        double secondStartY,
        double secondEndX,
        double secondEndY,
        out double firstT)
    {
        firstT = 0.0;
        var firstDx = firstEndX - firstStartX;
        var firstDy = firstEndY - firstStartY;
        var secondDx = secondEndX - secondStartX;
        var secondDy = secondEndY - secondStartY;
        var denominator = Cross(firstDx, firstDy, secondDx, secondDy);
        if (Math.Abs(denominator) <= Epsilon)
            return false;

        var diffX = secondStartX - firstStartX;
        var diffY = secondStartY - firstStartY;
        firstT = Cross(diffX, diffY, secondDx, secondDy) / denominator;
        var secondT = Cross(diffX, diffY, firstDx, firstDy) / denominator;
        return firstT >= -Epsilon &&
               firstT <= 1.0 + Epsilon &&
               secondT >= -Epsilon &&
               secondT <= 1.0 + Epsilon;
    }

    private static double Cross(double ax, double ay, double bx, double by) => (ax * by) - (ay * bx);
}
