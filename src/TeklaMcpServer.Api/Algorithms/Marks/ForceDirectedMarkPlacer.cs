using System;
using System.Collections.Generic;
using System.Linq;
using TeklaMcpServer.Api.Algorithms.Geometry;

namespace TeklaMcpServer.Api.Algorithms.Marks;

internal readonly struct PartBbox
{
    public PartBbox(int modelId, double minX, double minY, double maxX, double maxY)
    {
        ModelId = modelId;
        MinX = minX;
        MinY = minY;
        MaxX = maxX;
        MaxY = maxY;
    }

    public int ModelId { get; }
    public double MinX { get; }
    public double MinY { get; }
    public double MaxX { get; }
    public double MaxY { get; }
}

internal sealed class ForceDirectedMarkPlacer
{
    private const double KAttract = 0.02;
    private const double KRepelPart = 1.5;
    private const double KRepelMark = 1.0;
    private const double MarkGapMm = 2.0;
    private const double MaxAttract = 12.0;
    private const double DeadzoneMm = 1.0;
    private const double PartRepelRadius = 60.0;
    private const double InitialDt = 2.0;
    private const double DtDecay = 0.995;
    private const double StopEpsilon = 0.05;
    private const int MaxIterations = 50;

    // Incremental mode: keep the current placement as the force solver start state.
    public void PlaceInitial(IReadOnlyList<ForceDirectedMarkItem> items)
    {
        _ = items;
    }

    public int Relax(
        IReadOnlyList<ForceDirectedMarkItem> items,
        IReadOnlyList<PartBbox> allParts,
        bool includeMarkRepulsion = false,
        ISet<int>? movableIds = null,
        Action<ForceIterationDebugInfo>? debugSink = null)
    {
        var dt = InitialDt;
        var iterationsUsed = 0;
        for (var iter = 0; iter < MaxIterations; iter++)
        {
            iterationsUsed = iter + 1;
            var totalDisplacement = 0.0;
            var updates = new List<ForceIterationUpdate>(items.Count);

            foreach (var mark in items)
            {
                if (!mark.CanMove) continue;
                if (movableIds != null && !movableIds.Contains(mark.Id)) continue;

                var debug = ComputeForce(mark, items, allParts, includeMarkRepulsion);

                var dx = debug.Fx * dt;
                var dy = debug.Fy * dt;
                updates.Add(new ForceIterationUpdate(mark, debug, dx, dy));
            }

            foreach (var update in updates)
            {
                update.Mark.Cx += update.Dx;
                update.Mark.Cy += update.Dy;
                totalDisplacement += Math.Sqrt((update.Dx * update.Dx) + (update.Dy * update.Dy));
                debugSink?.Invoke(new ForceIterationDebugInfo(
                    iter + 1,
                    update.Mark.Id,
                    update.Debug.AttractFx,
                    update.Debug.AttractFy,
                    update.Debug.PartRepelFx,
                    update.Debug.PartRepelFy,
                    update.Debug.MarkRepelFx,
                    update.Debug.MarkRepelFy,
                    update.Debug.Fx,
                    update.Debug.Fy,
                    update.Dx,
                    update.Dy,
                    update.Mark.Cx,
                    update.Mark.Cy));
            }

            dt *= DtDecay;
            if (totalDisplacement < StopEpsilon)
                break;
        }

        return iterationsUsed;
    }

