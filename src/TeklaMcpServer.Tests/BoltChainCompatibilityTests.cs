using System.Text.Json;
using System.Text.RegularExpressions;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

/// <summary>Checked-in synthetic inputs and answers from the baseline verified in 4e926bb,
/// before the second stage of the architectural refactor.
/// No local case directory, running Tekla or baseline-generation mode is required.</summary>
public sealed class BoltChainCompatibilityTests
{
    [Theory]
    [InlineData("front-default")]
    [InlineData("front-reversed")]
    [InlineData("front-frame-refusal")]
    [InlineData("front-edgeless")]
    [InlineData("back-reversed")]
    [InlineData("section")]
    [InlineData("end")]
    [InlineData("front-tall")]
    public void PreviewMatchesTheVerified4e926bbContract(string name)
    {
        using var fixture = Load(name);
        var context = Context(fixture.RootElement);
        foreach (var question in new[] { "chain", "chainDetails", "boltChains" })
            Assert.Equal(Normalize(fixture.RootElement.GetProperty("expected").GetProperty(question)),
                Normalize(context.Query(question)));
    }

    [Theory]
    [InlineData("front-default")]
    [InlineData("back-reversed")]
    [InlineData("front-tall")]
    public void RepeatReadAndBatchResolveTheSameBoltKeyAndOffset(string name)
    {
        using var fixture = Load(name);
        var context = Context(fixture.RootElement);
        var answer = context.Query("chain,repeats", readSheet: () => []);
        var rows = answer.GetProperty("chainPreview");
        Assert.Equal(Normalize(rows), Normalize(context.Query("chain", storedRepeats: true).GetProperty("chainPreview")));
        var selected = rows.EnumerateArray().Single(row => row.GetProperty("side").GetString() == "Bolt")
            .GetProperty("chains").EnumerateArray().First(chain => !chain.GetProperty("incomplete").GetBoolean());
        var key = selected.GetProperty("key").GetString()!;
        Assert.Single(ViewDimensionContextProvider.IndexPreviewChains(rows, [key]));
        var offsets = new List<double>();
        var provider = new ViewDimensionContextProvider(() => "compatibility", (_, _) => context, () => { },
            (_, distance) => { offsets.Add(distance); return new() { Created = true, DimensionId = 1 }; },
            readDimensions: _ => new GetDimensionsResult(), validateAttributes: _ => { });
        provider.Get(7);
        provider.CreateBatch(new CreateDimensionsBatchRequest { ViewId = 7, ContextId = context.ContextId,
            RuleSet = "steel", PreviewPlanId = answer.GetProperty("previewPlanId").GetString(), Chains = [new() { Preview = key }] });
        // This test checks preparation/resolution; verified drawing read-back has separate coverage.
        Assert.Equal(selected.GetProperty("distance").GetDouble(), Assert.Single(offsets));
    }

