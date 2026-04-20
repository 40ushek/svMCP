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

    public static bool TryGetPolygonGapVector(
        IReadOnlyList<double[]> first,
        IReadOnlyList<double[]> second,
        out double axisX,
        out double axisY,
        out double gap)
    {
        axisX = 0.0;
        axisY = 0.0;
        gap = 0.0;

        if (first.Count < 3 || second.Count < 3)
            return false;

        if (Intersects(first, second))
            return false;

        var bestDistance2 = double.MaxValue;
        var bestDx = 0.0;
        var bestDy = 0.0;

        for (var i = 0; i < first.Count; i++)
        {
            var a1 = first[i];
            var a2 = first[(i + 1) % first.Count];

            for (var j = 0; j < second.Count; j++)
            {
                var b1 = second[j];
                var b2 = second[(j + 1) % second.Count];
                var (firstX, firstY, secondX, secondY) = GetClosestPointsBetweenSegments(
                    a1[0], a1[1], a2[0], a2[1],
                    b1[0], b1[1], b2[0], b2[1]);

                var dx = secondX - firstX;
                var dy = secondY - firstY;
                var distance2 = (dx * dx) + (dy * dy);
                if (distance2 < bestDistance2)
                {
                    bestDistance2 = distance2;
                    bestDx = dx;
                    bestDy = dy;
                }
            }
        }

        if (bestDistance2 <= Epsilon * Epsilon)
            return false;

        gap = Math.Sqrt(bestDistance2);
        axisX = bestDx / gap;
        axisY = bestDy / gap;
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
                             (x < (((next[0] - current[0]) * (y - current[1])) / (next[1] - current[1])) + current[0]);
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

    private static (double ax, double ay, double bx, double by) GetClosestPointsBetweenSegments(
        double p1x,
        double p1y,
        double q1x,
        double q1y,
        double p2x,
        double p2y,
        double q2x,
        double q2y)
    {
        var d1x = q1x - p1x;
        var d1y = q1y - p1y;
        var d2x = q2x - p2x;
        var d2y = q2y - p2y;
        var rx = p1x - p2x;
        var ry = p1y - p2y;

        var a = (d1x * d1x) + (d1y * d1y);
        var e = (d2x * d2x) + (d2y * d2y);
        var f = (d2x * rx) + (d2y * ry);

        double s;
        double t;

        if (a <= Epsilon && e <= Epsilon)
            return (p1x, p1y, p2x, p2y);

        if (a <= Epsilon)
        {
            s = 0.0;
            t = Clamp01(f / e);
        }
        else
        {
            var c = (d1x * rx) + (d1y * ry);
            if (e <= Epsilon)
            {
                t = 0.0;
                s = Clamp01(-c / a);
            }
            else
            {
                var b = (d1x * d2x) + (d1y * d2y);
                var denom = (a * e) - (b * b);

                if (Math.Abs(denom) > Epsilon)
                    s = Clamp01(((b * f) - (c * e)) / denom);
                else
                    s = 0.0;

                t = ((b * s) + f) / e;

                if (t < 0.0)
                {
                    t = 0.0;
                    s = Clamp01(-c / a);
                }
                else if (t > 1.0)
                {
                    t = 1.0;
                    s = Clamp01((b - c) / a);
                }
            }
        }

        return (
            p1x + (d1x * s),
            p1y + (d1y * s),
            p2x + (d2x * t),
            p2y + (d2y * t));
    }

    private static double Clamp01(double value) => Math.Max(0.0, Math.Min(1.0, value));

    private static double Dot(double x, double y, double axisX, double axisY) => (x * axisX) + (y * axisY);
}
