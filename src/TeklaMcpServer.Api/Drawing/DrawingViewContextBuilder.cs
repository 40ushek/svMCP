using System.Collections.Generic;
using System.Linq;
using TeklaMcpServer.Api.Algorithms.Geometry;
using Tekla.Structures.Geometry3d;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class DrawingViewContextBuilder
{
    private readonly IDrawingPartGeometryApi _partGeometryApi;
    private readonly IDrawingBoltGeometryApi _boltGeometryApi;
    private readonly IDrawingGridApi _gridApi;

    public DrawingViewContextBuilder(
        IDrawingPartGeometryApi partGeometryApi,
        IDrawingBoltGeometryApi boltGeometryApi,
        IDrawingGridApi gridApi)
    {
        _partGeometryApi = partGeometryApi;
        _boltGeometryApi = boltGeometryApi;
        _gridApi = gridApi;
    }

    public DrawingViewContext Build(int viewId, double viewScale, string viewType)
    {
        var context = new DrawingViewContext
        {
            ViewId = viewId,
            ViewType = viewType ?? string.Empty,
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

    private static bool HasBbox(PartGeometryInViewResult part) =>
        part.BboxMin.Length >= 2 && part.BboxMax.Length >= 2;

    private static string ResolveGridIdentifier(GridAxisInfo axis)
    {
        if (!string.IsNullOrWhiteSpace(axis.Guid))
            return axis.Guid!;

        return axis.Label ?? string.Empty;
    }
}
