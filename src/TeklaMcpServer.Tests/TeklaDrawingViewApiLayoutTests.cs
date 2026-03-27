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
}
