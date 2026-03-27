using System.Collections.Generic;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class TeklaDrawingViewApiLayoutTests
{
    [Fact]
    public void FormatEstimateFitFailureDecision_IncludesStageCountsAndConflictDetails()
    {
        var decision = new TeklaDrawingViewApi.EstimateFitFailureDecision(
            stage: "candidate-reject",
            candidateScale: 20,
            fits: false,
            oversizeConflicts: new List<DrawingFitConflict>
            {
                new()
                {
                    ViewId = 10,
                    ViewType = "FrontView",
                    AttemptedZone = "sheet",
                    Conflicts = new List<DrawingFitConflictItem>
                    {
                        new() { Type = "sheet-oversize", Target = "usable=100.0x80.0;view=120.0x90.0" }
                    }
                }
            },
            diagnosedConflicts: new List<DrawingFitConflict>
            {
                new()
                {
                    ViewId = 20,
                    ViewType = "SectionView",
                    AttemptedZone = "Top",
                    BBoxMinX = 10,
                    BBoxMinY = 20,
                    BBoxMaxX = 40,
                    BBoxMaxY = 60,
                    Conflicts = new List<DrawingFitConflictItem>
                    {
                        new() { Type = "intersects_view", OtherViewId = 99, Target = "n/a" }
                    }
                }
            });

        var text = TeklaDrawingViewApi.FormatEstimateFitFailureDecision(decision);

        Assert.Contains("stage=candidate-reject", text);
        Assert.Contains("candidate=1:20", text);
        Assert.Contains("fits=0", text);
        Assert.Contains("oversizeConflicts=1", text);
        Assert.Contains("diagnosedConflicts=1", text);
        Assert.Contains("source=oversize view=10:FrontView zone=sheet", text);
        Assert.Contains("conflict=sheet-oversize:other=n/a:target=usable=100.0x80.0;view=120.0x90.0", text);
        Assert.Contains("source=diagnosed view=20:SectionView zone=Top bbox=[10.00,20.00,40.00,60.00]", text);
        Assert.Contains("conflict=intersects_view:other=99:target=n/a", text);
    }

    [Fact]
    public void FormatEstimateFitFailureDecision_UsesEmptyConflictListsByDefault()
    {
        var decision = new TeklaDrawingViewApi.EstimateFitFailureDecision(
            stage: "keep-current-scales",
            candidateScale: 50,
            fits: false,
            oversizeConflicts: null,
            diagnosedConflicts: null);

        var text = TeklaDrawingViewApi.FormatEstimateFitFailureDecision(decision);

        Assert.Contains("stage=keep-current-scales", text);
        Assert.Contains("candidate=1:50", text);
        Assert.Contains("oversizeConflicts=0", text);
        Assert.Contains("diagnosedConflicts=0", text);
    }

    [Theory]
    [InlineData(SectionPlacementSide.Top, 60, 40, 70, 20, 5, true)]
    [InlineData(SectionPlacementSide.Top, 60, 40, 60, 20, 5, false)]
    [InlineData(SectionPlacementSide.Right, 60, 40, 20, 50, 5, true)]
    [InlineData(SectionPlacementSide.Right, 60, 40, 20, 45, 5, false)]
    public void IsOversizedStandardSectionScaleDriver_MatchesStage3Policy(
        SectionPlacementSide placementSide,
        double baseWidth,
        double baseHeight,
        double sectionWidth,
        double sectionHeight,
        double gap,
        bool expected)
    {
        Assert.Equal(
            expected,
            TeklaDrawingViewApi.IsOversizedStandardSectionScaleDriver(
                placementSide,
                baseWidth,
                baseHeight,
                sectionWidth,
                sectionHeight,
                gap));
    }

    [Fact]
    public void ProbeDetailPlacement_UsesStableCrossBandDegradedReason()
    {
        var decision = BaseProjectedDrawingArrangeStrategy.ProbeDetailPlacement(
            ownerRect: new ReservedRect(40, 40, 80, 80),
            detailWidth: 20,
            detailHeight: 10,
            offset: 10,
            freeMinX: 0,
            freeMaxX: 200,
            freeMinY: 0,
            freeMaxY: 200,
            occupied: new[]
            {
                new ReservedRect(40, 40, 80, 80),
                new ReservedRect(0, 85, 85, 200),
                new ReservedRect(85, 35, 140, 140)
            },
            anchorX: 100,
            anchorY: 100);

        Assert.True(decision.Success);
        Assert.Equal("cross-band", decision.DegradedReason);
    }
}
