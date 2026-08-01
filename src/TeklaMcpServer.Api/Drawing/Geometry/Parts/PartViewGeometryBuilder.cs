using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.Geometry3d;
using TeklaMcpServer.Api.Algorithms.Geometry;

namespace TeklaMcpServer.Api.Drawing;

internal static class PartViewGeometryBuilder
{
    public static List<double[]> BuildHull(IEnumerable<double[]> vertices)
    {
        var points = vertices
            .Where(static vertex => vertex.Length >= 2)
            .Select(static vertex => new Point(
                vertex[0],
                vertex[1],
                vertex.Length > 2 ? vertex[2] : 0.0))
            .ToList();

        return ConvexHull.Compute(points)
            .Select(static point => new[] { point.X, point.Y })
            .ToList();
    }
}
