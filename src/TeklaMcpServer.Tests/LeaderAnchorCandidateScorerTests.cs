using System.Collections.Generic;
using TeklaMcpServer.Api.Algorithms.Marks;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class LeaderAnchorCandidateScorerTests
{
    [Fact]
    public void SelectBestCandidate_PrefersShorterLineLength()
    {
        var best = LeaderAnchorCandidateScorer.SelectBestCandidate(
        [
            CreateCandidate(LeaderAnchorCandidateKind.ShiftedLeft, lineLength: 20.0, cornerDistance: 5.0, farEdgeClearance: 8.0),
            CreateCandidate(LeaderAnchorCandidateKind.Nearest, lineLength: 10.0, cornerDistance: 1.0, farEdgeClearance: 2.0),
        ]);

        Assert.NotNull(best);
        Assert.Equal(LeaderAnchorCandidateKind.Nearest, best!.Kind);
    }

    [Fact]
    public void SelectBestCandidate_PrefersLargerCornerDistance_WhenLengthsMatch()
    {
        var best = LeaderAnchorCandidateScorer.SelectBestCandidate(
        [
            CreateCandidate(LeaderAnchorCandidateKind.Nearest, lineLength: 10.0, cornerDistance: 2.0, farEdgeClearance: 4.0),
            CreateCandidate(LeaderAnchorCandidateKind.ShiftedLeft, lineLength: 10.0, cornerDistance: 6.0, farEdgeClearance: 4.0),
        ]);

        Assert.NotNull(best);
        Assert.Equal(LeaderAnchorCandidateKind.ShiftedLeft, best!.Kind);
    }

    [Fact]
    public void SelectBestCandidate_PrefersLargerFarEdgeClearance_WhenLengthAndCornerDistanceMatch()
    {
        var best = LeaderAnchorCandidateScorer.SelectBestCandidate(
        [
            CreateCandidate(LeaderAnchorCandidateKind.Nearest, lineLength: 10.0, cornerDistance: 4.0, farEdgeClearance: 3.0),
            CreateCandidate(LeaderAnchorCandidateKind.ShiftedRight, lineLength: 10.0, cornerDistance: 4.0, farEdgeClearance: 7.0),
        ]);

        Assert.NotNull(best);
        Assert.Equal(LeaderAnchorCandidateKind.ShiftedRight, best!.Kind);
    }

    [Fact]
    public void SelectBestCandidate_UsesStableKindTiebreaker()
    {
        var best = LeaderAnchorCandidateScorer.SelectBestCandidate(
        [
            CreateCandidate(LeaderAnchorCandidateKind.ShiftedRight, lineLength: 10.0, cornerDistance: 4.0, farEdgeClearance: 7.0),
            CreateCandidate(LeaderAnchorCandidateKind.Nearest, lineLength: 10.0, cornerDistance: 4.0, farEdgeClearance: 7.0),
            CreateCandidate(LeaderAnchorCandidateKind.ShiftedLeft, lineLength: 10.0, cornerDistance: 4.0, farEdgeClearance: 7.0),
        ]);

        Assert.NotNull(best);
        Assert.Equal(LeaderAnchorCandidateKind.Nearest, best!.Kind);
    }

    private static LeaderAnchorCandidate CreateCandidate(
        LeaderAnchorCandidateKind kind,
        double lineLength,
        double cornerDistance,
        double farEdgeClearance)
    {
        return new LeaderAnchorCandidate
        {
            Kind = kind,
            AnchorPoint = new DrawingPointInfo { X = 1, Y = 2 },
            LineLengthToLeaderEnd = lineLength,
            CornerDistance = cornerDistance,
            FarEdgeClearance = farEdgeClearance,
        };
    }
}