    private static ForceComponents ComputeForce(
        ForceDirectedMarkItem mark,
        IReadOnlyList<ForceDirectedMarkItem> allMarks,
        IReadOnlyList<PartBbox> allParts,
        bool includeMarkRepulsion)
    {
        var attractFx = 0.0;
        var attractFy = 0.0;
        var partRepelFx = 0.0;
        var partRepelFy = 0.0;
        var markRepelFx = 0.0;
        var markRepelFy = 0.0;

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
                    attractFx += Clamp(KAttract * dx, -MaxAttract, MaxAttract);
                    attractFy += Clamp(KAttract * dy, -MaxAttract, MaxAttract);
                }
            }
        }

        // Repulsion from foreign part bboxes.
        foreach (var part in allParts)
        {
            if (mark.OwnModelId.HasValue && part.ModelId == mark.OwnModelId.Value)
                continue;

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
                        partRepelFx += KRepelPart * ownDx / ownDist;
                        partRepelFy += KRepelPart * ownDy / ownDist;
                    }
                }
                continue;
            }

            if (dist > PartRepelRadius) continue;

            var force = KRepelPart / (dist * dist * dist);
            partRepelFx += force * dx;
            partRepelFy += force * dy;
        }

        if (includeMarkRepulsion)
        {
            foreach (var other in allMarks)
            {
                if (other.Id == mark.Id) continue;

                if (TryGetMarkRepulsion(mark, other, out var repelFx, out var repelFy))
                {
                    markRepelFx += repelFx;
                    markRepelFy += repelFy;
                }
            }
        }

        return new ForceComponents(
            attractFx,
            attractFy,
            partRepelFx,
            partRepelFy,
            markRepelFx,
            markRepelFy);
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

    private static double Clamp(double value, double min, double max) =>
        Math.Max(min, Math.Min(max, value));

    private static bool TryGetMarkRepulsion(
        ForceDirectedMarkItem mark,
        ForceDirectedMarkItem other,
        out double repelFx,
        out double repelFy)
    {
        repelFx = 0.0;
        repelFy = 0.0;

        if (mark.LocalCorners.Count >= 3 && other.LocalCorners.Count >= 3)
        {
            var markPolygon = PolygonGeometry.Translate(mark.LocalCorners, mark.Cx, mark.Cy);
            var otherPolygon = PolygonGeometry.Translate(other.LocalCorners, other.Cx, other.Cy);
            if (!PolygonGeometry.TryGetMinimumTranslationVector(markPolygon, otherPolygon, out var axisX, out var axisY, out var depth))
                return false;

            var targetSeparation = depth + MarkGapMm;
            repelFx = -axisX * targetSeparation * KRepelMark;
            repelFy = -axisY * targetSeparation * KRepelMark;
            return true;
        }

        var ox = OverlapX(mark, other);
        var oy = OverlapY(mark, other);
        if (ox <= 0.0 || oy <= 0.0)
            return false;

        if (ox < oy)
            repelFx = (mark.Cx >= other.Cx ? 1.0 : -1.0) * KRepelMark * (ox + MarkGapMm);
        else
            repelFy = (mark.Cy >= other.Cy ? 1.0 : -1.0) * KRepelMark * (oy + MarkGapMm);

        return true;
    }

    private static double OverlapX(ForceDirectedMarkItem a, ForceDirectedMarkItem b) =>
        Math.Max(0.0, (a.Width + b.Width) * 0.5 - Math.Abs(a.Cx - b.Cx));

    private static double OverlapY(ForceDirectedMarkItem a, ForceDirectedMarkItem b) =>
        Math.Max(0.0, (a.Height + b.Height) * 0.5 - Math.Abs(a.Cy - b.Cy));
}

internal readonly struct ForceIterationDebugInfo
{
    public ForceIterationDebugInfo(
        int iteration,
        int markId,
        double attractFx,
        double attractFy,
        double partRepelFx,
        double partRepelFy,
        double markRepelFx,
        double markRepelFy,
        double fx,
        double fy,
        double dx,
        double dy,
        double x,
        double y)
    {
        Iteration = iteration;
        MarkId = markId;
        AttractFx = attractFx;
        AttractFy = attractFy;
        PartRepelFx = partRepelFx;
        PartRepelFy = partRepelFy;
        MarkRepelFx = markRepelFx;
        MarkRepelFy = markRepelFy;
        Fx = fx;
        Fy = fy;
        Dx = dx;
        Dy = dy;
        X = x;
        Y = y;
    }

    public int Iteration { get; }
    public int MarkId { get; }
    public double AttractFx { get; }
    public double AttractFy { get; }
    public double PartRepelFx { get; }
    public double PartRepelFy { get; }
    public double MarkRepelFx { get; }
    public double MarkRepelFy { get; }
    public double Fx { get; }
    public double Fy { get; }
    public double Dx { get; }
    public double Dy { get; }
    public double X { get; }
    public double Y { get; }
}

internal readonly struct ForceIterationUpdate
{
    public ForceIterationUpdate(ForceDirectedMarkItem mark, ForceComponents debug, double dx, double dy)
    {
        Mark = mark;
        Debug = debug;
        Dx = dx;
        Dy = dy;
    }

    public ForceDirectedMarkItem Mark { get; }
    public ForceComponents Debug { get; }
    public double Dx { get; }
    public double Dy { get; }
}

internal readonly struct ForceComponents
{
    public ForceComponents(
        double attractFx,
        double attractFy,
        double partRepelFx,
        double partRepelFy,
        double markRepelFx,
        double markRepelFy)
    {
        AttractFx = attractFx;
        AttractFy = attractFy;
        PartRepelFx = partRepelFx;
        PartRepelFy = partRepelFy;
        MarkRepelFx = markRepelFx;
        MarkRepelFy = markRepelFy;
    }

    public double AttractFx { get; }
    public double AttractFy { get; }
    public double PartRepelFx { get; }
    public double PartRepelFy { get; }
    public double MarkRepelFx { get; }
    public double MarkRepelFy { get; }
    public double Fx => AttractFx + PartRepelFx + MarkRepelFx;
    public double Fy => AttractFy + PartRepelFy + MarkRepelFy;
}
