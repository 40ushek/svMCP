using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.Geometry3d;
using TeklaMcpServer.Api.Algorithms.Geometry;
using Tekla.Structures.Drawing;

namespace TeklaMcpServer.Api.Drawing;

internal enum MarkLayoutSourceKind
{
    Unknown = 0,
    Part = 1,
    Bolt = 2,
}

internal readonly struct MarkSourceReference
{
    public MarkSourceReference(MarkLayoutSourceKind kind, int? modelId)
    {
        Kind = kind;
        ModelId = modelId;
    }

    public MarkLayoutSourceKind Kind { get; }

    public int? ModelId { get; }

    public bool HasModelId => ModelId.HasValue && ModelId.Value > 0;
}

internal static class MarkSourceResolver
{
    public static MarkSourceReference Resolve(Mark mark)
    {
        var related = mark.GetRelatedObjects();
        while (related.MoveNext())
        {
            switch (related.Current)
            {
                case Part part:
                    return CreateReference(MarkLayoutSourceKind.Part, part.ModelIdentifier.ID);
                case Bolt bolt:
                    return CreateReference(MarkLayoutSourceKind.Bolt, bolt.ModelIdentifier.ID);
                case Tekla.Structures.Drawing.ModelObject modelObject:
                    return CreateReference(MarkLayoutSourceKind.Unknown, modelObject.ModelIdentifier.ID);
            }
        }

        return default;
    }

    public static bool TryResolveCenter(
        MarkSourceReference source,
        DrawingViewContext? viewContext,
        out double centerX,
        out double centerY)
    {
        centerX = 0.0;
        centerY = 0.0;

        if (viewContext == null || !source.HasModelId)
            return false;

        return source.Kind switch
        {
            MarkLayoutSourceKind.Part => TryResolvePartCenter(viewContext.Parts, source.ModelId!.Value, out centerX, out centerY),
            MarkLayoutSourceKind.Bolt => TryResolveBoltCenter(viewContext.Bolts, source.ModelId!.Value, out centerX, out centerY),
            _ => TryResolvePartCenter(viewContext.Parts, source.ModelId!.Value, out centerX, out centerY)
                 || TryResolveBoltCenter(viewContext.Bolts, source.ModelId!.Value, out centerX, out centerY)
        };
    }

    internal static bool TryResolvePartCenter(
        IReadOnlyList<PartGeometryInViewResult> parts,
        int modelId,
        out double centerX,
        out double centerY)
    {
        centerX = 0.0;
        centerY = 0.0;

        var part = parts.FirstOrDefault(candidate => candidate.Success && candidate.ModelId == modelId);
        if (part == null)
            return false;

        if (TryResolveBoundsCenter(part.BboxMin, part.BboxMax, out centerX, out centerY))
            return true;

        return TryResolveVertexBoundsCenter(part.SolidVertices, out centerX, out centerY);
    }

    internal static bool TryResolvePartPolygon(
        IReadOnlyList<PartGeometryInViewResult> parts,
        int modelId,
        out List<double[]> polygon)
    {
        polygon = [];

        var part = parts.FirstOrDefault(candidate => candidate.Success && candidate.ModelId == modelId);
        if (part == null)
            return false;

        var hullPoints = BuildPartHull(part);
        if (hullPoints.Count < 3)
            return false;

        polygon = hullPoints
            .Select(static point => new[] { point.X, point.Y })
            .ToList();
        return true;
    }

    internal static Dictionary<int, List<double[]>> BuildPartPolygons(IReadOnlyList<PartGeometryInViewResult> parts)
    {
        var result = new Dictionary<int, List<double[]>>();
        foreach (var part in parts.Where(static part => part.Success && part.ModelId > 0))
        {
            if (!TryResolvePartPolygon(part, out var polygon))
                continue;

            result[part.ModelId] = polygon;
        }

        return result;
    }

    internal static bool TryResolvePartPolygon(
        PartGeometryInViewResult part,
        out List<double[]> polygon)
    {
        polygon = [];

        if (!part.Success)
            return false;

        var hullPoints = BuildPartHull(part);
        if (hullPoints.Count < 3)
            return false;

        polygon = hullPoints
            .Select(static point => new[] { point.X, point.Y })
            .ToList();
        return true;
    }

    internal static bool TryResolveBoltCenter(
        IReadOnlyList<BoltGroupGeometry> bolts,
        int modelId,
        out double centerX,
        out double centerY)
    {
        centerX = 0.0;
        centerY = 0.0;

        var bolt = bolts.FirstOrDefault(candidate => candidate.ModelId == modelId);
        if (bolt == null)
            return false;

        if (TryResolveBoundsCenter(bolt.BboxMin, bolt.BboxMax, out centerX, out centerY))
            return true;

        if (bolt.Positions.Count == 0)
            return false;

        var sumX = 0.0;
        var sumY = 0.0;
        var count = 0;
        foreach (var position in bolt.Positions.Where(static position => position.Point.Length >= 2))
        {
            sumX += position.Point[0];
            sumY += position.Point[1];
            count++;
        }

        if (count == 0)
            return false;

        centerX = sumX / count;
        centerY = sumY / count;
        return true;
    }

    private static MarkSourceReference CreateReference(MarkLayoutSourceKind kind, int modelId) =>
        new(kind, modelId > 0 ? modelId : null);

    private static IReadOnlyList<Point> BuildPartHull(PartGeometryInViewResult part)
    {
        var sourcePoints = new List<Point>();
        foreach (var vertex in part.SolidVertices.Where(static vertex => vertex.Length >= 2))
            sourcePoints.Add(new Point(vertex[0], vertex[1], vertex.Length > 2 ? vertex[2] : 0.0));

        if (sourcePoints.Count == 0 && part.BboxMin.Length >= 2 && part.BboxMax.Length >= 2)
        {
            sourcePoints.Add(new Point(part.BboxMin[0], part.BboxMin[1], 0.0));
            sourcePoints.Add(new Point(part.BboxMin[0], part.BboxMax[1], 0.0));
            sourcePoints.Add(new Point(part.BboxMax[0], part.BboxMax[1], 0.0));
            sourcePoints.Add(new Point(part.BboxMax[0], part.BboxMin[1], 0.0));
        }

        return sourcePoints.Count == 0 ? [] : ConvexHull.Compute(sourcePoints);
    }

    private static bool TryResolveBoundsCenter(
        IReadOnlyList<double> min,
        IReadOnlyList<double> max,
        out double centerX,
        out double centerY)
    {
        centerX = 0.0;
        centerY = 0.0;

        if (min.Count < 2 || max.Count < 2)
            return false;

        centerX = (min[0] + max[0]) * 0.5;
        centerY = (min[1] + max[1]) * 0.5;
        return true;
    }

    private static bool TryResolveVertexBoundsCenter(
        IReadOnlyList<double[]> vertices,
        out double centerX,
        out double centerY)
    {
        centerX = 0.0;
        centerY = 0.0;

        var minX = double.MaxValue;
        var minY = double.MaxValue;
        var maxX = double.MinValue;
        var maxY = double.MinValue;
        var any = false;

        foreach (var vertex in vertices.Where(static vertex => vertex.Length >= 2))
        {
            minX = System.Math.Min(minX, vertex[0]);
            minY = System.Math.Min(minY, vertex[1]);
            maxX = System.Math.Max(maxX, vertex[0]);
            maxY = System.Math.Max(maxY, vertex[1]);
            any = true;
        }

        if (!any)
            return false;

        centerX = (minX + maxX) * 0.5;
        centerY = (minY + maxY) * 0.5;
        return true;
    }
}
