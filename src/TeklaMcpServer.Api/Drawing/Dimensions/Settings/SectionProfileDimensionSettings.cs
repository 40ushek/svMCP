using System;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class SectionProfileDimensionSettings
{
    public DimensionChainSide Side { get; }
    public double SameCoordinateTolerance { get; }

    public SectionProfileDimensionSettings(DimensionChainSide side, double sameCoordinateTolerance)
    {
        if (double.IsNaN(sameCoordinateTolerance) || double.IsInfinity(sameCoordinateTolerance)
            || sameCoordinateTolerance < 0)
            throw new ArgumentOutOfRangeException(nameof(sameCoordinateTolerance));
        Side = side;
        SameCoordinateTolerance = sameCoordinateTolerance;
    }
}
