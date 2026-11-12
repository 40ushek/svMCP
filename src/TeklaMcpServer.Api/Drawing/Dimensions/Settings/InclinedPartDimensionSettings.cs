using System;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class InclinedPartDimensionSettings
{
    /// <summary>A part is inclined when its long edge is at least this far from both view axes.</summary>
    public double MinimumTiltDegrees { get; }

    /// <summary>
    /// How far, in view units, a main-part point must reach past the part's end to get its own
    /// segment; closer than this, the main part is flush with the part there.
    /// </summary>
    public double MinimumSegmentViewUnits { get; }

    /// <summary>Coordinate comparison tolerance in view units.</summary>
    public double SameCoordinateTolerance { get; }

    /// <summary>
    /// How far, in view units across the part, a main-part point may lie outside the part's
    /// own thickness and still count as standing against it.
    /// </summary>
    public double ContactBandToleranceViewUnits { get; }

    /// <summary>
    /// A part standing on an inclined part is carried by that part's chain when its own size
    /// across the inclined part is at most this many of that part's thicknesses.
    /// </summary>
    public double AttachedPartAcrossRatio { get; } = 3.0;

    /// <summary>How far from the inclined part, in view units, a part may stand and still count as welded to it.</summary>
    public double AttachedPartGapViewUnits { get; } = 2.0;

    public InclinedPartDimensionSettings(double minimumTiltDegrees = 1.0, double minimumSegmentViewUnits = 3.0,
        double sameCoordinateTolerance = 0.01, double contactBandToleranceViewUnits = 0.5)
    {
        foreach (var value in new[] { minimumTiltDegrees, minimumSegmentViewUnits, sameCoordinateTolerance,
                     contactBandToleranceViewUnits })
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
                throw new ArgumentOutOfRangeException(nameof(minimumTiltDegrees), "Settings must be finite and non-negative.");
        MinimumTiltDegrees = minimumTiltDegrees;
        MinimumSegmentViewUnits = minimumSegmentViewUnits;
        SameCoordinateTolerance = sameCoordinateTolerance;
        ContactBandToleranceViewUnits = contactBandToleranceViewUnits;
    }
}
