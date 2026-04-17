using System;
using System.Collections.Generic;
using System.Linq;
using TeklaMcpServer.Api.Algorithms.Geometry;

namespace TeklaMcpServer.Api.Algorithms.Marks;

internal readonly struct PartBbox
{
    public PartBbox(double minX, double minY, double maxX, double maxY)
    {
        MinX = minX;
        MinY = minY;
        MaxX = maxX;
        MaxY = maxY;
    }

    public double MinX { get; }
    public double MinY { get; }
    public double MaxX { get; }
    public double MaxY { get; }
}

internal sealed class ForceDirectedMarkPlacer
{
    private const double KAttract = 0.3;
    private const double KRepelPart = 1.5;
    private const double KRepelMark = 1.0;
    private const double DeadzoneMm = 8.0;
    private const double PartRepelRadius = 60.0;
    private const double InitialDt = 1.0;
    private const double DtDecay = 0.98;
    private const double StopEpsilon = 0.05;
    private const int MaxIterations = 80;

    // Incremental mode: keep the current placement as the force solver start state.
    public void PlaceInitial(IReadOnlyList<ForceDirectedMarkItem> items)
    {
        _ = items;
    }

    // Pass 2: force-directed relaxation with repulsion from parts and other marks
    public int Relax(IReadOnlyList<ForceDirectedMarkItem> items, IReadOnlyList<PartBbox> allParts)
    {
        var dt = InitialDt;
        var iterationsUsed = 0;
        for (var iter = 0; iter < MaxIterations; iter++)
        {
            iterationsUsed = iter + 1;
            var totalDisplacement = 0.0;

            foreach (var mark in items)
            {
                if (!mark.CanMove) continue;

                var (fx, fy) = ComputeForce(mark, items, allParts);

                var dx = fx * dt;
                var dy = fy * dt;
                mark.Cx += dx;
                mark.Cy += dy;
                totalDisplacement += Math.Sqrt((dx * dx) + (dy * dy));
            }

            dt *= DtDecay;
            if (totalDisplacement < StopEpsilon)
                break;
        }

        return iterationsUsed;
    }

    private static (double fx, double fy) ComputeForce(
        ForceDirectedMarkItem mark,
        IReadOnlyList<ForceDirectedMarkItem> allMarks,
        IReadOnlyList<PartBbox> allParts)
    {
        var fx = 0.0;
        var fy = 0.0;

        // Attraction to own part contour
        if (mark.OwnPolygon != null && mark.OwnPolygon.Count >= 2)
        {
            if (LeaderAnchorResolver.TryFindNearestEdgeHit(mark.OwnPolygon, mark.Cx, mark.Cy, out var hit))
            {
                var dx = hit.PointX - mark.Cx;
                var dy = hit.PointY - mark.Cy;
                var dist = Math.Sqrt((dx * dx) + (dy * dy));
                if (dist > DeadzoneMm)
                {
                    fx += KAttract * dx / dist;
                    fy += KAttract * dy / dist;
                }
            }
        }

        // Repulsion from all part bboxes (own included — keeps mark outside)
        foreach (var part in allParts)
        {
            var (nx, ny) = NearestOnBbox(mark.Cx, mark.Cy, part);
            var dx = mark.Cx - nx;
            var dy = mark.Cy - ny;
            var dist = Math.Sqrt((dx * dx) + (dy * dy));

            if (dist < 0.001)
            {
                // Inside/against part bbox — push away from own part centroid when available.
                if (TryGetPolygonCentroid(mark, out var centroidX, out var centroidY))
                {
                    var ownDx = mark.Cx - centroidX;
                    var ownDy = mark.Cy - centroidY;
                    var ownDist = Math.Sqrt((ownDx * ownDx) + (ownDy * ownDy));
                    if (ownDist > 0.001)
                    {
                        fx += KRepelPart * ownDx / ownDist;
                        fy += KRepelPart * ownDy / ownDist;
                    }
                }
                continue;
            }

            if (dist > PartRepelRadius) continue;

            var force = KRepelPart / (dist * dist * dist);
            fx += force * dx;
            fy += force * dy;
        }

        // Repulsion from other marks (collision only)
        foreach (var other in allMarks)
        {
            if (other.Id == mark.Id) continue;
            var ox = OverlapX(mark, other);
            var oy = OverlapY(mark, other);
            if (ox <= 0.0 || oy <= 0.0) continue;

            if (ox < oy)
            {
                fx += (mark.Cx >= other.Cx ? 1.0 : -1.0) * KRepelMark * ox;
            }
            else
            {
                fy += (mark.Cy >= other.Cy ? 1.0 : -1.0) * KRepelMark * oy;
            }
        }

        return (fx, fy);
    }

    private static bool TryGetPolygonCentroid(ForceDirectedMarkItem mark, out double centroidX, out double centroidY)
    {
        centroidX = 0.0;
        centroidY = 0.0;

        if (mark.OwnPolygon == null || mark.OwnPolygon.Count < 3)
            return false;

        centroidX = mark.OwnPolygon.Average(static point => point[0]);
        centroidY = mark.OwnPolygon.Average(static point => point[1]);
        return true;
    }

    private static (double x, double y) NearestOnBbox(double px, double py, PartBbox bbox)
    {
        var nx = Math.Max(bbox.MinX, Math.Min(px, bbox.MaxX));
        var ny = Math.Max(bbox.MinY, Math.Min(py, bbox.MaxY));
        return (nx, ny);
    }

    private static double OverlapX(ForceDirectedMarkItem a, ForceDirectedMarkItem b) =>
        Math.Max(0.0, (a.Width + b.Width) * 0.5 - Math.Abs(a.Cx - b.Cx));

    private static double OverlapY(ForceDirectedMarkItem a, ForceDirectedMarkItem b) =>
        Math.Max(0.0, (a.Height + b.Height) * 0.5 - Math.Abs(a.Cy - b.Cy));
}
