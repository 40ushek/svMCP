using System.Text.Json;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class BoltLocationChainComposerTests
{
    [Fact]
    public void CarryRestoresTheOverallAndRetainsEverySourceReference()
    {
        var rows = Rows([20, 80]);
        var composed = Compose(rows);
        Assert.False(rows[0].Chains[0].Incomplete);
        Assert.True(composed[0].Chains[0].Incomplete);
        var overall = composed[0].Chains[1];
        Assert.False(overall.Incomplete);
        Assert.Equal("Bottom-location-bolts", overall.RestoredBesides);
        var carried = Assert.Single(composed[1].Chains, chain => chain.Carries != null);
        Assert.Equal(new[] { 20d, 60, 20 }, carried.Segments);
        Assert.Equal(80, carried.Distance);
        var sources = carried.Sources.CapturedPositions;
        Assert.Equal(new[] { 0, 1 }, sources.SelectMany(position => position.Sources).Select(source => source.Index));
        Assert.All(sources.SelectMany(position => position.Sources), source => Assert.Equal(42, source.GroupId));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(101)]
    public void BoltsOutsideOrOnTheLocationEndsDoNotChangeAnyChain(double x)
    {
        var rows = Rows([x]);
        Assert.Same(rows, Compose(rows));
        Assert.False(rows[0].Chains[0].Incomplete);
        Assert.True(rows[0].Chains[1].Incomplete);
    }

    [Fact]
    public void AnIncompleteBoltReadNeverResolvesPointsOrPlacesACarriedLine()
    {
        var rows = Rows([20, 80]);
        rows[1] = new DimensionPreviewRow("Bolt", rows[1].Chains, "incomplete read");
        Assert.Same(rows, BoltLocationChainComposer.Compose(rows, 0.01,
            (_, _) => throw new InvalidOperationException("unexpected source resolution"),
            (_, _) => throw new InvalidOperationException("unexpected placement")));
    }

    [Fact]
    public void RestoredOverallIsComparedWithTheSheetAfterCarry()
    {
        var rows = Rows([20, 80]);
        Func<IReadOnlyList<DimensionChainRepeats.Placed>> sheet = () => [new(99, 2, "FrontView", [100], true)];
        var initial = DimensionPreviewRepeatPlanner.Structural(7, "FrontView", rows, sheet, false, null);
        var carried = Compose(initial.Rows);
        var final = DimensionPreviewRepeatPlanner.Carried(7, "FrontView", carried, sheet, false, initial.Repeats);
        Assert.True(final.Publish);
        var overall = final.Rows[0].Chains[1];
        Assert.True(overall.Incomplete);
        Assert.Equal("overallOnSheet", overall.Repeat);
        Assert.Contains(final.Repeats!, repeat => repeat.Key == "Bottom-overall" && repeat.DimensionId == 99);
        var stored = DimensionPreviewRepeatPlanner.Carried(7, "FrontView", carried, null, true, final.Repeats);
        Assert.Equal(overall.Reason, stored.Rows[0].Chains[1].Reason);
        Assert.False(stored.Publish);
    }

    [Fact]
    public void PlainStructuralReadClearsCachedRepeats()
    {
        var plan = DimensionPreviewRepeatPlanner.Structural(7, "FrontView", Rows([20]), null, false,
            [new DimensionChainRepeats.Repeat("Bottom-location", "mirror", dimensionId: 1, viewId: 2)]);
        Assert.Null(plan.Repeats);
        Assert.False(plan.Publish);
        Assert.False(plan.Rows[0].Chains[0].Incomplete);
    }

    [Fact]
    public void ProjectionKeepsCarriedSourcePrecisionAndThePublicOutputShape()
    {
        const double x = 20.123456789;
        var composed = Compose(Rows([x, 80]));
        var answer = ViewDimensionContext.Freeze(composed, display: true);
        var carried = answer[1].GetProperty("chains").EnumerateArray().Single(chain => chain.TryGetProperty("carries", out _));
        Assert.Equal(x, carried.GetProperty("sources")[0].GetProperty("point")[0].GetDouble());
        Assert.Equal(Math.Round(x, 3), carried.GetProperty("points")[1][0].GetDouble());
        Assert.Equal("Bottom-location", carried.GetProperty("carries").GetString());
        Assert.False(carried.GetProperty("incomplete").GetBoolean());
    }

    [Fact]
    public void RepeatProjectionPreservesTheLegacyPrecisionOfFrozenCarriedPoints()
    {
        const double x = 20.123456789;
        var composed = Compose(Rows([x, 80]));
        var plan = DimensionPreviewRepeatPlanner.Carried(7, "BackView", composed,
            () => [new DimensionChainRepeats.Placed(99, 2, "FrontView", [20.123, 59.877, 20], true)], false, []);
        var answer = ViewDimensionContext.Freeze(plan.Rows, display: true);
        var carried = answer[1].GetProperty("chains").EnumerateArray().Single(chain => chain.TryGetProperty("carries", out _));
        Assert.Equal("mirror", carried.GetProperty("repeat").GetString());
        Assert.True(carried.GetProperty("incomplete").GetBoolean());
        Assert.Equal(x, carried.GetProperty("points")[1][0].GetDouble());
    }

    [Fact]
    public void LiveRepeatPlannerExcludesOrdinaryBoltChainsWhileMatchingStructuralChains()
    {
        var ordinary = new DimensionPreviewChain("ordinary-bolt", "bolt", "horizontal-down", "standard",
            [], [100], false, null);
        DimensionPreviewRow[] rows = [new("Bottom", [Structural("location", false, null)]), new("Bolt", [ordinary])];
        Func<IReadOnlyList<DimensionChainRepeats.Placed>> sheet = () => [new(99, 2, "FrontView", [100], true)];
        var structural = DimensionPreviewRepeatPlanner.Structural(7, "BackView", rows, sheet, false, null);
        var final = DimensionPreviewRepeatPlanner.Carried(7, "BackView", structural.Rows, sheet, false, structural.Repeats);
        Assert.Equal("Bottom-location", Assert.Single(final.Repeats!).Key);
        Assert.Equal("mirror", final.Rows[0].Chains[0].Repeat);
        Assert.Equal(ordinary.Key, final.Rows[1].Chains[0].Key);
        Assert.False(final.Rows[1].Chains[0].Incomplete);
        Assert.Null(final.Rows[1].Chains[0].Repeat);
    }

    [Fact]
    public void NonCapturedSourcesExposeNoCapturedPositions()
    {
        Assert.Empty(BoltChainSources.Empty.CapturedPositions);
        Assert.Empty(BoltChainSources.Points([[1, 2]]).CapturedPositions);
        Assert.Empty(BoltChainSources.Indices([[1, 2]]).CapturedPositions);
        var position = new BoltProjectedPosition([1, 2, 0], [new(42, 0)]);
        Assert.Same(position, Assert.Single(BoltChainSources.Captured([position]).CapturedPositions));
    }

    private static DimensionPreviewRow[] Compose(DimensionPreviewRow[] rows) =>
        BoltLocationChainComposer.Compose(rows, 0.01, (_, _) => [0, 0, 0, 100, 0, 0], (_, _) => 80);

    private static DimensionPreviewRow[] Rows(double[] bolts)
    {
        var location = Structural("location", false, null);
        var overall = Structural("overall", true, DimensionChainPreview.OverallSpanned);
        var sources = bolts.Select((x, index) => new BoltProjectedPosition([x, 0, 0], [new(42, index)])).ToArray();
        var candidate = new BoltChainCandidate("bolt-part-11-groups-42-Bottom", BoltChainCandidate.PartKind, 42, 11, false,
            BoltGroupViewClass.Plan, 0, [], [], ViewDimensionContext.BoltEdgeless, false, new(0, -1), 0,
            BoltChainSources.Captured(sources));
        return [new DimensionPreviewRow("Bottom", [location, overall]),
            new DimensionPreviewRow("Bolt", [DimensionPreviewChain.Bolt(new BoltRowChain(new BoltChainDecision(candidate,
                ViewDimensionContext.BoltEdgeless), 80))])];
    }

    private static DimensionPreviewChain Structural(string kind, bool incomplete, string? reason)
    {
        var template = new Dictionary<string, object?> { ["key"] = "Bottom-" + kind, ["direction"] = "horizontal-down",
            ["attributesFile"] = kind == "overall" ? "overall" : "standard", ["kind"] = kind,
            ["pointIds"] = new[] { "a", "b" }, ["segments"] = new[] { 100d }, ["incomplete"] = incomplete };
        if (reason != null) template["incompleteReason"] = reason;
        return new DimensionPreviewChain("Bottom-" + kind, kind, "horizontal-down", kind == "overall" ? "overall" : "standard",
            ["a", "b"], [100], incomplete, reason, template);
    }
}
