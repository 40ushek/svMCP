using System.Collections.Generic;
using TeklaMcpServer.Api.Algorithms.Marks;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class SimpleMarkCostEvaluatorTests
{
    [Fact]
    public void EvaluateCandidate_PrefersCandidateCloserToSourceCenter_ForNonLeaderMark()
    {
        var evaluator = new SimpleMarkCostEvaluator();
        var item = new MarkLayoutItem
        {
            Id = 1,
            CurrentX = 100,
            CurrentY = 100,
            AnchorX = 100,
            AnchorY = 100,
            Width = 20,
            Height = 10,
            SourceCenterX = 200,
            SourceCenterY = 100,
            CanMove = true
        };
        var options = new MarkLayoutOptions
        {
            CurrentPositionWeight = 0,
            AnchorDistanceWeight = 0,
            SourceDistanceWeight = 1.0,
            CandidatePriorityWeight = 0,
            CrowdingPenaltyWeight = 0,
            PreferredSidePenaltyWeight = 0,
            LeaderLengthWeight = 0,
            OverlapPenalty = 0
        };

        var near = evaluator.EvaluateCandidate(item, new MarkCandidate { X = 180, Y = 100 }, new List<MarkLayoutPlacement>(), options);
        var far = evaluator.EvaluateCandidate(item, new MarkCandidate { X = 120, Y = 100 }, new List<MarkLayoutPlacement>(), options);

        Assert.True(near < far);
    }

    [Fact]
    public void EvaluateCandidate_IgnoresSourceCenter_ForLeaderLineMark()
    {
        var evaluator = new SimpleMarkCostEvaluator();
        var item = new MarkLayoutItem
        {
            Id = 1,
            CurrentX = 100,
            CurrentY = 100,
            AnchorX = 100,
            AnchorY = 100,
            Width = 20,
            Height = 10,
            HasLeaderLine = true,
            SourceCenterX = 1000,
            SourceCenterY = 1000,
            CanMove = true
        };
        var options = new MarkLayoutOptions
        {
            CurrentPositionWeight = 0,
            AnchorDistanceWeight = 0,
            SourceDistanceWeight = 10.0,
            CandidatePriorityWeight = 0,
            CrowdingPenaltyWeight = 0,
            PreferredSidePenaltyWeight = 0,
            LeaderLengthWeight = 0,
            OverlapPenalty = 0
        };

        var left = evaluator.EvaluateCandidate(item, new MarkCandidate { X = 80, Y = 100 }, new List<MarkLayoutPlacement>(), options);
        var right = evaluator.EvaluateCandidate(item, new MarkCandidate { X = 120, Y = 100 }, new List<MarkLayoutPlacement>(), options);

        Assert.Equal(left, right, 6);
    }
}
