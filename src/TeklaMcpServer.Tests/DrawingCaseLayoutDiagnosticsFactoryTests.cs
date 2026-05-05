using System.Linq;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DrawingCaseLayoutDiagnosticsFactoryTests
{
    [Fact]
    public void FromSelection_MapsSelectedCandidateAndApplySummaries()
    {
        var selection = new DrawingLayoutCandidateSelection
        {
            Selected = new DrawingLayoutCandidateEvaluation
            {
                Candidate = new DrawingLayoutCandidate
                {
                    Name = "fit_views_to_sheet:planned-centered",
                    Diagnostics = { "candidate:source=planned-centered" }
                },
                Score = new DrawingLayoutScore
                {
                    TotalScore = 0.75,
                    Diagnostics = { "score:uniform-scale-no-non-detail-views" }
                },
                Validation = new DrawingLayoutCandidateValidation
                {
                    Diagnostics =
                    {
                        "candidate:source=planned-centered",
                        "score:uniform-scale-no-non-detail-views"
                    }
                }
            },
            Diagnostics = { "candidate-selection:selected:index=1:name=fit_views_to_sheet:planned-centered" }
        };
        var applyPlan = new DrawingLayoutCandidateApplyPlan
        {
            CandidateName = "fit_views_to_sheet:planned-centered",
            CanApply = true,
            Reason = DrawingLayoutCandidateApplyPlanReason.PlannedCandidate,
            Moves =
            {
                new DrawingLayoutCandidateApplyMove { ViewId = 7 },
                new DrawingLayoutCandidateApplyMove { ViewId = 8 }
            }
        };
        var applyDelta = new DrawingLayoutCandidateApplyDeltaSummary
        {
            MoveCount = 2,
            ComparableMoveCount = 2,
            MissingBaselineCount = 0,
            MovedCount = 1,
            ScaleChangedCount = 1,
            MaxDelta = 10,
            AverageDelta = 5
        };
        var applySafety = new DrawingLayoutCandidateApplySafetyDecision
        {
            RequestedMode = DrawingLayoutCandidateApplyExecutionMode.Apply,
            EffectiveMode = DrawingLayoutCandidateApplyExecutionMode.DryRun,
            Reason = DrawingLayoutCandidateApplySafetyDecisionReason.ScaleChanged
        };

        var diagnostics = DrawingCaseLayoutDiagnosticsFactory.FromSelection(
            selection,
            applyPlan,
            applyDelta,
            applySafety);

        Assert.Equal("fit_views_to_sheet:planned-centered", diagnostics.SelectedCandidateName);
        Assert.Equal(0.75, diagnostics.SelectedCandidateScore);
        Assert.True(diagnostics.SelectedCandidateFeasible);
        Assert.True(diagnostics.ApplyPlan?.CanApply);
        Assert.Equal("planned-candidate", diagnostics.ApplyPlan?.Reason);
        Assert.Equal(2, diagnostics.ApplyPlan?.MoveCount);
        Assert.Equal(1, diagnostics.ApplyDelta?.MovedCount);
        Assert.Equal(1, diagnostics.ApplyDelta?.ScaleChangedCount);
        Assert.Equal("Apply", diagnostics.ApplySafety?.RequestedMode);
        Assert.Equal("DryRun", diagnostics.ApplySafety?.EffectiveMode);
        Assert.False(diagnostics.ApplySafety?.Allowed);
        Assert.Equal("scale-changed", diagnostics.ApplySafety?.Reason);
        Assert.Contains(
            "candidate-selection:selected:index=1:name=fit_views_to_sheet:planned-centered",
            diagnostics.Diagnostics);
        Assert.Contains("candidate:source=planned-centered", diagnostics.Diagnostics);
        Assert.Equal(diagnostics.Diagnostics.Count, diagnostics.Diagnostics.Distinct().Count());
    }

    [Fact]
    public void FromSelection_UsesApplyPlanName_WhenNoCandidateSelected()
    {
        var selection = new DrawingLayoutCandidateSelection
        {
            Diagnostics = { "candidate-selection:no-candidates" }
        };
        var applyPlan = new DrawingLayoutCandidateApplyPlan
        {
            CandidateName = "fit_views_to_sheet:planned-centered",
            Reason = DrawingLayoutCandidateApplyPlanReason.NoSelectedCandidate
        };

        var diagnostics = DrawingCaseLayoutDiagnosticsFactory.FromSelection(
            selection,
            applyPlan);

        Assert.Equal("fit_views_to_sheet:planned-centered", diagnostics.SelectedCandidateName);
        Assert.Null(diagnostics.SelectedCandidateScore);
        Assert.Null(diagnostics.SelectedCandidateFeasible);
        Assert.False(diagnostics.ApplyPlan?.CanApply);
        Assert.Equal("no-selected-candidate", diagnostics.ApplyPlan?.Reason);
        Assert.Contains("candidate-selection:no-candidates", diagnostics.Diagnostics);
    }
}
