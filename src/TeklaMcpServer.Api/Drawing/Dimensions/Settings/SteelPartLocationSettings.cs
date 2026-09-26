using System;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class SteelPartLocationSettings
{
    public DimensionChainSide Side { get; }
    public double MinimumSegmentViewUnits { get; }
    public double SameCoordinateTolerance { get; }

    public SteelPartLocationSettings(DimensionChainSide side, double minimumSegmentViewUnits,
        double sameCoordinateTolerance)
    {
        if (!IsNonnegativeFinite(minimumSegmentViewUnits))
            throw new ArgumentOutOfRangeException(nameof(minimumSegmentViewUnits));
        if (!IsNonnegativeFinite(sameCoordinateTolerance))
            throw new ArgumentOutOfRangeException(nameof(sameCoordinateTolerance));
        Side = side;
        MinimumSegmentViewUnits = minimumSegmentViewUnits;
        SameCoordinateTolerance = sameCoordinateTolerance;
    }

    private static bool IsNonnegativeFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0;
}
