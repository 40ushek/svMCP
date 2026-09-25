namespace TeklaMcpServer.Api.Drawing.Dimensions;

/// <summary>
/// Shared defaults for placing ordinary drawing dimensions.
/// Values named <c>PaperGapMm</c> are converted to drawing units by the caller
/// using the view scale.
/// </summary>
public static class DimensionPlacementSettings
{
    public const double DefaultPaperGapMm = 8.0;
    public const double VerificationToleranceViewUnits = 0.6;
    public const double MinimumDistanceViewUnits = 0.001;

    /// <summary>
    /// Shortest segment a preview chain keeps, in view units (the units of the point
    /// coordinates), not paper millimetres; on paper it is this value divided by the view scale.
    /// It sits above the 0.2-2 mm staircase of a polygonised rolled-profile radius.
    /// </summary>
    public const double MinimumChainSegmentViewUnits = 3.0;
}
