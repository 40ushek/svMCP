using System;
using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Algorithms.Geometry;

public static class PolygonGeometry
{
    private const double Epsilon = 1e-9;

    public static bool Intersects(IReadOnlyList<double[]> first, IReadOnlyList<double[]> second)
    {
        if (first.Count < 3 || second.Count < 3)
            return false;

        return !HasSeparatingAxis(first, second) && !HasSeparatingAxis(second, first);
    }

    public static bool TryGetMinimumTranslationVector(
        IReadOnlyList<double[]> first,
        IReadOnlyList<double[]> second,
        out double axisX,
        out double axisY,
        out double depth)
    {
        axisX = 0.0;
        axisY = 0.0;
        depth = 0.0;

        if (first.Count < 3 || second.Count < 3)
            return false;

        var smallestOverlap = double.MaxValue;
        if (!TryAccumulateMinimumOverlapAxis(first, second, ref smallestOverlap, ref axisX, ref axisY) ||
            !TryAccumulateMinimumOverlapAxis(second, first, ref smallestOverlap, ref axisX, ref axisY))
            return false;

        var centerDeltaX = GetCenterX(second) - GetCenterX(first);
        var centerDeltaY = GetCenterY(second) - GetCenterY(first);
        if (Dot(centerDeltaX, centerDeltaY, axisX, axisY) < 0)
        {
            axisX = -axisX;
            axisY = -axisY;
        }

        depth = smallestOverlap;
        return true;
    }

    public static List<double[]> Translate(
        IReadOnlyList<double[]> localCorners,
        double centerX,
        double centerY)
    {
        return localCorners
            .Select(c => new[] { centerX + c[0], centerY + c[1] })
            .ToList();
    }

    public static void GetBounds(
        IReadOnlyList<double[]> polygon,
        out double minX,
        out double minY,
        out double maxX,
        out double maxY)
    {
        minX = polygon[0][0];
        maxX = polygon[0][0];
        minY = polygon[0][1];
        maxY = polygon[0][1];

        foreach (var point in polygon.Skip(1))
        {
            if (point[0] < minX) minX = point[0];
            if (point[0] > maxX) maxX = point[0];
            if (point[1] < minY) minY = point[1];
            if (point[1] > maxY) maxY = point[1];
        }
    }

    public static bool RectanglesOverlap(
        double firstMinX,
        double firstMinY,
        double firstMaxX,
        double firstMaxY,
        double secondMinX,
        double secondMinY,
        double secondMaxX,
        double secondMaxY)
    {
        return firstMaxX > secondMinX &&
               secondMaxX > firstMinX &&
               firstMaxY > secondMinY &&
               secondMaxY > firstMinY;
    }

    public static bool ContainsPoint(
        IReadOnlyList<double[]> polygon,
        double x,
        double y)
    {
        if (polygon.Count < 3)
            return false;

        var inside = false;
        for (var i = 0; i < polygon.Count; i++)
        {
            var current = polygon[i];
            var next = polygon[(i + 1) % polygon.Count];

            var intersects = ((current[1] > y) != (next[1] > y)) &&
                             (x < (((next[0] - current[0]) * (y - current[1])) / ((next[1] - current[1]) + Epsilon)) + current[0]);
            if (intersects)
                inside = !inside;
        }

        return inside;
    }

    private static bool HasSeparatingAxis(IReadOnlyList<double[]> polygonA, IReadOnlyList<double[]> polygonB)
    {
        for (var i = 0; i < polygonA.Count; i++)
        {
            var current = polygonA[i];
            var next = polygonA[(i + 1) % polygonA.Count];
            var edgeX = next[0] - current[0];
            var edgeY = next[1] - current[1];

            if (Math.Abs(edgeX) < Epsilon && Math.Abs(edgeY) < Epsilon)
                continue;

            var axisX = -edgeY;
            var axisY = edgeX;
            ProjectPolygon(polygonA, axisX, axisY, out var aMin, out var aMax);
            ProjectPolygon(polygonB, axisX, axisY, out var bMin, out var bMax);

            if (aMax <= bMin + Epsilon || bMax <= aMin + Epsilon)
                return true;
        }

        return false;
    }

    private static bool TryAccumulateMinimumOverlapAxis(
        IReadOnlyList<double[]> polygonA,
        IReadOnlyList<double[]> polygonB,
        ref double smallestOverlap,
        ref double axisX,
        ref double axisY)
    {
        for (var i = 0; i < polygonA.Count; i++)
        {
            var current = polygonA[i];
            var next = polygonA[(i + 1) % polygonA.Count];
            var edgeX = next[0] - current[0];
            var edgeY = next[1] - current[1];

            if (Math.Abs(edgeX) < Epsilon && Math.Abs(edgeY) < Epsilon)
                continue;

            var candidateAxisX = -edgeY;
            var candidateAxisY = edgeX;
            var axisLength = Math.Sqrt((candidateAxisX * candidateAxisX) + (candidateAxisY * candidateAxisY));
            if (axisLength < Epsilon)
                continue;

            candidateAxisX /= axisLength;
            candidateAxisY /= axisLength;

            ProjectPolygon(polygonA, candidateAxisX, candidateAxisY, out var aMin, out var aMax);
            ProjectPolygon(polygonB, candidateAxisX, candidateAxisY, out var bMin, out var bMax);

            var overlap = Math.Min(aMax, bMax) - Math.Max(aMin, bMin);
            if (overlap <= Epsilon)
                return false;

            if (overlap < smallestOverlap)
            {
                smallestOverlap = overlap;
                axisX = candidateAxisX;
                axisY = candidateAxisY;
            }
        }

        return true;
    }

    private static void ProjectPolygon(
        IReadOnlyList<double[]> polygon,
        double axisX,
        double axisY,
        out double min,
        out double max)
    {
        var firstProjection = Dot(polygon[0][0], polygon[0][1], axisX, axisY);
        min = firstProjection;
        max = firstProjection;

        foreach (var point in polygon.Skip(1))
        {
            var projection = Dot(point[0], point[1], axisX, axisY);
            if (projection < min)
                min = projection;
            if (projection > max)
                max = projection;
        }
    }

    private static double GetCenterX(IReadOnlyList<double[]> polygon) => polygon.Average(point => point[0]);

    private static double GetCenterY(IReadOnlyList<double[]> polygon) => polygon.Average(point => point[1]);

    private static double Dot(double x, double y, double axisX, double axisY) => (x * axisX) + (y * axisY);
}
