using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class DimensionViewContextBuilder
{
    private readonly IDrawingPartGeometryApi _partGeometryApi;
    private readonly IDrawingBoltGeometryApi _boltGeometryApi;

    public DimensionViewContextBuilder(
        IDrawingPartGeometryApi partGeometryApi,
        IDrawingBoltGeometryApi boltGeometryApi)
    {
        _partGeometryApi = partGeometryApi;
        _boltGeometryApi = boltGeometryApi;
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

        return context;
    }
}
