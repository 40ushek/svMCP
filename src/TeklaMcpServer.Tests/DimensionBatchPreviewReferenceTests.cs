using System.Text.Json;
using TeklaMcpServer.Api.Drawing;
using TeklaMcpServer.Tools;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DimensionBatchPreviewReferenceTests
{
    [Theory]
    [InlineData("steel", "chain", "Top-location", "Relative")]
    [InlineData("steel", "chain", "Bottom-overall", "Absolute")]
    [InlineData("steel", "chain", "Right-location", "RelativeAndAbsolute")]
    [InlineData("steel", "chainDetails", "Top-location", "Absolute")]
    [InlineData("panel", "chain", "Bottom-overall", "Relative")]
    [InlineData("section", "chain", "Top-chain", "Absolute")]
    [InlineData("section", "chainDetails", "Top-profile", "RelativeAndAbsolute")]
    public void ReferenceAndExplicitPointsProduceTheSameWritesAndReadback(
        string fixture, string chainView, string key, string type)
    {
        var context = Context(fixture);
        var ruleSet = fixture == "panel" ? "panel" : "steel";
        var preview = Chain(context, key, ruleSet, chainView);
        var reference = new Harness(context);
        var explicitPoints = new Harness(context);
        var first = reference.Provider.CreateBatch(Batch(context,
            new() { Preview = key, DimensionType = type }, ruleSet, chainView));
        var second = explicitPoints.Provider.CreateBatch(Batch(context, new() {
            Key = key, PointIds = Ids(preview), Direction = preview.GetProperty("direction").GetString()!,
            AttributesFile = preview.GetProperty("attributesFile").GetString()!, DimensionType = type
        }, ruleSet, chainView));
        Assert.Equal(explicitPoints.Writes.Single().Request.Points, reference.Writes.Single().Request.Points);
        Assert.Equal(explicitPoints.Writes.Single().Request.Direction, reference.Writes.Single().Request.Direction);
        Assert.Equal(explicitPoints.Writes.Single().Request.AttributesFile, reference.Writes.Single().Request.AttributesFile);
        Assert.Equal(explicitPoints.Writes.Single().Distance, reference.Writes.Single().Distance);
        Assert.Equal(JsonSerializer.Serialize(second), JsonSerializer.Serialize(first));
        Assert.Equal("created", first.Chains.Single().Status);
    }

    [Theory]
    [InlineData("steel", "chain")]
    [InlineData("steel", "chainDetails")]
    [InlineData("panel", "chain")]
    [InlineData("section", "chain")]
    [InlineData("section", "chainDetails")]
    public void SingleSideReadHasSamePointOrderAsAllSides(string fixture, string chainView)
    {
        var context = Context(fixture);
        var ruleSet = fixture == "panel" ? "panel" : "steel";
        var single = context.Query(chainView, "Top", ruleSet: ruleSet).GetProperty("chainPreview")[0];
        var all = context.Query(chainView, "all", ruleSet: ruleSet).GetProperty("chainPreview")
            .EnumerateArray().Single(row => row.GetProperty("side").GetString() == "Top");
        Assert.Equal(single.GetRawText(), all.GetRawText());
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("Right-overall")]
    public void InvalidLaterReferencePreventsAllReadsAndWrites(string key)
    {
        var context = Context("steel");
        var harness = new Harness(context);
        var batch = Batch(context, new() { Preview = "Top-location" });
        batch.Chains.Add(new() { Preview = key });
        Assert.Throws<ArgumentException>(() => harness.Provider.CreateBatch(batch));
        Assert.Empty(harness.Writes);
        Assert.Equal(0, harness.DimensionReads);
    }

    [Theory]
    [InlineData("both")]
    [InlineData("both-null")]
    [InlineData("direction")]
    [InlineData("duplicate")]
    [InlineData("duplicate-key")]
    [InlineData("blank")]
    [InlineData("null-entry")]
    public void InvalidEntryFormsPreventEveryWrite(string invalid)
    {
        var context = Context("steel");
        var harness = new Harness(context);
        var chain = new BatchDimensionChain { Preview = "Top-location" };
        var batch = Batch(context, chain);
        switch (invalid) {
            case "both": chain.PointIds = []; break;
            case "both-null": chain.PointIds = null; break;
            case "direction": chain.Direction = "horizontal"; break;
            case "duplicate": batch.Chains.Add(new() { Preview = chain.Preview, Key = "different" }); break;
            case "duplicate-key": batch.Chains.Add(new() { Preview = "Bottom-overall", Key = "Top-location" }); break;
            case "blank": chain.Preview = ""; break;
            case "null-entry": batch.Chains.Add(null!); break;
        }
        Assert.Throws<ArgumentException>(() => harness.Provider.CreateBatch(batch));
        Assert.Empty(harness.Writes);
        Assert.Equal(0, harness.DimensionReads);
    }

    [Fact]
    public void RepeatedReferenceRetainsItsIdAndDoesNotMutateCallerInput()
    {
        var context = Context("steel");
        var harness = new Harness(context);
        var chain = new BatchDimensionChain { Preview = "Top-location" };
        var batch = Batch(context, chain);
        var first = harness.Provider.CreateBatch(batch);
        var second = harness.Provider.CreateBatch(batch);
        Assert.Single(harness.Writes);
        Assert.Equal("retained", second.Chains.Single().Status);
        Assert.Equal(first.Chains.Single().DimensionId, second.Chains.Single().DimensionId);
        Assert.Null(chain.PointIds);
        Assert.Null(chain.AttributesFile);
        Assert.Equal("", chain.Key);
    }

    [Fact]
    public void OccupiedSideRejectsNewReferenceUnlessItsOffsetIsExplicit()
    {
        var context = Context("steel");
        var harness = new Harness(context);
        harness.Provider.CreateBatch(Batch(context, new() { Preview = "Top-location" }));
        var batch = Batch(context, new() { Preview = "Top-overall", AttributesFile = "custom", DimensionType = "Absolute" });
        Assert.Throws<ArgumentException>(() => harness.Provider.CreateBatch(batch));
        Assert.Single(harness.Writes);
        batch.Chains[0].PaperGapMm = 16;
        harness.Provider.CreateBatch(batch);
        Assert.Equal(2, harness.Writes.Count);
        Assert.Equal("custom", harness.Writes[1].Request.AttributesFile);
        Assert.Equal("Absolute", harness.Writes[1].Request.DimensionType);
        Assert.Equal(160, harness.Writes[1].Distance);
    }

    [Theory]
    [InlineData("view")]
    [InlineData("drawing")]
    [InlineData("refresh")]
    public void InvalidatedContextNeverRebuildsGeometryDuringPlacement(string change)
    {
        var context = Context("steel");
        var harness = new Harness(context);
        if (change == "view") harness.Provider.ObserveView(8);
        if (change == "drawing") harness.Drawing = "another-drawing";
        if (change == "refresh") harness.Provider.Get(7, refresh: true);
        var geometryReads = harness.GeometryReads;
        Assert.Throws<InvalidOperationException>(() => harness.Provider.CreateBatch(Batch(context,
            new() { Preview = "Top-location" })));
        Assert.Empty(harness.Writes);
        Assert.Equal(0, harness.DimensionReads);
        Assert.Equal(geometryReads, harness.GeometryReads);
    }

    [Fact]
    public void SectionVariantsCannotResolveEachOthersKeys()
    {
        var context = Context("section");
        var harness = new Harness(context);
        Assert.Throws<ArgumentException>(() => harness.Provider.CreateBatch(Batch(context,
            new() { Preview = "Top-chain" }, chainView: "chainDetails")));
        Assert.Throws<ArgumentException>(() => harness.Provider.CreateBatch(Batch(context,
            new() { Preview = "Top-profile" })));
        Assert.Empty(harness.Writes);
    }

    [Fact]
    public void LegacyBridgeArgumentsAndExplicitEntriesRemainSupported()
    {
        var request = DrawingCommandParsers.ParseCreateDimensionsBatchRequest([
            "create_dimensions_batch", "7", "ctx", "[{\"key\":\"old\",\"pointIds\":[\"p1\",\"p2\"],\"direction\":\"horizontal\"}]"
        ]);
        Assert.Equal(string.Empty, request.RuleSet);
        Assert.Equal("chain", request.ChainView);
        Assert.Null(request.Chains[0].Preview);
        var context = Context("steel");
        var preview = Chain(context, "Top-location");
        var harness = new Harness(context);
        var batch = Batch(context, new() { Preview = "Bottom-overall" });
        batch.Chains.Add(new() { Key = "legacy", PointIds = Ids(preview), Direction = "horizontal" });
        harness.Provider.CreateBatch(batch);
        Assert.Equal(2, harness.Writes.Count);
        Assert.Equal("overall", harness.Writes[0].Request.AttributesFile);
        Assert.Equal("standard", harness.Writes[1].Request.AttributesFile);
    }

    [Fact]
    public void SerializedReferenceRetainsDefaultOverallAttributes()
    {
        var context = Context("steel");
        var json = JsonSerializer.Serialize(new[] { new BatchDimensionChain { Preview = "Bottom-overall" } });
        var request = DrawingCommandParsers.ParseCreateDimensionsBatchRequest([
            "create_dimensions_batch", "7", context.ContextId, json, "steel"
        ]);
        var harness = new Harness(context);
        harness.Provider.CreateBatch(request);
        Assert.Equal("overall", harness.Writes.Single().Request.AttributesFile);
    }

    [Fact]
    public void PreviewReferenceWithoutRuleSetIsRejectedBeforeAnyWrite()
    {
        var context = Context("steel");
        var json = JsonSerializer.Serialize(new[] { new BatchDimensionChain { Preview = "Bottom-overall" } });
        var request = DrawingCommandParsers.ParseCreateDimensionsBatchRequest([
            "create_dimensions_batch", "7", context.ContextId, json
        ]);
        var harness = new Harness(context);
        var error = Assert.Throws<ArgumentException>(() => harness.Provider.CreateBatch(request));
        Assert.Contains("ruleSet", error.Message);
        Assert.Empty(harness.Writes);
    }

    [Theory]
    [InlineData("unknown", "chain")]
    [InlineData("steel", "points")]
    public void InvalidPreviewOptionsAreRejectedAtMcpAndBridge(string ruleSet, string chainView)
    {
        Assert.Throws<ArgumentException>(() => ModelTools.CreateDimensionsBatch(7, "ctx", "[]", ruleSet, chainView));
        Assert.Throws<ArgumentException>(() => DrawingCommandParsers.ParseCreateDimensionsBatchRequest([
            "create_dimensions_batch", "7", "ctx", "[]", ruleSet, chainView
        ]));
    }

    [Theory]
    [InlineData("dropPointIds")]
    [InlineData("dropPoints")]
    public void DeferredPointRemovalIsNotSilentlyIgnored(string field)
    {
        Assert.Throws<ArgumentException>(() => DrawingCommandParsers.ParseCreateDimensionsBatchRequest([
            "create_dimensions_batch", "7", "ctx", "[{\"preview\":\"Top-location\",\"" + field + "\":[]}]"
        ]));
    }

    private static ViewDimensionContext Context(string fixture) => fixture switch {
        "section" => DimensionChainPreviewTests.ISectionContext(),
        "panel" => ViewDimensionContextTests.Context(),
        _ => DimensionChainPreviewTests.Context(68)
    };

    private static JsonElement Chain(ViewDimensionContext context, string key, string ruleSet = "steel", string chainView = "chain") =>
        context.Query(chainView, ruleSet: ruleSet).GetProperty("chainPreview").EnumerateArray()
            .SelectMany(row => row.GetProperty("chains").EnumerateArray())
            .Single(chain => chain.GetProperty("key").GetString() == key);

    private static string[] Ids(JsonElement chain) => chain.GetProperty("pointIds").EnumerateArray()
        .Select(id => id.GetString()!).ToArray();

    private static CreateDimensionsBatchRequest Batch(ViewDimensionContext context, BatchDimensionChain chain,
        string ruleSet = "steel", string chainView = "chain") => new() {
        ViewId = 7, ContextId = context.ContextId, RuleSet = ruleSet, ChainView = chainView, Chains = [chain]
    };

    private sealed class Harness
    {
        public string Drawing = "drawing-a";
        public int DimensionReads;
        public int GeometryReads;
        public List<(CreateDimensionRequest Request, double Distance)> Writes = [];
        public ViewDimensionContextProvider Provider { get; }
        private readonly GetDimensionsResult _dimensions = new();

        public Harness(ViewDimensionContext context)
        {
            Provider = new ViewDimensionContextProvider(() => Drawing, (_, _) => {
                GeometryReads++;
                return GeometryReads == 1 ? context : Context("steel");
            }, () => { }, Write, readDimensions: _ => { DimensionReads++; return _dimensions; }, validateAttributes: _ => { });
            Provider.Get(7);
        }

        private CreateDimensionResult Write(CreateDimensionRequest request, double distance)
        {
            Writes.Add((request, distance));
            var id = 100 + Writes.Count;
            var horizontal = request.Direction.StartsWith("horizontal");
            var positive = request.Direction is "horizontal" or "vertical";
            var points = Enumerable.Range(0, request.Points.Length / 3).Select(i => new DrawingPointInfo {
                X = request.Points[3 * i], Y = request.Points[3 * i + 1], Order = i
            }).ToList();
            var across = points.Select(point => horizontal ? point.Y : point.X);
            var line = positive ? across.Max() + distance : across.Min() - distance;
            _dimensions.Groups.Add(new DimensionGroupInfo {
                DimensionType = horizontal ? "Horizontal" : "Vertical",
                TopDirection = horizontal ? (positive ? 1 : -1) : (positive ? -1 : 1),
                Items = [new DimensionItemInfo {
                    Id = id, Distance = distance, PointList = points,
                    TeklaDimensionType = request.DimensionType ?? "Relative",
                    ReferenceLine = new DrawingLineInfo { StartX = horizontal ? 0 : line, StartY = horizontal ? line : 0 },
                    LengthList = points.Zip(points.Skip(1), (a, b) => Math.Abs(horizontal ? b.X - a.X : b.Y - a.Y)).ToList()
                }]
            });
            return new CreateDimensionResult { Created = true, DimensionId = id };
        }
    }
}
