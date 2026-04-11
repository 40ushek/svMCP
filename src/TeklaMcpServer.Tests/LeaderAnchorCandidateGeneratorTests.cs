using System.Linq;
using TeklaMcpServer.Api.Algorithms.Geometry;
using TeklaMcpServer.Api.Algorithms.Marks;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class LeaderAnchorCandidateGeneratorTests
{
    [Fact]
    public void CreateCandidates_OnRegularEdge_ReturnsThreeCandidatesOnSameEdge()
    {
        var polygon = CreateRectangle(width: 100.0, height: 50.0);
        var snapshot = CreateSnapshot(anchorX: 90.0, anchorY: 25.0, leaderEndX: 140.0, leaderEndY: 25.0);

        var candidates = LeaderAnchorCandidateGenerator.CreateCandidates(
            polygon,
            snapshot,
            depthMm: 10.0,
            minFarEdgeClearanceMm: 5.0);

        Assert.Equal(3, candidates.Count);
        Assert.All(candidates, candidate => Assert.Equal(1, candidate.EdgeIndex));
        Assert.Contains(candidates, candidate => candidate.Kind == LeaderAnchorCandidateKind.Nearest);
        Assert.Contains(candidates, candidate => candidate.Kind == LeaderAnchorCandidateKind.ShiftedLeft);
        Assert.Contains(candidates, candidate => candidate.Kind == LeaderAnchorCandidateKind.ShiftedRight);
    }

    [Fact]
    public void CreateCandidates_NearCorner_ClampsEdgePointsAwayFromVertex()
    {
        var polygon = CreateRectangle(width: 100.0, height: 50.0);
        var snapshot = CreateSnapshot(anchorX: 90.0, anchorY: 2.0, leaderEndX: 140.0, leaderEndY: 2.0);

        var candidates = LeaderAnchorCandidateGenerator.CreateCandidates(
            polygon,
            snapshot,
            depthMm: 10.0,
            minFarEdgeClearanceMm: 5.0);

        Assert.Equal(3, candidates.Count);
        Assert.All(candidates, candidate =>
        {
            Assert.True(candidate.EdgePoint!.Y > 0.0);
            Assert.True(candidate.EdgePoint.Y < 50.0);
        });
    }

    [Fact]
    public void CreateCandidates_ThinDetail_KeepsAnchorsInsidePolygon()
    {
        var polygon = CreateRectangle(width: 6.0, height: 40.0);
        var snapshot = CreateSnapshot(anchorX: 5.0, anchorY: 20.0, leaderEndX: 40.0, leaderEndY: 20.0);

        var candidates = LeaderAnchorCandidateGenerator.CreateCandidates(
            polygon,
            snapshot,
            depthMm: 10.0,
            minFarEdgeClearanceMm: 2.0);

        Assert.Equal(3, candidates.Count);
        Assert.All(candidates, candidate =>
            Assert.True(PolygonGeometry.ContainsPoint(
                polygon,
                candidate.AnchorPoint!.X,
                candidate.AnchorPoint.Y)));
    }

    [Fact]
    public void CreateCandidates_PreservesFarEdgeClearanceMetrics()
    {
        var polygon = CreateRectangle(width: 12.0, height: 40.0);
        var snapshot = CreateSnapshot(anchorX: 10.0, anchorY: 20.0, leaderEndX: 30.0, leaderEndY: 20.0);

        var candidates = LeaderAnchorCandidateGenerator.CreateCandidates(
            polygon,
            snapshot,
            depthMm: 10.0,
            minFarEdgeClearanceMm: 5.0);

        Assert.Equal(3, candidates.Count);
        Assert.All(candidates, candidate => Assert.True(candidate.FarEdgeClearance >= 5.0 - 0.0001));
        Assert.Contains(candidates, candidate => candidate.Kind == LeaderAnchorCandidateKind.Nearest && candidate.AnchorPoint!.X == 5.0);
    }

    private static LeaderSnapshot CreateSnapshot(double anchorX, double anchorY, double leaderEndX, double leaderEndY)
    {
        return new LeaderSnapshot
        {
            MarkId = 1,
            AnchorPoint = new DrawingPointInfo { X = anchorX, Y = anchorY },
            LeaderEndPoint = new DrawingPointInfo { X = leaderEndX, Y = leaderEndY },
            InsertionPoint = new DrawingPointInfo { X = leaderEndX + 10.0, Y = leaderEndY + 5.0 },
        };
    }

    private static double[][] CreateRectangle(double width, double height)
    {
        return
        [
            [0.0, 0.0],
            [width, 0.0],
            [width, height],
            [0.0, height],
        ];
    }
}
