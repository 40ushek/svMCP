using System;
using System.Linq;
using TeklaMcpServer.Api.Drawing;

namespace TeklaMcpServer.Api.Drawing.Dimensions;

/// <summary>Calculates a positive Tekla offset that leaves a paper-space gap beyond a view extent.</summary>
public static class DimensionPlacementCalculator
{
    public static DimensionPlacementCalculation Calculate(
        DimensionChainSide side,
        double[] points,
        GeometryGroupExtent extent,
        double viewScale,
        double paperGapMm = DimensionPlacementSettings.DefaultPaperGapMm)
    {
        if (points == null || points.Length < 6 || points.Length % 3 != 0)
            throw new ArgumentException("At least two XYZ points are required", nameof(points));
        if (extent == null) throw new ArgumentNullException(nameof(extent));
        if (!Finite(viewScale) || viewScale <= 0)
            throw new ArgumentOutOfRangeException(nameof(viewScale), "View scale must be finite and positive");
        if (!Finite(paperGapMm) || paperGapMm < 0)
            throw new ArgumentOutOfRangeException(nameof(paperGapMm), "Paper gap must be finite and non-negative");
        if (points.Any(value => !Finite(value)))
            throw new ArgumentException("Dimension points must be finite", nameof(points));

        var horizontal = side is DimensionChainSide.Top or DimensionChainSide.Bottom;
        var chainAxisIndex = horizontal ? 0 : 1;
        var offsetAxisIndex = horizontal ? 1 : 0;
        var baseAxisValue = Enumerable.Range(0, points.Length / 3)
            .Min(index => points[index * 3 + chainAxisIndex]);
        var firstPoints = Enumerable.Range(0, points.Length / 3)
            .Where(index => Math.Abs(points[index * 3 + chainAxisIndex] - baseAxisValue) <= 0.01)
            .ToArray();
        if (firstPoints.Length != 1)
            throw new ArgumentException("The chain start is ambiguous: multiple points share the first axis coordinate", nameof(points));

        var baseCoordinate = points[firstPoints[0] * 3 + offsetAxisIndex];
        var gapViewUnits = paperGapMm * viewScale;
        if (!Finite(gapViewUnits))
            throw new ArgumentOutOfRangeException(nameof(paperGapMm), "Converted gap is not finite");

        var targetLineCoordinate = side switch
        {
            DimensionChainSide.Top => extent.MaxY + gapViewUnits,
            DimensionChainSide.Bottom => extent.MinY - gapViewUnits,
            DimensionChainSide.Left => extent.MinX - gapViewUnits,
            DimensionChainSide.Right => extent.MaxX + gapViewUnits,
            _ => throw new ArgumentOutOfRangeException(nameof(side))
        };
        var distance = side is DimensionChainSide.Top or DimensionChainSide.Right
            ? targetLineCoordinate - baseCoordinate
            : baseCoordinate - targetLineCoordinate;
        if (!Finite(distance) || distance <= DimensionPlacementSettings.MinimumDistanceViewUnits)
            throw new ArgumentException("Calculated dimension distance is not positive or the points lie beyond the target side", nameof(points));

        return new DimensionPlacementCalculation(paperGapMm, gapViewUnits, viewScale,
            baseCoordinate, targetLineCoordinate, distance);
    }

    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}

public sealed class DimensionPlacementCalculation
{
    internal DimensionPlacementCalculation(double paperGapMm, double gapViewUnits, double viewScale,
        double baseCoordinate, double targetLineCoordinate, double distance)
    {
        PaperGapMm = paperGapMm;
        GapViewUnits = gapViewUnits;
        ViewScale = viewScale;
        BaseCoordinate = baseCoordinate;
        TargetLineCoordinate = targetLineCoordinate;
        Distance = distance;
    }

    public double PaperGapMm { get; }
    public double GapViewUnits { get; }
    public double ViewScale { get; }
    public double BaseCoordinate { get; }
    public double TargetLineCoordinate { get; }
    public double Distance { get; }
}
