using System;
using System.Collections.Generic;
using System.Linq;
using TeklaMcpServer.Api.Algorithms.Geometry;

namespace TeklaMcpServer.Api.Algorithms.Marks;

internal enum AxisMarkPairSeparationMode
{
    None,
    ParallelAxes,
    IndependentAxes
}

internal readonly struct AxisMarkPairSeparationMark
{
    public AxisMarkPairSeparationMark(
        int id,
        double x,
        double y,
        double width,
        double height,
        bool hasLeaderLine,
        bool hasAxis,
        double axisDx,
        double axisDy,
        bool canMove,
        IReadOnlyList<double[]>? localCorners)
    {
        Id = id;
        X = x;
        Y = y;
        Width = width;
        Height = height;
        HasLeaderLine = hasLeaderLine;
        HasAxis = hasAxis;
        AxisDx = axisDx;
        AxisDy = axisDy;
        CanMove = canMove;
        LocalCorners = localCorners ?? [];
    }

    public int Id { get; }
    public double X { get; }
    public double Y { get; }
    public double Width { get; }
    public double Height { get; }
    public bool HasLeaderLine { get; }
    public bool HasAxis { get; }
    public double AxisDx { get; }
    public double AxisDy { get; }
    public bool CanMove { get; }
    public IReadOnlyList<double[]> LocalCorners { get; }

    public static AxisMarkPairSeparationMark FromPlacement(MarkLayoutPlacement placement) =>
        new AxisMarkPairSeparationMark(
            placement.Id,
            placement.X,
            placement.Y,
            placement.Width,
            placement.Height,
            placement.HasLeaderLine,
            placement.HasAxis,
            placement.AxisDx,
            placement.AxisDy,
            placement.CanMove,
            placement.LocalCorners);

    public static AxisMarkPairSeparationMark FromForceItem(
        ForceDirectedMarkItem item,
        bool hasAxis,
        bool hasLeaderLine = false) =>
        new AxisMarkPairSeparationMark(
            item.Id,
            item.Cx,
            item.Cy,
            item.Width,
            item.Height,
            hasLeaderLine,
            hasAxis,
            item.AxisDx,
            item.AxisDy,
            item.CanMove,
            item.LocalCorners);
}

internal readonly struct AxisMarkPairSeparationResult
{
    public AxisMarkPairSeparationResult(
        AxisMarkPairSeparationMode mode,
        double deltaAx,
        double deltaAy,
        double deltaBx,
        double deltaBy,
        double overlapDepth)
    {
        Mode = mode;
        DeltaAx = deltaAx;
        DeltaAy = deltaAy;
        DeltaBx = deltaBx;
        DeltaBy = deltaBy;
        OverlapDepth = overlapDepth;
    }

    public AxisMarkPairSeparationMode Mode { get; }
    public double DeltaAx { get; }
    public double DeltaAy { get; }
    public double DeltaBx { get; }
    public double DeltaBy { get; }
    public double OverlapDepth { get; }

    public bool HasMovement =>
        Math.Abs(DeltaAx) > 0.001 ||
        Math.Abs(DeltaAy) > 0.001 ||
        Math.Abs(DeltaBx) > 0.001 ||
        Math.Abs(DeltaBy) > 0.001;
}

internal static class AxisMarkPairSeparation
{
    private const double AxisEpsilon = 0.001;

