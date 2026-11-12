using System;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class SteelPartLocationSettings
{
    public DimensionChainSide Side { get; }
    public double MinimumSegmentViewUnits { get; }
    public double SameCoordinateTolerance { get; }
    /// <summary>For parts located along the main part. Default: the first face.</summary>
    public DimensionPartAnchor PartAnchor { get; }
    /// <summary>
    /// For end parts located across the main part. Default: both faces, so the chain closes
    /// on the end part's edges around the main-part edges.
    /// </summary>
    public DimensionPartAnchor EndPartAnchor { get; }

    public SteelPartLocationSettings(DimensionChainSide side, double minimumSegmentViewUnits,
        double sameCoordinateTolerance, DimensionPartAnchor partAnchor = DimensionPartAnchor.LowFace,
        DimensionPartAnchor endPartAnchor = DimensionPartAnchor.BothFaces)
    {
        if (!IsNonnegativeFinite(minimumSegmentViewUnits))
            throw new ArgumentOutOfRangeException(nameof(minimumSegmentViewUnits));
        if (!IsNonnegativeFinite(sameCoordinateTolerance))
            throw new ArgumentOutOfRangeException(nameof(sameCoordinateTolerance));
        Side = side;
        MinimumSegmentViewUnits = minimumSegmentViewUnits;
        SameCoordinateTolerance = sameCoordinateTolerance;
        PartAnchor = partAnchor;
        EndPartAnchor = endPartAnchor;
    }

    private static bool IsNonnegativeFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0;
}
