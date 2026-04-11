using System;
using System.Collections.Generic;
using System.Linq;
using TeklaMcpServer.Api.Algorithms.Geometry;

namespace TeklaMcpServer.Api.Algorithms.Marks;

internal static class LeaderAnchorResolver
{
    private const double MinDepthMm = 0.5;
    private const double CornerClearanceMm = 2.0;
    private const double IntersectionEpsilon = 0.001;

    internal static bool TryResolveAnchorTarget(
        IReadOnlyList<double[]> polygon,
        double bodyCenterX,
        double bodyCenterY,
        double depthMm,
        double minFarEdgeClearanceMm,
        out double anchorX,
        out double anchorY)
    {
        anchorX = 0.0;
        anchorY = 0.0;

        if (polygon.Count < 3 || depthMm < MinDepthMm)
            return false;

        if (!TryFindNearestEdgeHit(polygon, bodyCenterX, bodyCenterY, out var hit))
            return false;

        var edgeDx = hit.EndX - hit.StartX;
        var edgeDy = hit.EndY - hit.StartY;
        var edgeLength = Math.Sqrt((edgeDx * edgeDx) + (edgeDy * edgeDy));
        if (edgeLength < 0.000001)
            return false;

        var safeT = ClampAwayFromCorners(hit.T, edgeLength);
        var baseX = hit.StartX + (edgeDx * safeT);
        var baseY = hit.StartY + (edgeDy * safeT);
        var unitEdgeX = edgeDx / edgeLength;
        var unitEdgeY = edgeDy / edgeLength;

        if (!TryResolveInwardNormal(polygon, baseX, baseY, unitEdgeX, unitEdgeY, depthMm, out var inwardNx, out var inwardNy))
            return false;

        if (!TryResolveAvailableDepth(polygon, baseX, baseY, inwardNx, inwardNy, out var availableDepth))
            return false;

        var maxOffset = availableDepth - Math.Max(minFarEdgeClearanceMm, 0.0);
        var offset = maxOffset >= MinDepthMm
            ? Math.Min(depthMm, maxOffset)
            : Math.Min(depthMm, availableDepth * 0.5);

        while (offset >= MinDepthMm)
        {
            var candidateX = baseX + (inwardNx * offset);
            var candidateY = baseY + (inwardNy * offset);
            if (PolygonGeometry.ContainsPoint(polygon, candidateX, candidateY))
            {
                anchorX = candidateX;
                anchorY = candidateY;
                return true;
            }

            offset *= 0.5;
        }

        return false;
    }

    private static bool TryResolveAvailableDepth(
        IReadOnlyList<double[]> polygon,
        double originX,
        double originY,
        double directionX,
        double directionY,
        out double availableDepth)
    {
        availableDepth = double.MaxValue;

        for (var i = 0; i < polygon.Count; i++)
        {
            var start = polygon[i];
            var end = polygon[(i + 1) % polygon.Count];
            if (!TryIntersectRayWithSegment(
                    originX,
                    originY,
                    directionX,
                    directionY,
                    start[0],
                    start[1],
                    end[0],
                    end[1],
                    out var distance))
            {
                continue;
            }

            if (distance < availableDepth)
                availableDepth = distance;
        }

        return availableDepth < double.MaxValue;
    }

    private static bool TryFindNearestEdgeHit(
        IReadOnlyList<double[]> polygon,
        double x,
        double y,
        out EdgeHit hit)
    {
        hit = default;
        var bestDistanceSquared = double.MaxValue;

        for (var i = 0; i < polygon.Count; i++)
        {
            var start = polygon[i];
            var end = polygon[(i + 1) % polygon.Count];
            var point = GetNearestPointOnSegment(x, y, start[0], start[1], end[0], end[1], out var t);
            var dx = x - point.X;
            var dy = y - point.Y;
            var distanceSquared = (dx * dx) + (dy * dy);
            if (distanceSquared >= bestDistanceSquared)
                continue;

            bestDistanceSquared = distanceSquared;
            hit = new EdgeHit(start[0], start[1], end[0], end[1], point.X, point.Y, t);
        }

        return bestDistanceSquared < double.MaxValue;
    }

    private static double ClampAwayFromCorners(double t, double edgeLength)
    {
        var clearance = Math.Min(CornerClearanceMm, edgeLength * 0.25);
        if (clearance <= 0.0 || edgeLength < 0.000001)
            return t;

        var minT = clearance / edgeLength;
        var maxT = 1.0 - minT;
        if (minT >= maxT)
            return 0.5;

        return Math.Max(minT, Math.Min(maxT, t));
    }

