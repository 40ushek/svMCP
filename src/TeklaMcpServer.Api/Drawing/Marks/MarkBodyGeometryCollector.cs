using System.Reflection;
using System.Runtime.CompilerServices;
using Tekla.Structures.Drawing;
using Tekla.Structures.Geometry3d;
using TeklaMcpServer.Api.Algorithms.Geometry;

namespace TeklaMcpServer.Api.Drawing;

internal static class MarkBodyGeometryCollector
{
    private const int MaxDepth = 4;
    private const double BoxEpsilon = 0.001;

    public static bool TryCollectBodyPolygon(Mark mark, out List<double[]> polygon)
    {
        polygon = [];
        var points = new List<Point>();
        var visited = new HashSet<int>();
        CollectFromChildren(mark.GetObjects(), points, visited, depth: 0);

        if (points.Count < 3)
            return false;

        var hull = ConvexHull.Compute(points);
        if (hull.Count < 3)
            return false;

        polygon = hull
            .Select(static p => new[] { p.X, p.Y })
            .ToList();
        return polygon.Count >= 3;
    }

    private static void CollectFromChildren(
        DrawingObjectEnumerator? enumerator,
        List<Point> points,
        HashSet<int> visited,
        int depth)
    {
        if (enumerator == null || depth > MaxDepth)
            return;

        while (enumerator.MoveNext())
            CollectFromObject(enumerator.Current, points, visited, depth);
    }

    private static void CollectFromObject(
        object? candidate,
        List<Point> points,
        HashSet<int> visited,
        int depth)
    {
        if (candidate == null || candidate is LeaderLine)
            return;

        var visitId = RuntimeHelpers.GetHashCode(candidate);
        if (!visited.Add(visitId))
            return;

        TryAppendObjectAlignedCorners(candidate, points);

        if (depth >= MaxDepth)
            return;

        try
        {
            var getObjectsMethod = candidate.GetType().GetMethod(
                "GetObjects",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);
            if (getObjectsMethod?.Invoke(candidate, null) is DrawingObjectEnumerator childEnumerator)
                CollectFromChildren(childEnumerator, points, visited, depth + 1);
        }
        catch
        {
            // Ignore child objects that do not expose recursive enumeration.
        }
    }

    private static void TryAppendObjectAlignedCorners(object candidate, List<Point> points)
    {
        try
        {
            var objectAlignedMethod = candidate.GetType().GetMethod(
                "GetObjectAlignedBoundingBox",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (objectAlignedMethod?.Invoke(candidate, null) is not RectangleBoundingBox box)
                return;

            if (box.Width < BoxEpsilon && box.Height < BoxEpsilon)
                return;

            points.Add(new Point(box.LowerLeft.X, box.LowerLeft.Y, 0));
            points.Add(new Point(box.UpperLeft.X, box.UpperLeft.Y, 0));
            points.Add(new Point(box.UpperRight.X, box.UpperRight.Y, 0));
            points.Add(new Point(box.LowerRight.X, box.LowerRight.Y, 0));
        }
        catch
        {
            // Ignore objects that do not expose a usable object-aligned box.
        }
    }
}
