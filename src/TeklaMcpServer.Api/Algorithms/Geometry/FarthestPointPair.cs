using System;
using System.Collections.Generic;
using Tekla.Structures.Geometry3d;

namespace TeklaMcpServer.Api.Algorithms.Geometry;

public static class FarthestPointPair
{
    public static FarthestPointPairResult Find(IEnumerable<Point> points)
    {
        if (points == null)
            throw new ArgumentNullException(nameof(points));

        var hull = ConvexHull.Compute(points);
        if (hull.Count == 0)
            throw new ArgumentException("At least one point is required.", nameof(points));

        if (hull.Count == 1)
            return new FarthestPointPairResult(hull[0], hull[0], 0);

        if (hull.Count == 2)
        {
            var distanceSquared = ConvexHull.DistanceSquared(hull[0], hull[1]);
            return new FarthestPointPairResult(hull[0], hull[1], distanceSquared);
        }

        var best = new FarthestPointPairResult(hull[0], hull[1], ConvexHull.DistanceSquared(hull[0], hull[1]));
        var antipodalIndex = 1;

        for (var i = 0; i < hull.Count; i++)
        {
            var nextI = (i + 1) % hull.Count;

            while (AreaTwice(hull[i], hull[nextI], hull[(antipodalIndex + 1) % hull.Count])
                 > AreaTwice(hull[i], hull[nextI], hull[antipodalIndex]))
            {
                antipodalIndex = (antipodalIndex + 1) % hull.Count;
            }

            best = Max(best, hull[i], hull[antipodalIndex]);
            best = Max(best, hull[nextI], hull[antipodalIndex]);
        }

        return best;
    }

    private static double AreaTwice(Point left, Point right, Point candidate)
    {
        return ConvexHull.Cross(left, right, candidate);
    }

    private static FarthestPointPairResult Max(FarthestPointPairResult current, Point left, Point right)
    {
        var distanceSquared = ConvexHull.DistanceSquared(left, right);
        if (distanceSquared <= current.DistanceSquared)
            return current;

        return new FarthestPointPairResult(left, right, distanceSquared);
    }
}
