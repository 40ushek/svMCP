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
    }

    [Fact]
    public void SelectBest_ReportsNoCandidates()
    {
        var selection = new DrawingLayoutCandidateSelector().SelectBest([]);

        Assert.Null(selection.Selected);
        Assert.Empty(selection.Evaluations);
        Assert.Contains("candidate-selection:no-candidates", selection.Diagnostics);
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
