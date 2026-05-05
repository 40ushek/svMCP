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
        Assert.Equal(DrawingLayoutCandidateApplyPlanReason.PlannedCandidate, plan.Reason);
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
        Assert.Equal(DrawingLayoutCandidateApplyPlanReason.RuntimeCandidate, plan.Reason);
        Assert.Empty(plan.Moves);
    }

    [Fact]
    public void FromEvaluation_ReportsMissingSelection()
    {
        var plan = DrawingLayoutCandidateApplyPlanBuilder.FromEvaluation(null);

        Assert.False(plan.CanApply);
        Assert.Equal(DrawingLayoutCandidateApplyPlanReason.NoSelectedCandidate, plan.Reason);
        Assert.Empty(plan.Moves);
    }

    [Theory]
    [InlineData(DrawingLayoutCandidateApplyPlanReason.NoSelectedCandidate, "no-selected-candidate")]
    [InlineData(DrawingLayoutCandidateApplyPlanReason.PlannedCandidate, "planned-candidate")]
    [InlineData(DrawingLayoutCandidateApplyPlanReason.RuntimeCandidate, "runtime-candidate")]
    public void ToTraceString_ReturnsStableTraceValue(
        DrawingLayoutCandidateApplyPlanReason reason,
        string expected)
    {
        Assert.Equal(expected, DrawingLayoutCandidateApplyPlanReasonFormatter.ToTraceString(reason));
    }

    [Fact]
    public void Execute_DryRun_ValidatesRuntimeViewsWithoutApplying()
    {
        var plan = DrawingLayoutCandidateApplyPlanBuilder.FromEvaluation(CreateEvaluation(
            "fit_views_to_sheet:planned-centered",
            new DrawingLayoutCandidateView { Id = 7, OriginX = 15, OriginY = 10, Scale = 20 }));
        var applied = false;

        var result = new DrawingLayoutCandidateApplyService().Execute(
            plan,
            [7],
            DrawingLayoutCandidateApplyExecutionMode.DryRun,
            _ =>
            {
                applied = true;
                return true;
            });

        Assert.True(result.Success);
        Assert.False(applied);
        Assert.Equal(DrawingLayoutCandidateApplyExecutionReason.DryRun, result.Reason);
        Assert.Equal(1, result.RequestedMoveCount);
        Assert.Equal(0, result.AppliedMoveCount);
    }

    [Fact]
    public void Execute_ReportsMissingRuntimeViews()
    {
        var plan = DrawingLayoutCandidateApplyPlanBuilder.FromEvaluation(CreateEvaluation(
            "fit_views_to_sheet:planned-centered",
            new DrawingLayoutCandidateView { Id = 7, OriginX = 15, OriginY = 10, Scale = 20 }));

        var result = new DrawingLayoutCandidateApplyService().Execute(
            plan,
            [8],
            DrawingLayoutCandidateApplyExecutionMode.DryRun);

        Assert.False(result.Success);
        Assert.Equal(DrawingLayoutCandidateApplyExecutionReason.MissingRuntimeView, result.Reason);
        Assert.Equal(1, result.MissingRuntimeViewCount);
        Assert.Equal(7, Assert.Single(result.MissingRuntimeViewIds));
    }

    [Fact]
    public void Execute_Apply_InvokesApplyHandler()
    {
        var plan = DrawingLayoutCandidateApplyPlanBuilder.FromEvaluation(CreateEvaluation(
            "fit_views_to_sheet:planned-centered",
            new DrawingLayoutCandidateView { Id = 7, OriginX = 15, OriginY = 10, Scale = 20 }));
        DrawingLayoutCandidateApplyMove? appliedMove = null;

        var result = new DrawingLayoutCandidateApplyService().Execute(
            plan,
            [7],
            DrawingLayoutCandidateApplyExecutionMode.Apply,
            move =>
            {
                appliedMove = move;
                return true;
            });

        Assert.True(result.Success);
        Assert.Equal(DrawingLayoutCandidateApplyExecutionReason.Applied, result.Reason);
        Assert.Equal(1, result.AppliedMoveCount);
        Assert.Equal(7, appliedMove?.ViewId);
    }

    [Theory]
    [InlineData(DrawingLayoutCandidateApplyExecutionReason.DryRun, "dry-run")]
    [InlineData(DrawingLayoutCandidateApplyExecutionReason.Applied, "applied")]
    [InlineData(DrawingLayoutCandidateApplyExecutionReason.PlanNotApplicable, "plan-not-applicable")]
    [InlineData(DrawingLayoutCandidateApplyExecutionReason.MissingRuntimeView, "missing-runtime-view")]
    [InlineData(DrawingLayoutCandidateApplyExecutionReason.MissingApplyHandler, "missing-apply-handler")]
    [InlineData(DrawingLayoutCandidateApplyExecutionReason.ApplyFailed, "apply-failed")]
    public void ExecutionReasonToTraceString_ReturnsStableTraceValue(
        DrawingLayoutCandidateApplyExecutionReason reason,
        string expected)
    {
        Assert.Equal(expected, DrawingLayoutCandidateApplyExecutionReasonFormatter.ToTraceString(reason));
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
