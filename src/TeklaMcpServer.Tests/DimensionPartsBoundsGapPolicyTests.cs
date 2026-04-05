using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DimensionPartsBoundsGapPolicyTests
{
    [Fact]
    public void Evaluate_DoesNotRequestCorrection_WhenCurrentGapAlreadyExceedsTarget()
    {
        var placementInfo = new DimensionViewPlacementInfo
        {
            HasPartsBounds = true,
            PartsBoundsSide = "top",
            IsOutsidePartsBounds = true,
            OffsetFromPartsBounds = 20,
            ViewScale = 1
        };

        var result = DimensionPartsBoundsGapPolicy.Evaluate(placementInfo);

        Assert.True(result.CanEvaluate);
        Assert.Equal(20, result.CurrentGapDrawing, 3);
        Assert.Equal(10, result.TargetGapPaper, 3);
        Assert.Equal(10, result.TargetGapDrawing, 3);
        Assert.False(result.RequiresOutwardCorrection);
        Assert.Equal(0, result.SuggestedOutwardDeltaDrawing, 3);
    }

    [Fact]
    public void Evaluate_RequestsOutwardCorrection_WhenCurrentGapIsBelowTarget()
    {
        var placementInfo = new DimensionViewPlacementInfo
        {
            HasPartsBounds = true,
            PartsBoundsSide = "right",
            IsOutsidePartsBounds = true,
            OffsetFromPartsBounds = 5,
            ViewScale = 2
        };

        var result = DimensionPartsBoundsGapPolicy.Evaluate(placementInfo);

        Assert.True(result.CanEvaluate);
        Assert.Equal(5, result.CurrentGapDrawing, 3);
        Assert.Equal(10, result.TargetGapPaper, 3);
        Assert.Equal(20, result.TargetGapDrawing, 3);
        Assert.True(result.RequiresOutwardCorrection);
        Assert.Equal(15, result.SuggestedOutwardDeltaDrawing, 3);
    }

    [Fact]
    public void Evaluate_SkipsOverlapPlacement()
    {
        var placementInfo = new DimensionViewPlacementInfo
        {
            HasPartsBounds = true,
            PartsBoundsSide = "overlap",
            OffsetFromPartsBounds = 0,
            ViewScale = 1
        };

        var result = DimensionPartsBoundsGapPolicy.Evaluate(placementInfo);

        Assert.False(result.CanEvaluate);
        Assert.False(result.RequiresOutwardCorrection);
        Assert.Equal(10, result.TargetGapPaper, 3);
        Assert.Equal(0, result.TargetGapDrawing, 3);
    }
}
