using System;
using System.Collections.Generic;
using Tekla.Structures.Geometry3d;

namespace TeklaMcpServer.Api.Algorithms.Geometry;

public static class ConvexHull
{
    public static IReadOnlyList<Point> Compute(IEnumerable<Point> points)
    {
        if (points == null)
            throw new ArgumentNullException(nameof(points));

        var uniquePoints = GetUniquePoints(points);
        if (uniquePoints.Count <= 1)
            return uniquePoints;

        var pivotIndex = 0;
        for (var i = 1; i < uniquePoints.Count; i++)
        {
            if (CompareByYThenX(uniquePoints[i], uniquePoints[pivotIndex]) < 0)
                pivotIndex = i;
        }

        var pivot = uniquePoints[pivotIndex];
        uniquePoints.RemoveAt(pivotIndex);
        uniquePoints.Sort((left, right) => CompareByPolarAngle(pivot, left, right));

        var hull = new List<Point> { pivot };
        foreach (var point in uniquePoints)
        {
            while (hull.Count >= 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], point) <= 0)
                hull.RemoveAt(hull.Count - 1);

            hull.Add(point);
        }

        return hull;
    }

    private static List<Point> GetUniquePoints(IEnumerable<Point> points)
    {
        var result = new List<Point>();
        foreach (var point in points)
        {
            if (point == null)
                continue;

            var isDuplicate = false;
            for (var i = 0; i < result.Count; i++)
            {
                if (SameXY(result[i], point))
                {
                    isDuplicate = true;
                    break;
                }
            }

            if (!isDuplicate)
                result.Add(new Point(point.X, point.Y, point.Z));
        }

        return result;
    }

    private static int CompareByYThenX(Point left, Point right)
    {
        var byY = left.Y.CompareTo(right.Y);
        return byY != 0 ? byY : left.X.CompareTo(right.X);
    }

    private static int CompareByPolarAngle(Point pivot, Point left, Point right)
    {
        var cross = Cross(pivot, left, right);
        if (cross > 0)
            return -1;
        if (cross < 0)
            return 1;

        return DistanceSquared(pivot, left).CompareTo(DistanceSquared(pivot, right));
    }

    internal static double Cross(Point origin, Point left, Point right)
    {
        return (left.X - origin.X) * (right.Y - origin.Y)
            - (left.Y - origin.Y) * (right.X - origin.X);
    }

    internal static double DistanceSquared(Point left, Point right)
    {
        var dx = right.X - left.X;
        var dy = right.Y - left.Y;
        return dx * dx + dy * dy;
    }

    private static bool SameXY(Point left, Point right)
    {
        return left.X.Equals(right.X) && left.Y.Equals(right.Y);
    }
}
