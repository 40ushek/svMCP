using System.Text.Json;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class BoltDimensionChainPreviewTests
{
    [Fact]
    public void RectangleBuildsIndependentRowsAndColumnsInCoordinateOrder()
    {
        var group = Rectangle();
        group.Positions.Reverse();
        var result = Preview(group);
        var chains = result.GetProperty("groups")[0].GetProperty("chains");
        Assert.Equal(4, chains.GetArrayLength());
        Assert.All(chains.EnumerateArray(), chain => {
            Assert.Equal("Candidate", chain.GetProperty("state").GetString());
            Assert.Equal("explicitSelectionOnly", chain.GetProperty("creationMode").GetString());
            Assert.Equal(2, chain.GetProperty("points").GetArrayLength());
        });
        Assert.Equal(new[] { 3, 8 }, Indices(chains[0]));
        Assert.Equal(60, chains[0].GetProperty("segments")[0].GetDouble());
        Assert.Equal(40, chains[2].GetProperty("segments")[0].GetDouble());
        Assert.Equal("explicitSelectionOnly", result.GetProperty("creationMode").GetString());
        Assert.False(result.GetProperty("selectionComplete").GetBoolean());
        Assert.False(result.GetProperty("visibilityVerified").GetBoolean());
    }

    [Fact]
    public void DuplicateProjectedCentersRetainAllSourceIndices()
    {
        var group = Rectangle();
        group.Positions.Add(new() { Index = 99, Point = [10.005, 20.005, 0] });
        var result = Preview(group).GetProperty("groups")[0];
        var source = result.GetProperty("chains")[0].GetProperty("points")[0].GetProperty("sourceIndices");
        Assert.Equal(new[] { 3, 99 }, source.EnumerateArray().Select(value => value.GetInt32()));
        Assert.Equal("Merged", result.GetProperty("points").EnumerateArray()
            .Single(point => point.GetProperty("index").GetInt32() == 99).GetProperty("state").GetString());
    }

    [Theory]
    [InlineData("outside")]
    [InlineData("unresolved")]
    public void RestrictionFailureBlocksWholeRowInsteadOfTrimmingIt(string mode)
    {
        var group = Rectangle();
        var result = Preview(group, mode).GetProperty("groups")[0];
        var chains = result.GetProperty("chains");
        if (mode == "unresolved") Assert.Equal("Blocked", result.GetProperty("status").GetString());
        Assert.Equal("Blocked", chains[0].GetProperty("state").GetString());
        Assert.Equal(new[] { 3, 8 }, Indices(chains[0]));
        Assert.Equal(60, chains[0].GetProperty("segments")[0].GetDouble());
        Assert.Contains(3, chains[0].GetProperty("blockedIndices").EnumerateArray().Select(v => v.GetInt32()));
    }

    [Theory]
    [InlineData("skewed", "skewed")]
    [InlineData("depth", "view depth")]
    [InlineData("staggered", "staggered")]
    [InlineData("missing-cell", "incomplete")]
    [InlineData("circle", "BoltArray")]
    [InlineData("direction", "stable XY")]
    public void UnsupportedPatternsAreReportedAndNeverFlattened(string mode, string reason)
    {
        var group = Rectangle();
        switch (mode)
        {
            case "skewed": group.SecondPosition = [70, 80, 0]; break;
            case "depth": group.Positions[0].Point[2] = 30; break;
            case "staggered": group.Positions[2].Point[0] += 15; break;
            case "missing-cell": group.Positions.RemoveAt(3); break;
            case "circle": group.Shape = "BoltCircle"; break;
            case "direction": group.SecondPosition = group.FirstPosition.ToArray(); break;
        }
        var answer = Preview(group).GetProperty("groups")[0];
        Assert.Equal("Blocked", answer.GetProperty("status").GetString());
        Assert.Contains(reason, answer.GetProperty("reason").GetString());
        Assert.Empty(answer.GetProperty("chains").EnumerateArray());
        Assert.All(answer.GetProperty("points").EnumerateArray(), point =>
            Assert.Equal("Blocked", point.GetProperty("state").GetString()));
    }

    [Fact]
    public void SingleRowUsesActualUnequalSpacingAndHasNoInventedVerticalChain()
    {
        var group = Rectangle();
        group.Positions = [new() { Index = 9, Point = [10, 20, 0] },
            new() { Index = 2, Point = [40, 20, 0] }, new() { Index = 7, Point = [90, 20, 0] }];
        var chain = Assert.Single(Preview(group).GetProperty("groups")[0].GetProperty("chains").EnumerateArray());
        Assert.Equal(new[] { 9, 2, 7 }, Indices(chain));
        Assert.Equal(new[] { 30d, 50d }, chain.GetProperty("segments").EnumerateArray().Select(v => v.GetDouble()));
    }

    [Fact]
    public void ASharedGroupDoesNotInventPartOwnership()
    {
        var answer = Preview(Rectangle(), includedParts: [10, 20]).GetProperty("groups")[0];
        Assert.Equal(new[] { 10, 20 }, answer.GetProperty("partCandidates").EnumerateArray().Select(v => v.GetInt32()));
        Assert.StartsWith("unresolved", answer.GetProperty("partScopeStatus").GetString());
        Assert.False(answer.TryGetProperty("partId", out _));
    }

    [Fact]
    public void EmptySnapshotAndFailedReadsStayExplicit()
    {
        var snapshot = ViewBoltGeometrySnapshot.Capture(7, [10], id =>
            new() { ViewId = 7, PartId = id, Error = "read failed" }, null, point => point, true);
        var result = Freeze(BoltDimensionChainPreview.Build(snapshot.Answer, [10], AllSides));
        Assert.False(result.GetProperty("geometryReadComplete").GetBoolean());
        Assert.Single(result.GetProperty("unread").EnumerateArray());
        Assert.Empty(result.GetProperty("groups").EnumerateArray());
    }

    [Fact]
    public void ContextQueryReusesSnapshotAndIsNotAWriteReference()
    {
        var group = Rectangle();
        var snapshot = Snapshot(group);
        var context = ViewDimensionContextTests.Context(bolts: snapshot);
        var query = DrawingCommandParsers.NormalizePreviewQuestions("boltChains,scale");
        Assert.Equal("boltchains,scale", query);
        var first = context.Query(query, "Top,Bottom");
        group.Positions.Clear();
        Assert.Equal(first.GetRawText(), context.Query(query, "Top,Bottom").GetRawText());
        var chains = first.GetProperty("boltChainPreview").GetProperty("groups")[0].GetProperty("chains");
        Assert.Equal(2, chains.GetArrayLength());
        Assert.All(chains.EnumerateArray(), chain => {
            Assert.Equal("X", chain.GetProperty("axis").GetString());
            Assert.False(chain.TryGetProperty("key", out _));
            Assert.False(chain.TryGetProperty("pointIds", out _));
        });
        Assert.False(context.Query("all").TryGetProperty("boltChainPreview", out _));
        Assert.Throws<ArgumentException>(() => DrawingCommandParsers.ParseCreateDimensionsBatchRequest(["create_dimensions_batch", "7", context.ContextId, "[]", "steel", "boltChains"]));
    }

    [Fact]
    public void CoordinateClusteringDoesNotChainTogetherDistantPoints()
    {
        var group = Rectangle();
        group.Positions = [new() { Index = 1, Point = [10, 20, 0] },
            new() { Index = 2, Point = [10.09, 20, 0] }, new() { Index = 3, Point = [10.18, 20, 0] }];
        var chain = Assert.Single(Preview(group).GetProperty("groups")[0].GetProperty("chains").EnumerateArray());
        Assert.Equal(2, chain.GetProperty("points").GetArrayLength());
        Assert.Equal(new[] { 1, 3 }, Indices(chain));
    }

    [Fact]
    public void MissingCaptureIsNotReportedAsAnEmptyCompletePreview()
    {
        var preview = ViewDimensionContextTests.Context().Query("boltChains").GetProperty("boltChainPreview");
        Assert.False(preview.GetProperty("geometryReadComplete").GetBoolean());
        Assert.Equal("Bolt geometry was not captured", preview.GetProperty("error").GetString());
    }

    private static readonly DimensionChainSide[] AllSides = [DimensionChainSide.Top, DimensionChainSide.Bottom,
        DimensionChainSide.Left, DimensionChainSide.Right];

    [Fact]
    public void RequestToleranceReachesBoltPatternChecks()
    {
        var group = Rectangle();
        group.Positions[0].Point[2] = 0.05;
        var snapshot = Snapshot(group).Answer;
        var coarse = Freeze(BoltDimensionChainPreview.Build(snapshot, [10], AllSides, coordinateSettings: new(0.1)));
        var fine = Freeze(BoltDimensionChainPreview.Build(snapshot, [10], AllSides, coordinateSettings: new(0.01)));
        Assert.Equal("Candidate", coarse.GetProperty("groups")[0].GetProperty("status").GetString());
        Assert.Equal("Blocked", fine.GetProperty("groups")[0].GetProperty("status").GetString());
        Assert.Equal(0.01, fine.GetProperty("coordinateTolerance").GetDouble());
    }

    [Fact]
    public void QuerySharesSettingsBetweenPlansAndKeepsToleranceCachesSeparate()
    {
        var context = ViewDimensionContextTests.Context();
        var fine = context.Query("boltChains,chainDetails", ruleSet: "panel", coordinateSettings: new(0.01));
        var coarse = context.Query("boltChains,chainDetails", ruleSet: "panel", coordinateSettings: new(0.1));
        Assert.Equal(0.01, fine.GetProperty("boltChainPreview").GetProperty("coordinateTolerance").GetDouble());
        Assert.Equal(0.01, fine.GetProperty("compositionPlan").GetProperty("coordinateToleranceViewUnits").GetDouble());
        Assert.Equal(0.1, coarse.GetProperty("boltChainPreview").GetProperty("coordinateTolerance").GetDouble());
        Assert.Equal(0.1, coarse.GetProperty("compositionPlan").GetProperty("coordinateToleranceViewUnits").GetDouble());
        Assert.Equal(fine.GetRawText(), context.Query("boltChains,chainDetails", ruleSet: "panel", coordinateSettings: new(0.01)).GetRawText());
    }

    private static JsonElement Preview(BoltGroupGeometry group, string restriction = "inside", int[]? includedParts = null) =>
        Freeze(BoltDimensionChainPreview.Build(Snapshot(group, restriction).Answer, includedParts ?? [10], AllSides));

    private static ViewBoltGeometrySnapshot Snapshot(BoltGroupGeometry group, string restriction = "inside") =>
        ViewBoltGeometrySnapshot.Capture(7, [10], id => new() {
            Success = true, ViewId = 7, PartId = id, BoltGroups = [group]
        }, restriction == "unresolved" ? null : new DepthBox(restriction == "outside" ? 20 : -100,
            -100, -100, 200, 200, 100), point => point, true);

    private static JsonElement Freeze(object value) => JsonSerializer.SerializeToElement(value);
    private static int[] Indices(JsonElement chain) => chain.GetProperty("points").EnumerateArray()
        .Select(point => point.GetProperty("sourceIndices")[0].GetInt32()).ToArray();

    private static BoltGroupGeometry Rectangle() => new() {
        ModelId = 42, Shape = "BoltArray", FirstPosition = [10, 20, 0], SecondPosition = [70, 20, 0],
        PartToBeBoltedId = 10, PartToBoltToId = 20,
        Positions = [new() { Index = 3, Point = [10, 20, 0] }, new() { Index = 8, Point = [70, 20, 0] },
            new() { Index = 4, Point = [10, 60, 0] }, new() { Index = 6, Point = [70, 60, 0] }]
    };
}
