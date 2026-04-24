using System;
using System.Collections.Generic;
using System.Linq;
using TeklaMcpServer.Api.Algorithms.Geometry;

namespace TeklaMcpServer.Api.Algorithms.Marks;

internal readonly struct ForeignPartOverlap
{
    public ForeignPartOverlap(int markId, int partModelId, double depth)
    {
        MarkId = markId;
        PartModelId = partModelId;
        Depth = depth;
    }

    public int MarkId { get; }
    public int PartModelId { get; }
    public double Depth { get; }
}

internal readonly struct ForeignPartOverlapSummary
{
    public ForeignPartOverlapSummary(IReadOnlyList<ForeignPartOverlap> overlaps)
    {
        Overlaps = overlaps;
        Conflicts = overlaps.Count;
        Severity = overlaps.Sum(static x => x.Depth);
    }

    public IReadOnlyList<ForeignPartOverlap> Overlaps { get; }
    public int Conflicts { get; }
    public double Severity { get; }
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

                overlaps.Add(new ForeignPartOverlap(mark.Id, part.ModelId, depth));
            }
        }

        return new ForeignPartOverlapSummary(overlaps);
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
