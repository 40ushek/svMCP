using System.Linq;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DrawingLayoutCandidateApplyPlanTests
{
    [Fact]
    public void FromEvaluation_BuildsMoves_ForPlannedCandidate()
    {
        var rect = new ReservedRect(0, 0, 30, 20);
        var evaluation = CreateEvaluation(
            "fit_views_to_sheet:planned-centered",
            new DrawingLayoutCandidateView
            {
                Id = 7,
                OriginX = 15,
                OriginY = 10,
                Scale = 20,
                LayoutRect = rect
            });

        var plan = DrawingLayoutCandidateApplyPlanBuilder.FromEvaluation(evaluation);

        Assert.True(plan.CanApply);
        Assert.Equal("planned-candidate", plan.Reason);
        Assert.Equal("fit_views_to_sheet:planned-centered", plan.CandidateName);
        var move = Assert.Single(plan.Moves);
        Assert.Equal(7, move.ViewId);
        Assert.Equal(15, move.TargetOriginX);
        Assert.Equal(10, move.TargetOriginY);
        Assert.Equal(20, move.Scale);
        Assert.Equal(rect, move.LayoutRect);
    }

    [Fact]
    public void FromEvaluation_DoesNotBuildMoves_ForRuntimeCandidate()
    {
        var evaluation = CreateEvaluation(
            "fit_views_to_sheet:final",
            new DrawingLayoutCandidateView
            {
                Id = 7,
                OriginX = 15,
                OriginY = 10,
                Scale = 20,
                LayoutRect = new ReservedRect(0, 0, 30, 20)
            });

        var plan = DrawingLayoutCandidateApplyPlanBuilder.FromEvaluation(evaluation);

        Assert.False(plan.CanApply);
        Assert.Equal("runtime-candidate", plan.Reason);
        Assert.Empty(plan.Moves);
    }

    [Fact]
    public void FromEvaluation_ReportsMissingSelection()
    {
        var plan = DrawingLayoutCandidateApplyPlanBuilder.FromEvaluation(null);

        Assert.False(plan.CanApply);
        Assert.Equal("no-selected-candidate", plan.Reason);
        Assert.Empty(plan.Moves);
    }

    private static DrawingLayoutCandidateEvaluation CreateEvaluation(
        string candidateName,
        params DrawingLayoutCandidateView[] views)
        => new()
        {
            Candidate = new DrawingLayoutCandidate
            {
                Name = candidateName,
                Views = views.ToList()
            }
        };
}
