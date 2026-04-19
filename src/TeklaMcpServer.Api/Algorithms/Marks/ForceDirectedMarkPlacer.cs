using System;
using System.Collections.Generic;
using System.Linq;
using TeklaMcpServer.Api.Algorithms.Geometry;

namespace TeklaMcpServer.Api.Algorithms.Marks;

internal readonly struct PartBbox
{
    public PartBbox(int modelId, double minX, double minY, double maxX, double maxY, IReadOnlyList<double[]>? polygon = null)
    {
        ModelId = modelId;
        MinX = minX;
        MinY = minY;
        MaxX = maxX;
        MaxY = maxY;
        Polygon = polygon;
    }

    public int ModelId { get; }
    public double MinX { get; }
    public double MinY { get; }
    public double MaxX { get; }
    public double MaxY { get; }
    public IReadOnlyList<double[]>? Polygon { get; }
}

internal readonly struct ForcePassOptions
{
    public ForcePassOptions(
        double kAttract, double idealDist,
        double kRepelPart, double partRepelRadius, double partRepelSoftening,
        double kRepelMark, double markGapMm,
        double initialDt, double dtDecay, double stopEpsilon, int maxIterations)
    {
        KAttract = kAttract;
        IdealDist = idealDist;
        KRepelPart = kRepelPart;
        PartRepelRadius = partRepelRadius;
        PartRepelSoftening = partRepelSoftening;
        KRepelMark = kRepelMark;
        MarkGapMm = markGapMm;
        InitialDt = initialDt;
        DtDecay = dtDecay;
        StopEpsilon = stopEpsilon;
        MaxIterations = maxIterations;
    }

    public double KAttract { get; }
    /// <summary>Desired distance from mark center to own part surface. Attraction pulls to this distance, not to zero.</summary>
    public double IdealDist { get; }
    public double KRepelPart { get; }
    public double PartRepelRadius { get; }
    /// <summary>Softening epsilon for repulsion: force = KRepelPart / (dist² + ε²). Prevents singularity at dist→0.</summary>
    public double PartRepelSoftening { get; }
    public double KRepelMark { get; }
    public double MarkGapMm { get; }
    public double InitialDt { get; }
    public double DtDecay { get; }
    public double StopEpsilon { get; }
    public int MaxIterations { get; }

    // IdealDist=25, KRepel=300, KAttract=0.48 → sqrt(300/0.48)=25 ✓
    public static ForcePassOptions Pass1Default { get; } = new ForcePassOptions(
        kAttract: 0.48, idealDist: 25.0,
        kRepelPart: 300.0, partRepelRadius: 120.0, partRepelSoftening: 5.0,
        kRepelMark: 0.0, markGapMm: 2.0,
        initialDt: 1.0, dtDecay: 0.98, stopEpsilon: 0.05, maxIterations: 80);

    public static ForcePassOptions Pass2Default { get; } = new ForcePassOptions(
        kAttract: 0.48, idealDist: 25.0,
        kRepelPart: 300.0, partRepelRadius: 120.0, partRepelSoftening: 5.0,
        kRepelMark: 1.0, markGapMm: 2.0,
        initialDt: 1.0, dtDecay: 0.98, stopEpsilon: 0.05, maxIterations: 80);
}

internal sealed class ForceDirectedMarkPlacer
{
    public void PlaceInitial(IReadOnlyList<ForceDirectedMarkItem> items)
    {
        _ = items;
    }

