using System;
using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Model;

namespace TeklaMcpServer.Api.Drawing;

public sealed class PartBlockerGeometryDebugInfo
{
    public int ModelId { get; set; }
    public List<double[]> Polygon { get; set; } = [];
}

/// <summary>
/// Public debug facade that returns the part polygons used by force-flow as obstacles.
/// Same path that production uses: <c>TeklaDrawingPartGeometryApi.GetAllPartsGeometryInView</c>
/// → <c>MarkSourceResolver.BuildPartPolygons</c> (convex hull of part view points).
/// Coordinates are returned as they come from the production source, without shortening conversion.
/// </summary>
public static class PartBlockerGeometryDebugReader
{
    public static List<PartBlockerGeometryDebugInfo> Collect(View view)
    {
        if (view == null)
            throw new ArgumentNullException(nameof(view));

        var viewId = view.GetIdentifier().ID;
        var model = new Model();
        var partsApi = new TeklaDrawingPartGeometryApi(model);
        var parts = partsApi.GetAllPartsGeometryInView(viewId) ?? [];
        var productionParts = parts
            .Where(static part => part != null)
            .GroupBy(static part => part.ModelId)
            .Select(static group => group.First())
            .OrderBy(static part => part.ModelId)
            .ToList();
        var polygons = MarkSourceResolver.BuildPartPolygons(productionParts);

        var result = new List<PartBlockerGeometryDebugInfo>(polygons.Count);
        foreach (var pair in polygons)
        {
            result.Add(new PartBlockerGeometryDebugInfo
            {
                ModelId = pair.Key,
                Polygon = pair.Value
            });
        }

        return result;
    }
}
