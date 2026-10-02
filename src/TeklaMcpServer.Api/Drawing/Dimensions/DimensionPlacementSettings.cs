namespace TeklaMcpServer.Api.Drawing.Dimensions;

/// <summary>
/// Shared defaults for placing ordinary drawing dimensions.
/// Values named <c>PaperGapMm</c> are converted to drawing units by the caller
/// using the view scale.
/// </summary>
public static class DimensionPlacementSettings
{
    public const double DefaultPaperGapMm = 8.0;

    /// <summary>Resolve the preview's line row; explicit offsets override this default.</summary>
    public static double ResolvePaperGapMm(int row, double? paperGapMm)
    {
        if (paperGapMm.HasValue) return paperGapMm.Value;
        if (row < 1)
            throw new System.ArgumentOutOfRangeException(nameof(row), "row must be a positive integer");
        return row * DefaultPaperGapMm;
    }

    public const double VerificationToleranceViewUnits = 0.6;
    public const double MinimumDistanceViewUnits = 0.001;

    /// <summary>
    /// Shortest segment a preview chain keeps, in view units (the units of the point
    /// coordinates), not paper millimetres; on paper it is this value divided by the view scale.
    /// It sits above the 0.2-2 mm staircase of a polygonised rolled-profile radius.
    /// </summary>
    public const double MinimumChainSegmentViewUnits = 3.0;

    /// <summary>
    /// Section-preview positions closer than this on paper are represented by one
    /// candidate and reported as merged. Separate from the model-unit segment cutoff.
    /// </summary>
    public const double SectionReadabilityGapPaperMm = 0.5;
}
