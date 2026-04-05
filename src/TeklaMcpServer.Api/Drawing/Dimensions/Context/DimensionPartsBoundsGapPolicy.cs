namespace TeklaMcpServer.Api.Drawing;

internal sealed class DimensionPartsBoundsGapPolicyResult
{
    public bool CanEvaluate { get; set; }
    public bool RequiresOutwardCorrection { get; set; }
    public double CurrentGapDrawing { get; set; }
    public double TargetGapPaper { get; set; }
    public double TargetGapDrawing { get; set; }
    public double SuggestedOutwardDeltaDrawing { get; set; }
}

internal static class DimensionPartsBoundsGapPolicy
{
    public static DimensionPartsBoundsGapPolicyResult Evaluate(
        DimensionViewPlacementInfo placementInfo,
        double targetGapPaper = TeklaDrawingDimensionsApi.DefaultArrangeTargetGapPaper)
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
        var suggestedOutwardDelta = System.Math.Max(0.0, targetGapDrawing - currentGapDrawing);

        result.CanEvaluate = true;
        result.CurrentGapDrawing = System.Math.Round(currentGapDrawing, 3);
        result.TargetGapDrawing = targetGapDrawing;
        result.RequiresOutwardCorrection = suggestedOutwardDelta > 0.0001;
        result.SuggestedOutwardDeltaDrawing = System.Math.Round(suggestedOutwardDelta, 3);
        return result;
    }
}