    private static bool TryResolveInwardNormal(
        IReadOnlyList<double[]> polygon,
        double x,
        double y,
        double edgeUnitX,
        double edgeUnitY,
        double depthMm,
        out double normalX,
        out double normalY)
    {
        normalX = 0.0;
        normalY = 0.0;

        var firstNormalX = -edgeUnitY;
        var firstNormalY = edgeUnitX;
        var secondNormalX = edgeUnitY;
        var secondNormalY = -edgeUnitX;
        var centroidX = polygon.Average(static point => point[0]);
        var centroidY = polygon.Average(static point => point[1]);

        var probe = Math.Min(depthMm, 1.0);
        while (probe >= MinDepthMm)
        {
            var firstInside = PolygonGeometry.ContainsPoint(
                polygon,
                x + (firstNormalX * probe),
                y + (firstNormalY * probe));
            var secondInside = PolygonGeometry.ContainsPoint(
                polygon,
                x + (secondNormalX * probe),
                y + (secondNormalY * probe));

            if (firstInside && !secondInside)
            {
                normalX = firstNormalX;
                normalY = firstNormalY;
                return true;
            }

            if (secondInside && !firstInside)
            {
                normalX = secondNormalX;
                normalY = secondNormalY;
                return true;
            }

            if (firstInside && secondInside)
            {
                var toCentroidX = centroidX - x;
                var toCentroidY = centroidY - y;
                var firstDot = (firstNormalX * toCentroidX) + (firstNormalY * toCentroidY);
                var secondDot = (secondNormalX * toCentroidX) + (secondNormalY * toCentroidY);
                if (firstDot >= secondDot)
                {
                    normalX = firstNormalX;
                    normalY = firstNormalY;
                }
                else
                {
                    normalX = secondNormalX;
                    normalY = secondNormalY;
                }

                return true;
            }

            probe *= 0.5;
        }

        return false;
    }

    private static (double X, double Y) GetNearestPointOnSegment(
        double px,
        double py,
        double ax,
        double ay,
        double bx,
        double by,
        out double t)
    {
        var abx = bx - ax;
        var aby = by - ay;
        var lengthSquared = (abx * abx) + (aby * aby);
        if (lengthSquared < 0.000001)
        {
            t = 0.0;
            return (ax, ay);
        }

        t = (((px - ax) * abx) + ((py - ay) * aby)) / lengthSquared;
        t = Math.Max(0.0, Math.Min(1.0, t));
        return (ax + (abx * t), ay + (aby * t));
    }

    private static bool TryIntersectRayWithSegment(
        double rayOriginX,
        double rayOriginY,
        double rayDirectionX,
        double rayDirectionY,
        double segmentStartX,
        double segmentStartY,
        double segmentEndX,
        double segmentEndY,
        out double distance)
    {
        distance = 0.0;

        var segmentDx = segmentEndX - segmentStartX;
        var segmentDy = segmentEndY - segmentStartY;
        var denominator = Cross(rayDirectionX, rayDirectionY, segmentDx, segmentDy);
        if (Math.Abs(denominator) < 0.000001)
            return false;

        var diffX = segmentStartX - rayOriginX;
        var diffY = segmentStartY - rayOriginY;
        var rayT = Cross(diffX, diffY, segmentDx, segmentDy) / denominator;
        var segmentT = Cross(diffX, diffY, rayDirectionX, rayDirectionY) / denominator;
        if (rayT <= IntersectionEpsilon || segmentT < -IntersectionEpsilon || segmentT > 1.0 + IntersectionEpsilon)
            return false;

        distance = rayT;
        return true;
    }

    private static double Cross(double ax, double ay, double bx, double by) => (ax * by) - (ay * bx);

    private readonly struct EdgeHit
    {
        public EdgeHit(
            double startX,
            double startY,
            double endX,
            double endY,
            double pointX,
            double pointY,
            double t)
        {
            StartX = startX;
            StartY = startY;
            EndX = endX;
            EndY = endY;
            PointX = pointX;
            PointY = pointY;
            T = t;
        }

        public double StartX { get; }

        public double StartY { get; }

        public double EndX { get; }

        public double EndY { get; }

        public double PointX { get; }

        public double PointY { get; }

        public double T { get; }
    }
}
