using System;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class SectionPartLocationSettings
{
    public DimensionChainSide Side { get; }
    public double SameCoordinateTolerance { get; }
    public double ReadableGapViewUnits { get; }

    public SectionPartLocationSettings(DimensionChainSide side, double sameCoordinateTolerance,
        double readableGapViewUnits)
    {
        if (!IsNonnegativeFinite(sameCoordinateTolerance))
            throw new ArgumentOutOfRangeException(nameof(sameCoordinateTolerance));
        if (!IsNonnegativeFinite(readableGapViewUnits))
            throw new ArgumentOutOfRangeException(nameof(readableGapViewUnits));
        Side = side;
        SameCoordinateTolerance = sameCoordinateTolerance;
        ReadableGapViewUnits = readableGapViewUnits;
    }

    private static bool IsNonnegativeFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0;
}
