using System;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>Per-request coordinate comparisons in model/view millimetres, never paper units.</summary>
public sealed class DimensionCoordinateSettings
{
    public const double DefaultToleranceMm = 0.1;
    public double ToleranceMm { get; }

    public DimensionCoordinateSettings(double toleranceMm = DefaultToleranceMm)
    {
        if (double.IsNaN(toleranceMm) || double.IsInfinity(toleranceMm) || toleranceMm <= 0)
            throw new ArgumentOutOfRangeException(nameof(toleranceMm), "Coordinate tolerance must be positive and finite.");
        ToleranceMm = toleranceMm;
    }
}
