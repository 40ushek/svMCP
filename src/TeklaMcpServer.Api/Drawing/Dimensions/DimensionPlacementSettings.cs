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
    /// Shortest segment a steel location chain keeps, on paper: a segment shorter than this
    /// cannot carry a readable number. The view-unit cutoff above still applies as a floor.
    /// </summary>
    public const double MinimumChainSegmentPaperMm = 0.8;

    /// <summary>
    /// A local chain equal to the one next to a neighbouring part is not repeated when the two
    /// parts are closer than this on paper.
    /// </summary>
    public const double LocalChainRepeatGapPaperMm = 30.0;

    /// <summary>The steel chain's shortest kept segment in view units at a view scale.</summary>
    public static double MinimumSteelChainSegment(double viewScale) =>
        System.Math.Max(MinimumChainSegmentViewUnits,
            viewScale > 0 && !double.IsInfinity(viewScale) ? MinimumChainSegmentPaperMm * viewScale : 0);

    /// <summary>
    /// Section-preview positions closer than this on paper are represented by one
    /// candidate and reported as merged. Separate from the model-unit segment cutoff.
    /// </summary>
    public const double SectionReadabilityGapPaperMm = 0.5;

    /// <summary>
    /// How far, in view units along the view depth, a part must reach past the main part's
    /// end to count as an end part (end plate, cap) on a section or end view.
    /// </summary>
    public const double EndPartDepthToleranceViewUnits = 0.5;

    /// <summary>
    /// A part counts as lying in the view plane (seen face-on) when the out-of-plane share of
    /// each of its own X and Y axes is at most this: 0.1 is about 6 degrees of tilt.
    /// </summary>
    public const double FaceOnAxisOutOfPlaneTolerance = 0.1;
}
