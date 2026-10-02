using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DimensionRowPlacementTests
{
    [Theory]
    [InlineData("horizontal", false)]
    [InlineData("horizontal-down", false)]
    [InlineData("vertical", true)]
    [InlineData("vertical-left", true)]
    public void PreviewRowsProduceDistinctLinesOnEverySide(string direction, bool vertical)
    {
        var reads = 0;
        var provider = new ViewDimensionContextProvider(() => "drawing-a",
            (_, _) => { reads++; return ViewDimensionContextTests.Context(); }, () => { },
            (_, _) => new CreateDimensionResult { Created = true });
        CreateDimensionRequest Request(int row) => new() {
            ViewId = 7, Direction = direction, Row = row,
            Points = vertical ? [0, 0, 0, 0, 340, 0] : [0, 0, 0, 260, 0, 0]
        };
        var first = provider.Create(Request(1));
        var second = provider.Create(Request(2));
        var third = provider.Create(Request(3));
        var fourth = provider.Create(Request(4));
        Assert.Equal(24, third.Placement!.PaperGapMm);
        Assert.Equal(32, fourth.Placement!.PaperGapMm);
        Assert.Equal(80, Math.Abs(third.Placement.TargetLineCoordinate - second.Placement!.TargetLineCoordinate));
        Assert.Equal(80, Math.Abs(fourth.Placement.TargetLineCoordinate - third.Placement.TargetLineCoordinate));
        Assert.True(first.Created);
        Assert.True(second.Created);
        Assert.Equal(8, first.Placement!.PaperGapMm);
        Assert.Equal(16, second.Placement!.PaperGapMm);
        Assert.Equal(80, Math.Abs(second.Placement.TargetLineCoordinate - first.Placement.TargetLineCoordinate));
        Assert.Equal(1, reads);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void InvalidNumberedRowStopsBeforeReadingOrWriting(int row)
    {
        var provider = new ViewDimensionContextProvider(() => "drawing-a",
            (_, _) => throw new Exception("Unexpected geometry read"), () => { },
            (_, _) => throw new Exception("Unexpected write"));
        Assert.Throws<ArgumentOutOfRangeException>(() => provider.Create(new() {
            ViewId = 7, Row = row, Points = [0, 0, 0, 260, 0, 0]
        }));
    }

    [Theory]
    [InlineData("first")]
    [InlineData("second")]
    [InlineData("2.5")]
    [InlineData("2147483648")]
    public void SingleCommandRejectsNonIntegerRows(string row)
    {
        var parsed = DrawingCommandParsers.ParseCreateDimensionRequest([
            "create_dimension", "7", "[0,0,0,260,0,0]", "horizontal-down",
            "", "standard", "", "", "", "", "", row
        ]);
        Assert.False(parsed.IsValid);
    }

    [Fact]
    public void BatchJsonUsesNumericRowAndMissingRowDefaultsToOne()
    {
        var chains = System.Text.Json.JsonSerializer.Deserialize<BatchDimensionChain[]>(
            "[{\"row\":3},{}]", new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.Equal(3, chains![0].Row);
        Assert.Equal(1, chains[1].Row);
    }

    [Fact]
    public void ExplicitPaperGapBypassesInvalidUnusedRow()
    {
        var provider = new ViewDimensionContextProvider(() => "drawing-a",
            (_, _) => ViewDimensionContextTests.Context(), () => { },
            (_, _) => new CreateDimensionResult { Created = true });
        var result = provider.Create(new() {
            ViewId = 7, Direction = "horizontal-down", Row = 0, PaperGapMm = 12,
            Points = [0, 0, 0, 260, 0, 0]
        });
        Assert.Equal(12, result.Placement!.PaperGapMm);
        Assert.Equal(120, result.DistanceUsed);
    }

    [Fact]
    public void ExplicitDistanceOverridesRowWithoutReadingGeometry()
    {
        var provider = new ViewDimensionContextProvider(() => null,
            (_, _) => throw new Exception("Unexpected geometry read"), () => { },
            (_, distance) => new CreateDimensionResult { Created = distance == 27 });
        var result = provider.Create(new() {
            ViewId = 7, Row = 0, Distance = 27, Points = [0, 0, 0, 260, 0, 0]
        });
        Assert.True(result.Created);
        Assert.Null(result.Placement);
        Assert.Equal(27, result.DistanceUsed);
    }

    [Theory]
    [InlineData("27", "", "27")]
    [InlineData("", "12", "120")]
    public void SingleCommandIgnoresUnusedInvalidRowWithExplicitOffset(string distance, string gap, string expected)
    {
        var parsed = DrawingCommandParsers.ParseCreateDimensionRequest([
            "create_dimension", "7", "[0,0,0,260,0,0]", "horizontal-down",
            distance, "standard", gap, "", "", "", "", "typo"
        ]);
        Assert.True(parsed.IsValid);
        var provider = new ViewDimensionContextProvider(() => "drawing-a",
            (_, _) => ViewDimensionContextTests.Context(), () => { },
            (_, _) => new CreateDimensionResult { Created = true });
        Assert.Equal(double.Parse(expected), provider.Create(parsed.Request).DistanceUsed);
    }

    [Fact]
    public void SingleCommandPreservesPreviewRowThroughParsingAndCreation()
    {
        var parsed = DrawingCommandParsers.ParseCreateDimensionRequest([
            "create_dimension", "7", "[0,0,0,260,0,0]", "horizontal-down",
            "", "standard", "", "", "", "", "", "2"
        ]);
        Assert.True(parsed.IsValid);
        var provider = new ViewDimensionContextProvider(() => "drawing-a",
            (_, _) => ViewDimensionContextTests.Context(), () => { },
            (_, _) => new CreateDimensionResult { Created = true });
        Assert.Equal(160, provider.Create(parsed.Request).DistanceUsed);
    }

    [Fact]
    public void BatchRowsReachWriterAndInvalidLaterRowPreventsAllWrites()
    {
        var context = ViewDimensionContextTests.Context();
        var ids = context.Query("dimensionPoints", "Bottom").GetProperty("dimensionPoints")
            .GetProperty("sides")[0].GetProperty("points").EnumerateArray()
            .Select(point => point.GetProperty("pointId").GetString()!).ToArray();
        var distances = new List<double>();
        var provider = new ViewDimensionContextProvider(() => "drawing-a", (_, _) => context, () => { },
            (_, distance) => { distances.Add(distance); return new CreateDimensionResult { Created = true, DimensionId = distances.Count }; },
            readDimensions: _ => new GetDimensionsResult(), validateAttributes: _ => { });
        provider.Get(7);
        BatchDimensionChain Chain(string key, int row) => new() {
            Key = key, Row = row, Direction = "horizontal-down", PointIds = [ids[0], ids[^1]]
        };
        var batch = new CreateDimensionsBatchRequest {
            ViewId = 7, ContextId = context.ContextId,
            Chains = [Chain("location", 1), Chain("overall", 2), Chain("third", 3), Chain("fourth", 4)]
        };
        provider.CreateBatch(batch);
        Assert.Equal(new[] { 80d, 160d, 240d, 320d }, distances);
        distances.Clear();
        batch.Chains[1].Row = 0;
        batch.Chains[1].Distance = 190;
        provider.CreateBatch(batch);
        Assert.Equal(new[] { 80d, 190d, 240d, 320d }, distances);
        distances.Clear();
        batch.Chains[1].Distance = null;
        Assert.Throws<ArgumentOutOfRangeException>(() => provider.CreateBatch(batch));
        Assert.Empty(distances);
    }
}
