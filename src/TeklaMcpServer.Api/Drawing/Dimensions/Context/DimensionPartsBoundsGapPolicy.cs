namespace TeklaMcpServer.Api.Drawing;

internal sealed class DimensionPartsBoundsGapPolicyResult
{
    public bool CanEvaluate { get; set; }
    public bool RequiresCorrection { get; set; }
    public bool RequiresOutwardCorrection { get; set; }
    public bool RequiresInwardCorrection { get; set; }
    public double CurrentGapDrawing { get; set; }
    public double TargetGapPaper { get; set; }
    public double TargetGapDrawing { get; set; }
    public double SuggestedAxisDeltaDrawing { get; set; }
    public double SuggestedOutwardDeltaDrawing { get; set; }
}

internal static class DimensionPartsBoundsGapPolicy
{
    public static DimensionPartsBoundsGapPolicyResult Evaluate(
        DimensionViewPlacementInfo placementInfo,
        double targetGapPaper = TeklaDrawingDimensionsApi.DefaultArrangeTargetGapPaper,
        bool allowInwardCorrection = false)
    {
        var result = new DimensionPartsBoundsGapPolicyResult
        {
            TargetGapPaper = targetGapPaper
        };

        if (!placementInfo.HasPartsBounds || !placementInfo.IsOutsidePartsBounds)
            return result;

        if (string.IsNullOrWhiteSpace(placementInfo.PartsBoundsSide) ||
            placementInfo.PartsBoundsSide == "overlap")
        {
            return result;
        }

        var currentGapDrawing = placementInfo.OffsetFromPartsBounds ?? 0.0;
        var viewScale = placementInfo.ViewScale > 0 ? placementInfo.ViewScale : 1.0;
        var targetGapDrawing = System.Math.Round(targetGapPaper * viewScale, 3);
        var signedDelta = System.Math.Round(targetGapDrawing - currentGapDrawing, 3);
        if (!allowInwardCorrection && signedDelta < 0)
            signedDelta = 0;

        result.CanEvaluate = true;
        result.CurrentGapDrawing = System.Math.Round(currentGapDrawing, 3);
        result.TargetGapDrawing = targetGapDrawing;
        result.RequiresCorrection = System.Math.Abs(signedDelta) > 0.0001;
        result.RequiresOutwardCorrection = signedDelta > 0.0001;
        result.RequiresInwardCorrection = signedDelta < -0.0001;
        result.SuggestedAxisDeltaDrawing = signedDelta;
        result.SuggestedOutwardDeltaDrawing = signedDelta > 0 ? signedDelta : 0;
        return result;
    }
}