    public static bool TryCompute(
        AxisMarkPairSeparationMark a,
        AxisMarkPairSeparationMark b,
        double gap,
        out AxisMarkPairSeparationResult result)
    {
        result = default;

        if (a.HasLeaderLine || b.HasLeaderLine || !a.HasAxis || !b.HasAxis)
            return false;

        var aAxisDx = a.AxisDx;
        var aAxisDy = a.AxisDy;
        if (!TryNormalize(ref aAxisDx, ref aAxisDy))
            return false;

        var bAxisDx = b.AxisDx;
        var bAxisDy = b.AxisDy;
        if (!TryNormalize(ref bAxisDx, ref bAxisDy))
            return false;

        if (!TryGetSeparation(a, b, out var separationAxisX, out var separationAxisY, out var separationDepth))
            return false;

        var split = GetSplit(a, b);
        if (split.Total == 0.0)
        {
            result = new AxisMarkPairSeparationResult(AxisMarkPairSeparationMode.None, 0.0, 0.0, 0.0, 0.0, separationDepth);
            return true;
        }

        var dot = (aAxisDx * bAxisDx) + (aAxisDy * bAxisDy);
        if (Math.Abs(dot) < 0.95)
            return TryComputeIndependentAxes(
                a,
                b,
                aAxisDx,
                aAxisDy,
                bAxisDx,
                bAxisDy,
                separationAxisX,
                separationAxisY,
                separationDepth,
                gap,
                split,
                out result);

        bAxisDx = dot < 0.0 ? -bAxisDx : bAxisDx;
        bAxisDy = dot < 0.0 ? -bAxisDy : bAxisDy;
        var axisDx = aAxisDx + bAxisDx;
        var axisDy = aAxisDy + bAxisDy;
        if (!TryNormalize(ref axisDx, ref axisDy))
            return false;

        var projectedHalfA = ProjectHalfExtent(a, axisDx, axisDy);
        var projectedHalfB = ProjectHalfExtent(b, axisDx, axisDy);
        var centerA = Dot(a.X, a.Y, axisDx, axisDy);
        var centerB = Dot(b.X, b.Y, axisDx, axisDy);
        var axisOverlap = (projectedHalfA + projectedHalfB) - Math.Abs(centerA - centerB);

        if (axisOverlap <= 0.0)
        {
            result = new AxisMarkPairSeparationResult(AxisMarkPairSeparationMode.ParallelAxes, 0.0, 0.0, 0.0, 0.0, separationDepth);
            return true;
        }

        var push = axisOverlap + gap;
        var direction = centerB >= centerA ? 1.0 : -1.0;
        result = new AxisMarkPairSeparationResult(
            AxisMarkPairSeparationMode.ParallelAxes,
            -axisDx * direction * push * split.MoveA,
            -axisDy * direction * push * split.MoveA,
            +axisDx * direction * push * split.MoveB,
            +axisDy * direction * push * split.MoveB,
            separationDepth);
        return true;
    }

    private static bool TryComputeIndependentAxes(
        AxisMarkPairSeparationMark a,
        AxisMarkPairSeparationMark b,
        double aAxisDx,
        double aAxisDy,
        double bAxisDx,
        double bAxisDy,
        double separationAxisX,
        double separationAxisY,
        double separationDepth,
        double gap,
        (double MoveA, double MoveB, double Total) split,
        out AxisMarkPairSeparationResult result)
    {
        result = default;

        if (!a.CanMove && !b.CanMove)
        {
            result = new AxisMarkPairSeparationResult(AxisMarkPairSeparationMode.IndependentAxes, 0.0, 0.0, 0.0, 0.0, separationDepth);
            return true;
        }

        if (a.CanMove && b.CanMove)
        {
            var desiredDx = separationAxisX * (separationDepth + gap);
            var desiredDy = separationAxisY * (separationDepth + gap);

            var scaledADx = aAxisDx * split.MoveA;
            var scaledADy = aAxisDy * split.MoveA;
            var scaledBDx = bAxisDx * split.MoveB;
            var scaledBDy = bAxisDy * split.MoveB;

            var determinant = (scaledADx * scaledBDy) - (scaledADy * scaledBDx);
            if (Math.Abs(determinant) < AxisEpsilon)
                return false;

            var moveA = ((desiredDx * scaledBDy) - (desiredDy * scaledBDx)) / determinant;
            var moveB = ((scaledADx * desiredDy) - (scaledADy * desiredDx)) / determinant;

            result = new AxisMarkPairSeparationResult(
                AxisMarkPairSeparationMode.IndependentAxes,
                -aAxisDx * moveA * split.MoveA,
                -aAxisDy * moveA * split.MoveA,
                +bAxisDx * moveB * split.MoveB,
                +bAxisDy * moveB * split.MoveB,
                separationDepth);
            return true;
        }

        if (a.CanMove)
        {
            var move = ResolveSingleAxisMove(aAxisDx, aAxisDy, separationAxisX, separationAxisY, separationDepth, gap);
            result = new AxisMarkPairSeparationResult(
                AxisMarkPairSeparationMode.IndependentAxes,
                -aAxisDx * move,
                -aAxisDy * move,
                0.0,
                0.0,
                separationDepth);
            return true;
        }

        if (b.CanMove)
        {
            var move = ResolveSingleAxisMove(bAxisDx, bAxisDy, separationAxisX, separationAxisY, separationDepth, gap);
            result = new AxisMarkPairSeparationResult(
                AxisMarkPairSeparationMode.IndependentAxes,
                0.0,
                0.0,
                +bAxisDx * move,
                +bAxisDy * move,
                separationDepth);
            return true;
        }

        return true;
    }

