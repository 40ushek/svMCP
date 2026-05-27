using System.Collections.Generic;
using TeklaMcpServer.Api.Algorithms.Geometry;
using TeklaMcpServer.Api.Algorithms.Marks;

namespace TeklaMcpServer.Api.Drawing;

internal static class ForceDimensionBlockerBuilder
{
    internal static List<PartBbox> BuildSyntheticObstacles(
        IReadOnlyList<IReadOnlyList<double[]>> dimensionBlockerPolygons)
    {
        var result = new List<PartBbox>(dimensionBlockerPolygons.Count);
        for (var i = 0; i < dimensionBlockerPolygons.Count; i++)
        {
            var polygon = dimensionBlockerPolygons[i];
            if (polygon.Count < 3)
                continue;

            PolygonGeometry.GetBounds(polygon, out var minX, out var minY, out var maxX, out var maxY);
            if (maxX <= minX || maxY <= minY)
                continue;

            var syntheticId = -(i + 1);
            result.Add(new PartBbox(syntheticId, minX, minY, maxX, maxY, polygon));
        }

        return result;
    }
}