    public int Relax(
        IReadOnlyList<ForceDirectedMarkItem> items,
        IReadOnlyList<PartBbox> allParts,
        ForcePassOptions options,
        bool includeMarkRepulsion = false,
        ISet<int>? movableIds = null,
        Action<ForceIterationDebugInfo>? debugSink = null)
    {
        var dt = options.InitialDt;
        var iterationsUsed = 0;
        for (var iter = 0; iter < options.MaxIterations; iter++)
        {
            iterationsUsed = iter + 1;
            var totalDisplacement = 0.0;
            var updates = new List<ForceIterationUpdate>(items.Count);

            foreach (var mark in items)
            {
                if (!mark.CanMove) continue;
                if (movableIds != null && !movableIds.Contains(mark.Id)) continue;

                var debug = ComputeForce(mark, items, allParts, options, includeMarkRepulsion);

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

            dt *= options.DtDecay;
            if (totalDisplacement < options.StopEpsilon)
                break;
        }

        return iterationsUsed;
    }

    private static ForceComponents ComputeForce(
        ForceDirectedMarkItem mark,
        IReadOnlyList<ForceDirectedMarkItem> allMarks,
        IReadOnlyList<PartBbox> allParts,
        ForcePassOptions options,
        bool includeMarkRepulsion)
    {
        var attractFx = 0.0;
        var attractFy = 0.0;
        var partRepelFx = 0.0;
        var partRepelFy = 0.0;
        var markRepelFx = 0.0;
        var markRepelFy = 0.0;

        if (mark.OwnPolygon != null && mark.OwnPolygon.Count >= 2)
        {
            if (mark.ConstrainToAxis && (Math.Abs(mark.AxisDx) > 0.001 || Math.Abs(mark.AxisDy) > 0.001))
            {
                // Axis-constrained marks: leash along axis.
                // No force while mark projection is within [polyMin, polyMax].
                // Outside: Hooke spring pulls mark back to the nearest bound.
                var markAxPos = mark.Cx * mark.AxisDx + mark.Cy * mark.AxisDy;
                var polyMin = double.MaxValue;
                var polyMax = double.MinValue;
                foreach (var pt in mark.OwnPolygon)
                {
                    var proj = pt[0] * mark.AxisDx + pt[1] * mark.AxisDy;
                    if (proj < polyMin) polyMin = proj;
                    if (proj > polyMax) polyMax = proj;
                }

                double axisForce;
                if (markAxPos < polyMin)
                    axisForce = options.KAttract * (polyMin - markAxPos);
                else if (markAxPos > polyMax)
                    axisForce = options.KAttract * (polyMax - markAxPos);
                else
                    axisForce = 0.0;

                attractFx += axisForce * mark.AxisDx;
                attractFy += axisForce * mark.AxisDy;
            }
            else
            {
                // Leader-line and free marks: logarithmic spring to nearest part surface.
                // Equilibrium at IdealDist; positive when farther, negative when closer.
                if (LeaderAnchorResolver.TryFindNearestEdgeHit(mark.OwnPolygon, mark.Cx, mark.Cy, out var hit))
                {
                    var dx = hit.PointX - mark.Cx;
                    var dy = hit.PointY - mark.Cy;
                    var dist = Math.Max(Math.Sqrt((dx * dx) + (dy * dy)), 0.001);
                    var springF = options.KAttract * Math.Log(dist / options.IdealDist);
                    if (IsInsidePolygon(mark.Cx, mark.Cy, mark.OwnPolygon))
                        springF = -springF;
                    attractFx += springF * (dx / dist);
                    attractFy += springF * (dy / dist);
                }
            }
        }

        // Repulsion from foreign part bboxes.
        foreach (var part in allParts)
        {
            if (mark.OwnModelId.HasValue && part.ModelId == mark.OwnModelId.Value)
                continue;

            var isInsidePart = part.Polygon != null &&
                               part.Polygon.Count >= 3 &&
                               IsInsidePolygon(mark.Cx, mark.Cy, part.Polygon);
            var (nx, ny) = NearestOnShape(mark.Cx, mark.Cy, part);
            var dx = mark.Cx - nx;
            var dy = mark.Cy - ny;
            var dist = Math.Sqrt((dx * dx) + (dy * dy));

            if (isInsidePart)
            {
                var penetration = Math.Max(dist, 0.001);
                var insideForce = options.KRepelPart * (penetration / Math.Max(options.PartRepelSoftening, 0.001));
                partRepelFx += insideForce * (nx - mark.Cx) / penetration;
                partRepelFy += insideForce * (ny - mark.Cy) / penetration;
                continue;
            }

            if (dist > options.PartRepelRadius) continue;

            // Inverse-square repulsion with softening: force = KRepelPart / (dist² + ε²)
            var softDist2 = dist * dist + options.PartRepelSoftening * options.PartRepelSoftening;
            var force = options.KRepelPart / softDist2;
            if (dist < 0.001)
            {
                // Inside foreign shape — push away from the foreign shape centroid.
                if (TryGetPartCentroid(part, out var centroidX, out var centroidY))
                {
                    var partDx = mark.Cx - centroidX;
                    var partDy = mark.Cy - centroidY;
                    var partDist = Math.Max(Math.Sqrt((partDx * partDx) + (partDy * partDy)), 0.001);
                    partRepelFx += force * partDx / partDist;
                    partRepelFy += force * partDy / partDist;
                }
                continue;
            }

            partRepelFx += force * dx / dist;
            partRepelFy += force * dy / dist;
        }

        if (includeMarkRepulsion)
        {
            foreach (var other in allMarks)
            {
                if (other.Id == mark.Id) continue;

                if (TryGetMarkRepulsion(mark, other, options, out var repelFx, out var repelFy))
                {
                    markRepelFx += repelFx;
                    markRepelFy += repelFy;
                }
            }
        }

        if (mark.ConstrainToAxis && (Math.Abs(mark.AxisDx) > 0.001 || Math.Abs(mark.AxisDy) > 0.001))
        {
            var aProj = attractFx * mark.AxisDx + attractFy * mark.AxisDy;
            attractFx = mark.AxisDx * aProj;
            attractFy = mark.AxisDy * aProj;
            var pProj = partRepelFx * mark.AxisDx + partRepelFy * mark.AxisDy;
            partRepelFx = mark.AxisDx * pProj;
            partRepelFy = mark.AxisDy * pProj;
            var mProj = markRepelFx * mark.AxisDx + markRepelFy * mark.AxisDy;
            markRepelFx = mark.AxisDx * mProj;
            markRepelFy = mark.AxisDy * mProj;
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

    private static bool TryGetPartCentroid(PartBbox part, out double centroidX, out double centroidY)
    {
        centroidX = 0.0;
        centroidY = 0.0;

        if (part.Polygon != null && part.Polygon.Count >= 3)
        {
            centroidX = part.Polygon.Average(static point => point[0]);
            centroidY = part.Polygon.Average(static point => point[1]);
            return true;
        }

        centroidX = (part.MinX + part.MaxX) * 0.5;
        centroidY = (part.MinY + part.MaxY) * 0.5;
        return true;
    }

    private static (double x, double y) NearestOnShape(double px, double py, PartBbox part)
    {
        if (part.Polygon != null && part.Polygon.Count >= 3)
            return NearestOnPolygon(px, py, part.Polygon);

        var nx = Math.Max(part.MinX, Math.Min(px, part.MaxX));
        var ny = Math.Max(part.MinY, Math.Min(py, part.MaxY));
        return (nx, ny);
    }

    private static (double x, double y) NearestOnPolygon(double px, double py, IReadOnlyList<double[]> polygon)
    {
        var bestX = polygon[0][0];
        var bestY = polygon[0][1];
        var bestDist2 = double.MaxValue;
        var n = polygon.Count;
        for (var i = 0; i < n; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % n];
            var (nx, ny) = NearestOnSegment(px, py, a[0], a[1], b[0], b[1]);
            var d2 = (nx - px) * (nx - px) + (ny - py) * (ny - py);
            if (d2 < bestDist2) { bestDist2 = d2; bestX = nx; bestY = ny; }
        }

        return (bestX, bestY);
    }

    private static (double x, double y) NearestOnSegment(double px, double py, double ax, double ay, double bx, double by)
    {
        var abx = bx - ax; var aby = by - ay;
        var len2 = abx * abx + aby * aby;
        if (len2 < 1e-10) return (ax, ay);
        var t = Math.Max(0.0, Math.Min(1.0, ((px - ax) * abx + (py - ay) * aby) / len2));
        return (ax + t * abx, ay + t * aby);
    }

    private static bool IsInsidePolygon(double px, double py, IReadOnlyList<double[]> polygon)
    {
        var inside = false;
        var n = polygon.Count;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            var xi = polygon[i][0]; var yi = polygon[i][1];
            var xj = polygon[j][0]; var yj = polygon[j][1];
            if (((yi > py) != (yj > py)) && px < (xj - xi) * (py - yi) / (yj - yi) + xi)
                inside = !inside;
        }
        return inside;
    }

    private static double Clamp(double value, double min, double max) =>
        Math.Max(min, Math.Min(max, value));

    private static bool TryGetMarkRepulsion(
        ForceDirectedMarkItem mark,
        ForceDirectedMarkItem other,
        ForcePassOptions options,
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

            var targetSeparation = depth + options.MarkGapMm;
            repelFx = -axisX * targetSeparation * options.KRepelMark;
            repelFy = -axisY * targetSeparation * options.KRepelMark;
            return true;
        }

        var ox = OverlapX(mark, other);
        var oy = OverlapY(mark, other);
        if (ox <= 0.0 || oy <= 0.0)
            return false;

        if (ox < oy)
            repelFx = (mark.Cx >= other.Cx ? 1.0 : -1.0) * options.KRepelMark * (ox + options.MarkGapMm);
        else
            repelFy = (mark.Cy >= other.Cy ? 1.0 : -1.0) * options.KRepelMark * (oy + options.MarkGapMm);

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
