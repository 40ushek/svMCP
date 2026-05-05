using TeklaMcpServer.Api.Drawing;
using TeklaMcpServer.Api.Drawing.ViewLayout;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DrawingLayoutCandidateSelectorTests
{
    [Fact]
    public void SelectBest_ChoosesFeasibleCandidate_OverHigherScoringInfeasibleCandidate()
    {
        var feasible = CreateCandidate(
            "feasible",
            new ReservedRect(0, 0, 20, 20));
        var infeasible = CreateCandidate(
            "infeasible",
            new ReservedRect(0, 0, 70, 70),
            new ReservedRect(10, 10, 80, 80));

        var selection = new DrawingLayoutCandidateSelector().SelectBest(
            [infeasible, feasible]);

        Assert.Equal(2, selection.Evaluations.Count);
        Assert.Equal(feasible, selection.Selected?.Candidate);
        Assert.True(selection.Selected?.IsFeasible);
        Assert.Equal(2, selection.Items.Count);
        Assert.Equal(feasible, selection.Items[0].Evaluation.Candidate);
        Assert.Equal(1, selection.Items[0].Rank);
        Assert.True(selection.Items[0].IsSelected);
        Assert.Equal(DrawingLayoutCandidateSelectionReason.Selected, selection.Items[0].Reason);
        Assert.Equal(infeasible, selection.Items[1].Evaluation.Candidate);
        Assert.Equal(2, selection.Items[1].Rank);
        Assert.False(selection.Items[1].IsSelected);
        Assert.Equal(DrawingLayoutCandidateSelectionReason.RejectedFeasibility, selection.Items[1].Reason);
        Assert.Contains("candidate-selection:selected:index=1:name=feasible", selection.Diagnostics);
    }

    [Fact]
    public void SelectBest_PreservesInputOrder_WhenCandidatesTie()
    {
        var first = CreateCandidate(
            "first",
            new ReservedRect(0, 0, 20, 20));
        var second = CreateCandidate(
            "second",
            new ReservedRect(0, 0, 20, 20));

        var selection = new DrawingLayoutCandidateSelector().SelectBest(
            [first, second]);

        Assert.Equal(first, selection.Selected?.Candidate);
        Assert.Equal(DrawingLayoutCandidateSelectionReason.RejectedInputOrder, selection.Items[1].Reason);
    }

    [Fact]
    public void SelectBest_ReportsNoCandidates()
    {
        var selection = new DrawingLayoutCandidateSelector().SelectBest([]);

        Assert.Null(selection.Selected);
        Assert.Empty(selection.Evaluations);
        Assert.Empty(selection.Items);
        Assert.Contains("candidate-selection:no-candidates", selection.Diagnostics);
    }

    [Fact]
    public void SelectionReasonFormatter_ReturnsStableTraceStrings()
    {
        Assert.Equal(
            "selected",
            DrawingLayoutCandidateSelectionReasonFormatter.ToTraceString(DrawingLayoutCandidateSelectionReason.Selected));
        Assert.Equal(
            "rejected-feasibility",
            DrawingLayoutCandidateSelectionReasonFormatter.ToTraceString(DrawingLayoutCandidateSelectionReason.RejectedFeasibility));
        Assert.Equal(
            "rejected-score",
            DrawingLayoutCandidateSelectionReasonFormatter.ToTraceString(DrawingLayoutCandidateSelectionReason.RejectedScore));
        Assert.Equal(
            "rejected-input-order",
            DrawingLayoutCandidateSelectionReasonFormatter.ToTraceString(DrawingLayoutCandidateSelectionReason.RejectedInputOrder));
    }

    private static DrawingLayoutCandidate CreateCandidate(
        string name,
        params ReservedRect[] viewRects)
    {
        var candidate = new DrawingLayoutCandidate
        {
            Name = name,
            Sheet = new DrawingSheetContext
            {
                Width = 100,
                Height = 100
            }
        };

        for (var i = 0; i < viewRects.Length; i++)
        {
            var rect = viewRects[i];
            candidate.Views.Add(new DrawingLayoutCandidateView
            {
                Id = i + 1,
                ViewType = "FrontView",
                SemanticKind = "BaseProjected",
                Scale = 20,
                Width = rect.Width,
                Height = rect.Height,
                LayoutRect = rect
            });
        }

        return candidate;
    }
}
