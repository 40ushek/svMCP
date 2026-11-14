using System.Text.Json;
using TeklaMcpServer.Api.Drawing;
using TeklaMcpServer.Tools;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DimensionReadbackContractTests
{
    [Theory]
    [InlineData("Horizontal", "Top")]
    [InlineData("Vertical", "Left")]
    [InlineData("Inclined", "Unknown")]
    public void CompactAndBatchReadsAgreeOnSideAndUseReportedSegments(string type, string side)
    {
        var snapshot = new GetDimensionsResult { Groups = [new() {
            DimensionType = type, TopDirection = 1, Items = [new() {
                Id = 42, ViewId = 7, DimensionType = type,
                ReferenceLine = new() { StartX = -20.00000000001, StartY = -49.99999999999 },
                PointList = [new() { X = 0, Y = 0 }, new() { X = 100, Y = 10 }], LengthList = [777]
            }]
        }] };
        var json = JsonSerializer.SerializeToElement(snapshot, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        using var compact = JsonDocument.Parse(ModelTools.CompactDimensions(json));
        var item = compact.RootElement.GetProperty("dimensions")[0];
        var batch = Assert.Single(ViewDimensionContextProvider.ProjectReadback(snapshot));
        Assert.Equal(side, item.GetProperty("side").GetString());
        Assert.Equal(batch.Side, item.GetProperty("side").GetString());
        // Straight chains are read from their points; an inclined one from its lengths.
        Assert.Equal(type switch { "Horizontal" => new[] { 100d }, "Vertical" => new[] { 10d }, _ => new[] { 777d } }, batch.Segments);
        Assert.Equal(batch.Segments, item.GetProperty("segments").EnumerateArray().Select(value => value.GetDouble()));
        Assert.Equal(batch.LineAtSource, compact.RootElement.GetProperty("lineAtSource").GetString());
        if (type == "Inclined")
        {
            Assert.Null(batch.LineAt);
            Assert.Null(batch.From);
            Assert.Equal(JsonValueKind.Null, item.GetProperty("at").ValueKind);
            Assert.Equal(JsonValueKind.Null, item.GetProperty("from").ValueKind);
        }
        else
        {
            Assert.Equal(type == "Horizontal" ? -50 : -20, batch.LineAt!.Value);
            Assert.Equal(batch.LineAt.Value, item.GetProperty("at").GetDouble());
        }
    }

    [Theory]
    [InlineData("Horizontal")]
    [InlineData("Inclined")]
    public void SegmentsAreStepsBetweenPointsNotDistancesFromTheFirstPoint(string type)
    {
        // M.298, Bottom-location: Tekla's LengthList is each point's distance from the first one.
        var snapshot = new GetDimensionsResult { Groups = [new() {
            DimensionType = type, TopDirection = -1, Items = [new() {
                Id = 42, ViewId = 7, DimensionType = type, ReferenceLine = new() { StartX = 0, StartY = -180 },
                PointList = [new() { X = 0 }, new() { X = 260 }, new() { X = 335 }, new() { X = 1185 }, new() { X = 1260 }, new() { X = 4720 }],
                LengthList = [260, 335, 1185, 1260, 4720]
            }]
        }] };
        var json = JsonSerializer.SerializeToElement(snapshot, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        using var compact = JsonDocument.Parse(ModelTools.CompactDimensions(json));
        var expected = new[] { 260d, 75, 850, 75, 3460 };
        Assert.Equal(expected, Assert.Single(ViewDimensionContextProvider.ProjectReadback(snapshot)).Segments);
        Assert.Equal(expected, compact.RootElement.GetProperty("dimensions")[0].GetProperty("segments")
            .EnumerateArray().Select(value => value.GetDouble()));
    }

    [Fact]
    public void FactualReadRetainsCoincidentSetsAsDistinctObjects()
    {
        TeklaDimensionSetSnapshot Snapshot(int id) => new() {
            Id = id, ViewId = 7, ViewType = "FrontView", DirectionX = 1, Orientation = "horizontal",
            Bounds = new() { MinX = 0, MinY = 0, MaxX = 100, MaxY = 10 }, Distance = 80
        };
        var groups = DimensionGroupFactory.BuildFactGroups([Snapshot(42), Snapshot(43)]);
        Assert.Equal(new[] { 42, 43 }, groups.SelectMany(group => group.DimensionList).Select(item => item.DimensionId));
    }

    [Fact]
    public void BridgePreservesTheExplicitPlanIdentifier()
    {
        var request = DrawingCommandParsers.ParseCreateDimensionsBatchRequest([
            "create_dimensions_batch", "7", "ctx", "[{\"preview\":\"Top-location\"}]", "steel", "chain", "plan-42"
        ]);
        Assert.Equal("plan-42", request.PreviewPlanId);
    }
}
