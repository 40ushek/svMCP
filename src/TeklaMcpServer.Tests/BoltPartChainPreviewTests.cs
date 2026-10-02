using System.Text.Json;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class BoltPartChainPreviewTests
{
    [Fact]
    public void CombinesSeparateGroupsAndPreservesSourcesForRepeatedCoordinates()
    {
        var chains = Preview();
        var x = chains[0];
        Assert.Equal("Candidate", x.GetProperty("state").GetString());
        Assert.Equal(new[] { 30d, 120d, 80d }, x.GetProperty("segments").EnumerateArray().Select(v => v.GetDouble()));
        Assert.Equal(2, x.GetProperty("projectedPositions")[0].GetProperty("sources").GetArrayLength());
        Assert.Equal(new[] { 30d, 100d, 55d, 80d }, chains[1].GetProperty("segments").EnumerateArray().Select(v => v.GetDouble()));
        Assert.Equal(5, chains[1].GetProperty("points").GetArrayLength());
    }

    [Fact]
    public void OutsideSourceBlocksWholePartRatherThanShorteningChain()
    {
        foreach (var chain in Preview(outside: true).EnumerateArray())
        {
            Assert.Equal("Blocked", chain.GetProperty("state").GetString());
            Assert.Empty(chain.GetProperty("points").EnumerateArray());
        }
    }

    [Theory]
    [InlineData(0.09, 1)]
    [InlineData(0.1, 1)]
    [InlineData(0.11, 2)]
    public void CoordinateToleranceHasExplicitBoundary(double separation, int count)
    {
        var chain = Preview(points: [[0, -20, 0], [separation, -30, 0]])[0];
        Assert.Equal(count, chain.GetProperty("projectedPositions").GetArrayLength());
    }

    [Fact]
    public void ClusteringIsNotTransitiveAndDoesNotDependOnInputOrder()
    {
        double[][] points = [[0, -30, 0], [0.09, -20, 0], [0.18, -10, 0]];
        var forward = Preview(points: points)[0];
        var reversed = Preview(points: points.Reverse().ToArray())[0];
        Assert.Equal(2, forward.GetProperty("projectedPositions").GetArrayLength());
        Assert.Equal(forward.GetProperty("points").GetRawText(), reversed.GetProperty("points").GetRawText());
    }

    [Theory]
    [InlineData(DimensionChainSide.Top, 30)]
    [InlineData(DimensionChainSide.Bottom, -30)]
    public void RepresentativeUsesPlacementSide(DimensionChainSide side, double expectedY)
    {
        var chain = Preview(points: [[0, -30, 0], [0, 30, 0]], sides: [side])[0];
        Assert.Equal(expectedY, chain.GetProperty("projectedPositions")[0].GetProperty("point")[1].GetDouble());
        Assert.Equal(2, chain.GetProperty("projectedPositions")[0].GetProperty("sources").GetArrayLength());
    }

    [Fact]
    public void EndpointsOnDifferentOuterContoursBlockChain()
    {
        var chain = Preview(points: [[-100, -20, 0], [50, -20, 0]],
            outlines: [Box(-150, -50), Box(0, 80)])[0];
        Assert.Equal("Blocked", chain.GetProperty("state").GetString());
        Assert.Contains("different outer contours", chain.GetProperty("reason").GetString());
        Assert.Empty(chain.GetProperty("points").EnumerateArray());
    }

    [Fact]
    public void AllBlockedSourcesAreReportedAndOtherPartsStayIndependent()
    {
        var chains = Preview(states: ["Outside", "Unresolved", "Inside", "Inside"], partIds: [10, 10, 11, 11]);
        var blocked = chains[0].GetProperty("blockedSources");
        Assert.Equal(new[] { 100, 101 }, blocked.EnumerateArray().Select(source => source.GetProperty("boltGroupId").GetInt32()));
        Assert.All(blocked.EnumerateArray(), source => Assert.Equal(0, source.GetProperty("index").GetInt32()));
        Assert.Equal("Candidate", chains[2].GetProperty("state").GetString());
    }

    [Fact]
    public void MissingContourBlocksAndEmptySnapshotHasNoProposals()
    {
        var chain = Preview(outlines: [])[0];
        Assert.Equal("Blocked", chain.GetProperty("state").GetString());
        Assert.Contains("no captured", chain.GetProperty("reason").GetString());
        Assert.Empty(Preview(points: []).EnumerateArray());
    }

    [Fact]
    public void FrozenContextReusesPreviewForEquivalentSideSets()
    {
        var context = ViewDimensionContextTests.Context();
        var first = context.BuildBoltPreview([DimensionChainSide.Left, DimensionChainSide.Bottom]);
        Assert.Equal(first, context.BuildBoltPreview([DimensionChainSide.Bottom, DimensionChainSide.Left, DimensionChainSide.Left]));
        Assert.NotEqual(first, ViewDimensionContextTests.Context().BuildBoltPreview([DimensionChainSide.Left, DimensionChainSide.Bottom]));
    }

    private static OutlineTreeNodeResult Box(double min = -150, double max = 80) => new() {
        Polygon = [[min, -185], [max, -185], [max, 80], [min, 80]]
    };

    private static JsonElement Preview(bool outside = false, double[][]? points = null,
        DimensionChainSide[]? sides = null, OutlineTreeNodeResult[]? outlines = null,
        string[]? states = null, int[]? partIds = null)
    {
        points ??= [[0, -155, 32.5], [0, -55, 32.5], [-120, -155, 32.5], [-120, 0, 34]];
        var snapshot = JsonSerializer.SerializeToElement(new {
            boltGroups = points.Select((point, index) => new {
                geometry = new BoltGroupGeometry {
                    ModelId = index + 100, Shape = "BoltArray", PartToBeBoltedId = partIds?[index] ?? 10,
                    FirstPosition = [0, 0, 0], SecondPosition = [10, 0, 0],
                    Positions = [new() { Index = 0, Point = point }]
                },
                restriction = new[] { new { index = 0, centerState = states?[index] ?? (outside && index == 3 ? "Outside" : "Inside") } }
            })
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        return JsonSerializer.SerializeToElement(BoltPartChainPreview.Build(snapshot, [10, 11],
            sides ?? [DimensionChainSide.Bottom, DimensionChainSide.Left],
            new Dictionary<int, IReadOnlyList<OutlineTreeNodeResult>> { [10] = outlines ?? [Box()], [11] = outlines ?? [Box()] }));
    }
}
