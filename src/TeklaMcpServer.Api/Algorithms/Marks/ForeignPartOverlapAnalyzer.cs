using System;
using System.Collections.Generic;
using System.Linq;
using TeklaMcpServer.Api.Algorithms.Geometry;

namespace TeklaMcpServer.Api.Algorithms.Marks;

internal enum ForeignPartOverlapKind
{
    PartialForeignPartOverlap,
    MarkInsideForeignPart,
    ForeignPartInsideMark
}

internal readonly struct ForeignPartOverlap
{
    public ForeignPartOverlap(int markId, int partModelId, double depth, ForeignPartOverlapKind kind)
    {
        MarkId = markId;
        PartModelId = partModelId;
        Depth = depth;
        Kind = kind;
    }

    public int MarkId { get; }
    public int PartModelId { get; }
    public double Depth { get; }
    public ForeignPartOverlapKind Kind { get; }
}

internal readonly struct ForeignPartOverlapSummary
{
    public ForeignPartOverlapSummary(IReadOnlyList<ForeignPartOverlap> overlaps)
    {
        Overlaps = overlaps;
        Conflicts = overlaps.Count;
        Severity = overlaps.Sum(static x => x.Depth);
        MarkInsideConflicts = overlaps.Count(static x => x.Kind == ForeignPartOverlapKind.MarkInsideForeignPart);
        MarkInsideSeverity = overlaps
            .Where(static x => x.Kind == ForeignPartOverlapKind.MarkInsideForeignPart)
            .Sum(static x => x.Depth);
        PartInsideConflicts = overlaps.Count(static x => x.Kind == ForeignPartOverlapKind.ForeignPartInsideMark);
        PartInsideSeverity = overlaps
            .Where(static x => x.Kind == ForeignPartOverlapKind.ForeignPartInsideMark)
            .Sum(static x => x.Depth);
        PartialConflicts = overlaps.Count(static x => x.Kind == ForeignPartOverlapKind.PartialForeignPartOverlap);
        PartialSeverity = overlaps
            .Where(static x => x.Kind == ForeignPartOverlapKind.PartialForeignPartOverlap)
            .Sum(static x => x.Depth);
    }

    public IReadOnlyList<ForeignPartOverlap> Overlaps { get; }
    public int Conflicts { get; }
    public double Severity { get; }
    public int MarkInsideConflicts { get; }
    public double MarkInsideSeverity { get; }
    public int PartInsideConflicts { get; }
    public double PartInsideSeverity { get; }
    public int PartialConflicts { get; }
    public double PartialSeverity { get; }
}

internal static class ForeignPartOverlapAnalyzer
{
    public static ForeignPartOverlapSummary Analyze(
        IReadOnlyList<ForceDirectedMarkItem> marks,
        IReadOnlyList<PartBbox> parts,
        double threshold)
    {
        var overlaps = new List<ForeignPartOverlap>();
        foreach (var mark in marks)
        {
            var markPolygon = BuildMarkPolygon(mark);
            if (markPolygon.Count < 3)
                continue;

            foreach (var part in parts)
            {
                if (mark.OwnModelId.HasValue && part.ModelId == mark.OwnModelId.Value)
                    continue;

                var partPolygon = BuildPartPolygon(part);
                if (partPolygon.Count < 3)
                    continue;

                if (!PolygonGeometry.TryGetMinimumTranslationVector(markPolygon, partPolygon, out _, out _, out var depth))
                    continue;

                if (depth <= threshold)
                    continue;

                overlaps.Add(new ForeignPartOverlap(
                    mark.Id,
                    part.ModelId,
                    depth,
                    Classify(markPolygon, partPolygon)));
            }
        }

        return new ForeignPartOverlapSummary(overlaps);
    }

    private static ForeignPartOverlapKind Classify(
        IReadOnlyList<double[]> markPolygon,
        IReadOnlyList<double[]> partPolygon)
    {
        if (AllPointsInsideOrOnBoundary(markPolygon, partPolygon))
            return ForeignPartOverlapKind.MarkInsideForeignPart;

        if (AllPointsInsideOrOnBoundary(partPolygon, markPolygon))
            return ForeignPartOverlapKind.ForeignPartInsideMark;

        return ForeignPartOverlapKind.PartialForeignPartOverlap;
    }

    private static bool AllPointsInsideOrOnBoundary(
        IReadOnlyList<double[]> points,
        IReadOnlyList<double[]> polygon)
    {
        foreach (var point in points)
        {
            if (!ContainsPointOrOnBoundary(polygon, point[0], point[1]))
                return false;
        }

        return true;
    }

    private static bool ContainsPointOrOnBoundary(
        IReadOnlyList<double[]> polygon,
        double x,
        double y)
    {
        if (PolygonGeometry.ContainsPoint(polygon, x, y))
            return true;

        for (var i = 0; i < polygon.Count; i++)
        {
            var current = polygon[i];
            var next = polygon[(i + 1) % polygon.Count];
            if (IsPointOnSegment(x, y, current[0], current[1], next[0], next[1]))
                return true;
        }

        return false;
    }

    private static bool IsPointOnSegment(
        double x,
        double y,
        double ax,
        double ay,
        double bx,
        double by)
    {
        const double epsilon = 1e-7;
        var cross = ((x - ax) * (by - ay)) - ((y - ay) * (bx - ax));
        if (Math.Abs(cross) > epsilon)
            return false;

        var dot = ((x - ax) * (bx - ax)) + ((y - ay) * (by - ay));
        if (dot < -epsilon)
            return false;

        var length2 = ((bx - ax) * (bx - ax)) + ((by - ay) * (by - ay));
        return dot <= length2 + epsilon;
    }

    private static List<double[]> BuildMarkPolygon(ForceDirectedMarkItem mark)
    {
        if (mark.LocalCorners.Count >= 3)
            return PolygonGeometry.Translate(mark.LocalCorners, mark.Cx, mark.Cy);

        var halfWidth = Math.Max(mark.Width, 0.0) * 0.5;
        var halfHeight = Math.Max(mark.Height, 0.0) * 0.5;
        if (halfWidth <= 0.0 || halfHeight <= 0.0)
            return new List<double[]>();

        return new List<double[]>
        {
            new[] { mark.Cx - halfWidth, mark.Cy - halfHeight },
            new[] { mark.Cx + halfWidth, mark.Cy - halfHeight },
            new[] { mark.Cx + halfWidth, mark.Cy + halfHeight },
            new[] { mark.Cx - halfWidth, mark.Cy + halfHeight }
        };
    }

    private static List<double[]> BuildPartPolygon(PartBbox part)
    {
        if (part.Polygon is { Count: >= 3 })
            return part.Polygon.Select(static p => new[] { p[0], p[1] }).ToList();

        if (part.MaxX <= part.MinX || part.MaxY <= part.MinY)
            return new List<double[]>();

        return new List<double[]>
        {
            new[] { part.MinX, part.MinY },
            new[] { part.MaxX, part.MinY },
            new[] { part.MaxX, part.MaxY },
            new[] { part.MinX, part.MaxY }
        };
    }
}