    private static double ResolveSingleAxisMove(
        double axisDx,
        double axisDy,
        double separationAxisX,
        double separationAxisY,
        double separationDepth,
        double gap)
    {
        var targetDx = separationAxisX * (separationDepth + gap);
        var targetDy = separationAxisY * (separationDepth + gap);

        var alongX = Math.Abs(axisDx) >= AxisEpsilon ? targetDx / axisDx : 0.0;
        var alongY = Math.Abs(axisDy) >= AxisEpsilon ? targetDy / axisDy : 0.0;

        if (Math.Abs(axisDx) < AxisEpsilon)
            return alongY;

        if (Math.Abs(axisDy) < AxisEpsilon)
            return alongX;

        return Math.Abs(alongX) >= Math.Abs(alongY) ? alongX : alongY;
    }

    private static (double MoveA, double MoveB, double Total) GetSplit(
        AxisMarkPairSeparationMark a,
        AxisMarkPairSeparationMark b)
    {
        if (a.CanMove && b.CanMove)
            return (0.5, 0.5, 1.0);

        if (a.CanMove)
            return (1.0, 0.0, 1.0);

        if (b.CanMove)
            return (0.0, 1.0, 1.0);

        return (0.0, 0.0, 0.0);
    }

    private static bool TryGetSeparation(
        AxisMarkPairSeparationMark a,
        AxisMarkPairSeparationMark b,
        out double separationAxisX,
        out double separationAxisY,
        out double separationDepth)
    {
        separationAxisX = 0.0;
        separationAxisY = 0.0;
        separationDepth = 0.0;

        if (a.LocalCorners.Count >= 3 && b.LocalCorners.Count >= 3)
        {
            var aPolygon = TranslateCorners(a);
            var bPolygon = TranslateCorners(b);
            return PolygonGeometry.TryGetMinimumTranslationVector(aPolygon, bPolygon, out separationAxisX, out separationAxisY, out separationDepth);
        }

        var overlapX = Math.Min(a.X + (a.Width / 2.0), b.X + (b.Width / 2.0))
            - Math.Max(a.X - (a.Width / 2.0), b.X - (b.Width / 2.0));
        var overlapY = Math.Min(a.Y + (a.Height / 2.0), b.Y + (b.Height / 2.0))
            - Math.Max(a.Y - (a.Height / 2.0), b.Y - (b.Height / 2.0));

        if (overlapX <= 0.0 || overlapY <= 0.0)
            return false;

        var moveX = overlapX <= overlapY;
        separationAxisX = moveX ? (b.X >= a.X ? 1.0 : -1.0) : 0.0;
        separationAxisY = moveX ? 0.0 : (b.Y >= a.Y ? 1.0 : -1.0);
        separationDepth = moveX ? overlapX : overlapY;
        return true;
    }

    private static double ProjectHalfExtent(AxisMarkPairSeparationMark mark, double axisDx, double axisDy)
    {
        if (mark.LocalCorners.Count < 3)
            return (Math.Abs(axisDx) * (mark.Width / 2.0)) + (Math.Abs(axisDy) * (mark.Height / 2.0));

        return mark.LocalCorners
            .Select(c => Math.Abs(Dot(c[0], c[1], axisDx, axisDy)))
            .DefaultIfEmpty(0.0)
            .Max();
    }

    private static List<double[]> TranslateCorners(AxisMarkPairSeparationMark mark) =>
        PolygonGeometry.Translate(mark.LocalCorners, mark.X, mark.Y);

    private static double Dot(double x, double y, double axisDx, double axisDy) =>
        (x * axisDx) + (y * axisDy);

    private static bool TryNormalize(ref double dx, ref double dy)
    {
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length < AxisEpsilon)
            return false;

        dx /= length;
        dy /= length;
        return true;
    }
}
