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
}
