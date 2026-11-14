using System.Text.Json;
using TeklaMcpServer.Api.Drawing;
using TeklaMcpServer.Tools;
using Xunit;
using Xunit.Abstractions;

namespace TeklaMcpServer.Tests;

public sealed class DimensionAnswerProjectionTests(ITestOutputHelper output)
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
    public void CompactAnswersKeepEverySelectionAndTheFullPlan(string name)
    {
        using var fixture = Fixture(name);
        var context = BoltChainCompatibilityTests.Context(fixture.RootElement);
        var full = context.Query("chain,scale,repeats", readSheet: () => []);
        var summary = DimensionAnswerProjection.Compact(full);
        Assert.Equal(full.GetProperty("previewPlanId").GetString(), summary.GetProperty("previewPlanId").GetString());
        Assert.Equal(full.GetProperty("isComplete").GetBoolean(), summary.GetProperty("isComplete").GetBoolean());
        var original = Chains(full).ToArray();
        var compact = Chains(summary).ToArray();
        Assert.Equal(original.Select(chain => chain.GetProperty("key").GetString()), compact.Select(chain => chain.GetProperty("key").GetString()));
        Assert.Equal(original.Length, compact.Length);
        for (var i = 0; i < original.Length; i++)
        {
            foreach (var field in new[] { "segments", "incomplete", "chainKind", "viewClass", "incompleteReason", "repeat", "remark" })
                if (original[i].TryGetProperty(field, out var value) && value.ValueKind != JsonValueKind.Null)
                    Assert.Equal(value.GetRawText(), compact[i].GetProperty(field).GetRawText());
            Assert.False(compact[i].TryGetProperty("points", out _));
            Assert.False(compact[i].TryGetProperty("pointIds", out _));
            Assert.False(compact[i].TryGetProperty("sources", out _));
            Assert.False(compact[i].TryGetProperty("holes", out _));
        }
        Assert.Equal(EligibleKeys(full), EligibleKeys(summary));
        var writes = new List<string>();
        var provider = Provider(context, writes);
        var details = provider.GetPreviewPlanDetails(7, summary.GetProperty("previewPlanId").GetString()!);
        Assert.Equal(full.GetProperty("chainPreview").GetRawText(), details.GetProperty("chainPreview").GetRawText());
        Assert.Equal("chain", details.GetProperty("chainView").GetString());
        Assert.False(summary.TryGetProperty("boltSettings", out _));
        Assert.Empty(writes);
        Assert.True(summary.GetRawText().Length < full.GetRawText().Length);
        output.WriteLine($"{name}: full={full.GetRawText().Length}, summary={summary.GetRawText().Length}");
    }

    [Theory]
    [InlineData("front-default")]
    [InlineData("back-reversed")]
    [InlineData("front-tall")]
    public void SummaryAndFullSelectionProduceIdenticalWriteParameters(string name)
    {
        using var fixture = Fixture(name);
        var context = BoltChainCompatibilityTests.Context(fixture.RootElement);
        var full = context.Query("chain,scale,repeats", readSheet: () => []);
        var summary = DimensionAnswerProjection.Compact(full);
        var expected = new List<string>();
        var actual = new List<string>();
        var baseline = Provider(context, expected);
        var compact = Provider(context, actual);
        baseline.CreateBatch(Batch(context, full));
        // Reading the detailed representation and unrelated questions cannot change the selection.
        compact.GetPreviewPlanDetails(7, summary.GetProperty("previewPlanId").GetString()!);
        context.Query("chain");
        compact.CreateBatch(Batch(context, summary));
        Assert.NotEmpty(expected);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SectionDetailsReadTheSamePlanAfterAnotherVariantWasRequested()
    {
        var context = DimensionChainPreviewTests.ISectionContext();
        var full = context.Query("chain");
        var compact = DimensionAnswerProjection.Compact(full);
        var split = context.Query("chainDetails");
        var reads = 0;
        var drawing = "drawing";
        var provider = new ViewDimensionContextProvider(() => drawing, (_, _) => { reads++; return context; }, () => { });
        provider.Get(7);
        var details = provider.GetPreviewPlanDetails(7, compact.GetProperty("previewPlanId").GetString()!);
        Assert.Equal(full.GetProperty("chainPreview").GetRawText(), details.GetProperty("chainPreview").GetRawText());
        Assert.Equal("chain", details.GetProperty("chainView").GetString());
        Assert.Contains(Chains(details), chain => chain.GetProperty("key").GetString() == "Top-chain");
        Assert.Contains(Chains(split), chain => chain.GetProperty("key").GetString() == "Top-profile");
        Assert.Equal(1, reads);
        Assert.Throws<InvalidOperationException>(() => provider.GetPreviewPlanDetails(7, "unknown"));
        Assert.Throws<InvalidOperationException>(() => provider.GetPreviewPlanDetails(8, compact.GetProperty("previewPlanId").GetString()!));
        Assert.Equal(1, reads);
        drawing = "other-drawing";
        Assert.Throws<InvalidOperationException>(() => provider.GetPreviewPlanDetails(7, compact.GetProperty("previewPlanId").GetString()!));
        Assert.Equal(1, reads);
    }

    [Fact]
    public void SafetyStatusAndNumbersArePreservedWithoutDisplayRoundingChanges()
    {
        using var document = JsonDocument.Parse("""
            {"viewId":7,"contextId":"ctx","previewPlanId":"plan","isComplete":false,
             "sectionPreviewStatus":"refused","error":"missing contour","boltSettings":{"x":1},"partSpanMatchToleranceMm":0.5,
             "chainPreview":[{"side":"Bolt","readIncomplete":"unread parts 42","chains":[
             {"key":"bolt-position-Bottom","chainKind":"position","viewClass":"Plan","segments":[549.999,5.001],
              "incomplete":true,"incompleteReason":"blocked","remark":null,"sources":[{"point":[20.123456789,0,0]}]}]}]}
            """);
        var full = document.RootElement;
        var summary = DimensionAnswerProjection.Compact(full);
        Assert.Equal("refused", summary.GetProperty("sectionPreviewStatus").GetString());
        Assert.Equal("missing contour", summary.GetProperty("error").GetString());
        Assert.Equal("unread parts 42", summary.GetProperty("chainPreview")[0].GetProperty("readIncomplete").GetString());
        var chain = Assert.Single(Chains(summary));
        Assert.Equal("[549.999,5.001]", chain.GetProperty("segments").GetRawText());
        Assert.Equal("blocked", chain.GetProperty("incompleteReason").GetString());
        Assert.False(chain.TryGetProperty("remark", out _));
        Assert.Equal(new[] { "key", "chainKind", "viewClass", "segments", "incomplete", "incompleteReason" }, chain.EnumerateObject().Select(field => field.Name));
        Assert.False(summary.TryGetProperty("partSpanMatchToleranceMm", out _));
        var diagnostic = DimensionAnswerProjection.Compact(full, includeDiagnostics: true);
        Assert.True(diagnostic.TryGetProperty("boltSettings", out _));
        Assert.Equal(0.5, diagnostic.GetProperty("partSpanMatchToleranceMm").GetDouble());
        Assert.Equal(20.123456789, full.GetProperty("chainPreview")[0].GetProperty("chains")[0].GetProperty("sources")[0].GetProperty("point")[0].GetDouble());
    }

    [Fact]
    public void CompactAnswerKeepsWhichBoltsALocationChainCarries()
    {
        using var document = JsonDocument.Parse("""
            {"previewPlanId":"plan","chainPreview":[{"side":"Bottom","chains":[
             {"key":"Bottom-overall","segments":[100],"incomplete":false,"restoredBesides":"Bottom-location-bolts","attributesFile":"overall"}]},
             {"side":"Bolt","chains":[
             {"key":"Bottom-location-bolts","segments":[20,60,20],"incomplete":false,"carries":"Bottom-location","points":[[0,0]]}]}]}
            """);
        var chains = Chains(DimensionAnswerProjection.Compact(document.RootElement)).ToArray();
        Assert.Equal("Bottom-location-bolts", chains[0].GetProperty("restoredBesides").GetString());
        Assert.Equal("Bottom-location", chains[1].GetProperty("carries").GetString());
        Assert.False(chains[1].TryGetProperty("points", out _));
    }

    [Fact]
    public void EachAnswerKeepsItsOwnPlanSoDetailsReturnTheAnswerThatNamedIt()
    {
        using var fixture = Fixture("front-default");
        var context = BoltChainCompatibilityTests.Context(fixture.RootElement);
        var shortRead = context.Query("chain", readSheet: () => []);
        var longRead = context.Query("chain,scale,repeats", readSheet: () => []);
        var again = context.Query("chain", readSheet: () => []);
        var shortId = shortRead.GetProperty("previewPlanId").GetString()!;
        var longId = longRead.GetProperty("previewPlanId").GetString()!;
        Assert.NotEqual(shortId, longId);
        Assert.Equal(shortId, again.GetProperty("previewPlanId").GetString());
        var provider = Provider(context, []);
        foreach (var (read, id) in new[] { (shortRead, shortId), (longRead, longId) })
        {
            var details = provider.GetPreviewPlanDetails(7, id);
            foreach (var field in read.EnumerateObject())
                Assert.Equal(field.Value.GetRawText(), details.GetProperty(field.Name).GetRawText());
        }
    }

    [Fact]
    public void DetailsOfAPlanReadOnAnotherSheetAreRefusedLikeABatch()
    {
        using var fixture = Fixture("front-default");
        var context = BoltChainCompatibilityTests.Context(fixture.RootElement);
        var sheet = new List<DimensionChainRepeats.Placed>();
        var id = context.Query("chain", readSheet: () => sheet).GetProperty("previewPlanId").GetString()!;
        var provider = new ViewDimensionContextProvider(() => "drawing", (_, _) => context, () => { },
            readDimensions: _ => new(), validateAttributes: _ => { }, readSheet: () => sheet);
        provider.Get(7);
        provider.GetPreviewPlanDetails(7, id);
        sheet.Add(new(900, 8, "FrontView", [100], true));
        var error = Assert.Throws<InvalidOperationException>(() => provider.GetPreviewPlanDetails(7, id));
        Assert.Contains("changed", error.Message);
    }

    [Fact]
    public void NonChainResponsesAreNotChanged()
    {
        using var document = JsonDocument.Parse("""{"isComplete":false,"error":"unread"}""");
        Assert.Equal(document.RootElement.GetRawText(), DimensionAnswerProjection.Compact(document.RootElement).GetRawText());
    }

    [Fact]
    public void DetailsRequestCarriesOnlyTheViewAndStoredPlanId()
    {
        var args = ModelTools.BuildViewDimensionContextArgs(7, "ignored", "ignored", "ignored", "ignored", true,
            "ignored", "ignored", 8, "ignored", "ignored", "ignored", previewPlanId: "plan-42");
        Assert.Equal(17, args.Length);
        Assert.Equal("get_view_dimension_context", args[0]);
        Assert.Equal("7", args[1]);
        Assert.All(args.Skip(2).Take(14), argument => Assert.Equal("", argument));
        Assert.Equal("plan-42", args[16]);
    }

    private static JsonDocument Fixture(string name) => JsonDocument.Parse(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "TestAssets", "BoltChains", name + ".json")));

    private static IEnumerable<JsonElement> Chains(JsonElement answer) => answer.GetProperty("chainPreview").EnumerateArray()
        .SelectMany(row => row.GetProperty("chains").EnumerateArray());

    private static string[] EligibleKeys(JsonElement answer) => answer.GetProperty("chainPreview").EnumerateArray()
        .Where(row => row.GetProperty("side").GetString() != "Bolt" || !row.TryGetProperty("readIncomplete", out var reason)
            || reason.ValueKind == JsonValueKind.Null || reason.GetString() == "")
        .SelectMany(row => row.GetProperty("chains").EnumerateArray().Where(chain => !chain.GetProperty("incomplete").GetBoolean()
            && (row.GetProperty("side").GetString() != "Bolt" || chain.GetProperty("viewClass").GetString() == "Plan")))
        .Select(chain => chain.GetProperty("key").GetString()!).ToArray();

    private static CreateDimensionsBatchRequest Batch(ViewDimensionContext context, JsonElement answer) => new() {
        ViewId = 7, ContextId = context.ContextId, PreviewPlanId = answer.GetProperty("previewPlanId").GetString(),
        RuleSet = "steel", Chains = EligibleKeys(answer).Select(key => new BatchDimensionChain { Preview = key }).ToList()
    };

    private static ViewDimensionContextProvider Provider(ViewDimensionContext context, List<string> writes)
    {
        var provider = new ViewDimensionContextProvider(() => "drawing", (_, _) => context, () => { },
            (request, distance) => {
                writes.Add(JsonSerializer.Serialize(new { request.Points, request.Direction, request.AttributesFile, request.DimensionType, distance }));
                return new() { Created = true, DimensionId = writes.Count };
            }, readDimensions: _ => new(), validateAttributes: _ => { }, readSheet: () => []);
        provider.Get(7);
        return provider;
    }
}
