using System.Collections.Generic;
using System.Linq;
using TeklaMcpServer.Api.Algorithms.Geometry;
using Tekla.Structures.Geometry3d;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class DimensionViewContextBuilder
{
    private readonly IDrawingPartGeometryApi _partGeometryApi;
    private readonly IDrawingBoltGeometryApi _boltGeometryApi;
    private readonly IDrawingGridApi _gridApi;

    public DimensionViewContextBuilder(
        IDrawingPartGeometryApi partGeometryApi,
        IDrawingBoltGeometryApi boltGeometryApi,
        IDrawingGridApi gridApi)
    {
        _partGeometryApi = partGeometryApi;
        _boltGeometryApi = boltGeometryApi;
        _gridApi = gridApi;
    }

    public DimensionViewContext Build(int viewId, double viewScale)
    {
        var context = new DimensionViewContext
        {
            ViewId = viewId,
            ViewScale = viewScale
        };

        var parts = _partGeometryApi.GetAllPartsGeometryInView(viewId) ?? [];
        foreach (var part in parts
                     .Where(static part => part != null)
                     .GroupBy(static part => part.ModelId)
                     .Select(static group => group.First())
                     .OrderBy(static part => part.ModelId))
        {
            if (!part.Success)
            {
                context.Warnings.Add($"part:{part.ModelId}:{part.Error ?? "geometry_unavailable"}");
                continue;
            }

            context.Parts.Add(part);
        }

        context.PartsBounds = BuildPartsBounds(context.Parts);
        context.PartsHull.AddRange(BuildPartsHull(context.Parts));

        var seenBoltIds = new HashSet<int>();
        foreach (var part in context.Parts.Where(static part => part.ModelId != 0))
        {
            var boltResult = _boltGeometryApi.GetPartBoltGeometryInView(viewId, part.ModelId);
            if (!boltResult.Success)
            {
                context.Warnings.Add($"bolt-part:{part.ModelId}:{boltResult.Error ?? "geometry_unavailable"}");
                continue;
            }

            foreach (var boltGroup in boltResult.BoltGroups
                         .Where(static bolt => bolt != null)
                         .OrderBy(static bolt => bolt.ModelId))
            {
                if (!seenBoltIds.Add(boltGroup.ModelId))
                    continue;

                context.Bolts.Add(boltGroup);
            }
        }

        var gridResult = _gridApi.GetGridAxes(viewId);
        if (!gridResult.Success)
        {
            context.Warnings.Add($"grid:{gridResult.Error ?? "unavailable"}");
            return context;
        }

        foreach (var gridId in gridResult.Axes
                     .Select(ResolveGridIdentifier)
                     .Where(static id => !string.IsNullOrWhiteSpace(id))
                     .Distinct(System.StringComparer.Ordinal)
                     .OrderBy(static id => id, System.StringComparer.Ordinal))
        {
            context.GridIds.Add(gridId);
        }

        return context;
    }

    private static DrawingBoundsInfo? BuildPartsBounds(IReadOnlyList<PartGeometryInViewResult> parts)
    {
        var bounds = parts
            .Where(HasBbox)
            .Select(static part => TeklaDrawingDimensionsApi.CreateBoundsInfo(
                part.BboxMin[0],
                part.BboxMin[1],
                part.BboxMax[0],
                part.BboxMax[1]));
        return TeklaDrawingDimensionsApi.CombineBounds(bounds);
    }

    private static List<DrawingPointInfo> BuildPartsHull(IReadOnlyList<PartGeometryInViewResult> parts)
    {
        var sourcePoints = new List<Point>();
        foreach (var part in parts)
        {
            AddPartHullSourcePoints(sourcePoints, part);
        }

        if (sourcePoints.Count == 0)
            return [];

        var hull = ConvexHull.Compute(sourcePoints).ToList();
        if (hull.Count == 0)
            return [];

        hull = TeklaDrawingDimensionsApi.SimplifyHull(hull);
        return hull
            .Select((point, index) => new DrawingPointInfo
            {
                X = point.X,
                Y = point.Y,
                Order = index
            })
            .ToList();
    }

    private static void AddPartHullSourcePoints(List<Point> points, PartGeometryInViewResult part)
    {
        var addedSolidVertices = false;
        foreach (var vertex in part.SolidVertices.Where(static vertex => vertex.Length >= 2))
        {
            points.Add(new Point(
                vertex[0],
                vertex[1],
                vertex.Length > 2 ? vertex[2] : 0.0));
            addedSolidVertices = true;
        }

        if (addedSolidVertices || !HasBbox(part))
            return;

        var minX = part.BboxMin[0];
        var minY = part.BboxMin[1];
        var maxX = part.BboxMax[0];
        var maxY = part.BboxMax[1];
        points.Add(new Point(minX, minY, 0));
        points.Add(new Point(minX, maxY, 0));
        points.Add(new Point(maxX, maxY, 0));
        points.Add(new Point(maxX, minY, 0));
    }

    private static bool HasBbox(PartGeometryInViewResult part) =>
        part.BboxMin.Length >= 2 && part.BboxMax.Length >= 2;

    private static string ResolveGridIdentifier(GridAxisInfo axis)
    {
        if (!string.IsNullOrWhiteSpace(axis.Guid))
            return axis.Guid!;

        return axis.Label ?? string.Empty;
    }
}