    [Fact]
    public void CarriedContractSurvivesRepeatReadAndBatchResolution()
    {
        // Reviewed expectations from the existing full-length fin-plate scenario, not a captured 4e926bb response.
        using var fixture = Load("full-length-fin-carry");
        Assert.Equal("FullLengthFinPlate", fixture.RootElement.GetProperty("scenario").GetString());
        var expected = fixture.RootElement.GetProperty("expected");
        var context = BoltChainRuleTests.FullLengthFinPlate();
        var answer = context.Query("chain,repeats", readSheet: () => []);
        var rows = answer.GetProperty("chainPreview");
        var key = expected.GetProperty("key").GetString()!;
        var carried = rows.EnumerateArray().Single(row => row.GetProperty("side").GetString() == "Bolt")
            .GetProperty("chains").EnumerateArray().Single(chain => chain.GetProperty("key").GetString() == key);
        Assert.False(carried.GetProperty("incomplete").GetBoolean());
        Assert.Equal(expected.GetProperty("carries").GetString(), carried.GetProperty("carries").GetString());
        Assert.Equal(Normalize(expected.GetProperty("segments")), Normalize(carried.GetProperty("segments")));
        var sources = carried.GetProperty("sources").EnumerateArray().ToArray();
        Assert.Equal(expected.GetProperty("sourceXs").EnumerateArray().Select(x => x.GetDouble()),
            sources.Select(source => source.GetProperty("point")[0].GetDouble()));
        var references = sources.SelectMany(source => source.GetProperty("sources").EnumerateArray()).ToArray();
        Assert.Equal(expected.GetProperty("sourceIndices").EnumerateArray().Select(x => x.GetInt32()),
            references.Select(source => source.GetProperty("index").GetInt32()));
        Assert.All(references, source => Assert.Equal(expected.GetProperty("groupId").GetInt32(), source.GetProperty("boltGroupId").GetInt32()));
        var overall = rows.EnumerateArray().Single(row => row.GetProperty("side").GetString() == "Bottom")
            .GetProperty("chains").EnumerateArray().Single(chain => chain.GetProperty("key").GetString() == expected.GetProperty("overallKey").GetString());
        Assert.False(overall.GetProperty("incomplete").GetBoolean());
        Assert.Equal(Normalize(expected.GetProperty("overallSegments")), Normalize(overall.GetProperty("segments")));
        Assert.Equal(Normalize(rows), Normalize(context.Query("chain", storedRepeats: true).GetProperty("chainPreview")));
        Assert.Single(ViewDimensionContextProvider.IndexPreviewChains(rows, [key]));
        var writes = new List<(CreateDimensionRequest Request, double Distance)>();
        var provider = new ViewDimensionContextProvider(() => "carry-contract", (_, _) => context, () => { },
            (request, distance) => { writes.Add((request, distance)); return new() { Created = true, DimensionId = 1 }; },
            readDimensions: _ => new GetDimensionsResult(), validateAttributes: _ => { });
        provider.Get(7);
        provider.CreateBatch(new CreateDimensionsBatchRequest { ViewId = 7, ContextId = context.ContextId,
            RuleSet = "steel", PreviewPlanId = answer.GetProperty("previewPlanId").GetString(), Chains = [new() { Preview = key }] });
        var write = Assert.Single(writes);
        Assert.Equal(carried.GetProperty("distance").GetDouble(), write.Distance);
        Assert.Equal(carried.GetProperty("points").EnumerateArray().SelectMany(point => new[] { point[0].GetDouble(), point[1].GetDouble(), 0d }),
            write.Request.Points);
    }

    private static JsonDocument Load(string name) => JsonDocument.Parse(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "TestAssets", "BoltChains", name + ".json")));

    private static string Normalize(JsonElement value)
    {
        // Plan IDs are new volatile transport metadata; the captured geometry contract is unchanged.
        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("previewPlanId", out _))
            value = JsonSerializer.SerializeToElement(value.EnumerateObject().Where(property => property.Name != "previewPlanId")
                .ToDictionary(property => property.Name, property => property.Value));
        return Regex.Replace(JsonSerializer.Serialize(value), "ctx_[0-9a-f]{32}", "ctx");
    }

    private static ViewDimensionContext Context(JsonElement fixture)
    {
        BoltGroupGeometry Column(int id, int part, double x) => new() { ModelId = id, Shape = "BoltArray",
            FirstPosition = [x, -40, 0], SecondPosition = [x, 40, 0], Axis = [0, 0, 1], PartToBeBoltedId = part,
            Positions = [new() { Index = 0, Point = [x, -40, 0] }, new() { Index = 1, Point = [x, 40, 0] }] };
        var bolts = ViewBoltGeometrySnapshot.Capture(7, [10, 11], id => new() { Success = true, ViewId = 7, PartId = id,
            BoltGroups = id == 10 ? [Column(41, 10, 40), Column(42, 10, 300), Column(43, 10, 860)] : [Column(44, 11, 520)] },
            new DepthBox(-1000, -1000, -1000, 1000, 1000, 1000), point => point, true);
        var height = fixture.GetProperty("halfHeight").GetDouble();
        var beam = DimensionChainPreviewTests.Solid(10, 0, 900, -height, height);
        var reversed = fixture.GetProperty("reversed").GetBoolean();
        beam.StartPoint = reversed ? [900, 0, 0] : [0, 0, 0];
        beam.EndPoint = reversed ? [0, 0, 0] : [900, 0, 0];
        var plate = fixture.GetProperty("edgelessPlate").GetBoolean()
            ? DimensionChainPreviewTests.Poly(11, (480, -95), (560, -95), (560, -60), (520, 95), (480, -60))
            : DimensionChainPreviewTests.Solid(11, 480, 560, -95, 95);
        return DimensionChainPreviewTests.Framed(fixture.GetProperty("viewType").GetString()!, bolts,
            fixture.GetProperty("frameRefusal").GetBoolean() ? [10, -300, 1000, 300] : null, beam, plate);
    }
}
