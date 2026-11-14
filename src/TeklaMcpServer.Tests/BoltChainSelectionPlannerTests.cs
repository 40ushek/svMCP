using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class BoltChainSelectionPlannerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MainPartReplacementAndFallbackDoNotDependOnCandidateOrder(bool positionOutside)
    {
        var position = Candidate("position", BoltChainCandidate.PositionKind, startX: -10);
        var plate = Candidate("plate", BoltChainCandidate.PartKind, axis: 1, over: "cross");
        var cross = Candidate("cross", BoltChainCandidate.PartKind, axis: 1, main: true, requires: "position");
        var candidates = new[] { position, plate, cross };
        var orders = new[] { new[] { 0, 1, 2 }, [0, 2, 1], [1, 0, 2], [1, 2, 0], [2, 0, 1], [2, 1, 0] };
        foreach (var order in orders)
        {
            var evaluated = new BoltChainRule(new BoltChainSettings()).Evaluate(order.Select(i => candidates[i]).ToArray());
            var selected = BoltChainSelectionPlanner.Select(evaluated, null, point => positionOutside && point.X < 0)
                .ToDictionary(decision => decision.Candidate.Key);
            Assert.Equal(!positionOutside, selected["position"].Create);
            Assert.Equal(!positionOutside, selected["cross"].Create);
            Assert.Equal(positionOutside, selected["plate"].Create);
            if (positionOutside)
                Assert.Equal("created only with position, which is not created", selected["cross"].Reason);
            else
                Assert.Equal("the same bolts are dimensioned from the main part by cross", selected["plate"].Reason);
        }
    }

    [Fact]
    public void PositionCoverageTakesPrecedenceOverAnEdgelessRefusal()
    {
        var candidates = new[] {
            Candidate("position", BoltChainCandidate.PositionKind),
            Candidate("plate", BoltChainCandidate.PartKind, builtRefusal: ViewDimensionContext.BoltEdgeless)
        };
        var evaluated = new BoltChainRule(new BoltChainSettings()).Evaluate(candidates);
        var selected = BoltChainSelectionPlanner.Select(evaluated, null, _ => false);
        Assert.True(selected[0].Create);
        Assert.False(selected[1].Create);
        Assert.Equal(ViewDimensionContext.BoltEdgeless, selected[1].PolicyRefusal);
        Assert.Equal("bolt positions are given by position", selected[1].Reason);
    }

    [Fact]
    public void AnIncompleteReadKeepsDiagnosticsWithoutOccupyingAnnotationRows()
    {
        var evaluated = new BoltChainRule(new BoltChainSettings()).Evaluate([
            Candidate("first", BoltChainCandidate.PartKind), Candidate("second", BoltChainCandidate.PartKind)
        ]);
        var selected = BoltChainSelectionPlanner.Select(evaluated, "bolt geometry was read incompletely", _ => false);
        var occupied = new List<(double Nx, double Ny, double Line, double From, double To)>();
        var placed = BoltChainRowPlanner.Place(selected, occupied, 80);
        Assert.Empty(occupied);
        Assert.Equal(2, placed.Count);
        Assert.All(placed, chain => {
            Assert.False(chain.Create);
            Assert.Equal("bolt geometry was read incompletely", chain.Reason);
            Assert.Equal(80, chain.Distance);
        });
    }

    [Fact]
    public void CapturedSourcesKeepTheirPrecisionWhileDisplayPointsAreRounded()
    {
        const double x = 20.123456789;
        var group = new BoltGroupGeometry { ModelId = 42, Shape = "BoltArray", Axis = [0, 0, 1],
            PartToBeBoltedId = 10, FirstPosition = [x, 30.654321, 0], SecondPosition = [x + 60, 30.654321, 0],
            Positions = [new() { Index = 0, Point = [x, 30.654321, 0] }, new() { Index = 1, Point = [x + 60, 30.654321, 0] }] };
        var snapshot = ViewBoltGeometrySnapshot.Capture(7, [10], id => new() {
            Success = true, ViewId = 7, PartId = id, BoltGroups = [group]
        }, new DepthBox(-1000, -1000, -1000, 1000, 1000, 1000), point => point, true);
        var context = DimensionChainPreviewTests.Framed("FrontView", snapshot, null,
            DimensionChainPreviewTests.Solid(10, 0, 260, 0, 340));
        var chain = context.Query("chain").GetProperty("chainPreview").EnumerateArray()
            .Single(row => row.GetProperty("side").GetString() == "Bolt").GetProperty("chains").EnumerateArray()
            .Single(item => item.GetProperty("key").GetString()!.EndsWith("-Bottom"));
        Assert.Equal(x, chain.GetProperty("sources")[0].GetProperty("point")[0].GetDouble());
        Assert.Equal(Math.Round(x, 3), chain.GetProperty("points")[1][0].GetDouble());
    }

    private static BoltChainCandidate Candidate(string key, string kind, int axis = 0, bool main = false,
        string? over = null, string? requires = null, string? builtRefusal = null, double startX = 0) =>
        new(key, kind, 42, main ? 10 : 11, main, BoltGroupViewClass.Plan, axis,
            [new("start", startX, 0, []), new("end", axis == 0 ? 100 : startX, axis == 0 ? 0 : 100, [])],
            [100], builtRefusal, true, axis == 0 ? new(0, 1) : new(1, 0), 0, BoltChainSources.Empty,
            overMainPartChain: over, requiresChain: requires);
}
