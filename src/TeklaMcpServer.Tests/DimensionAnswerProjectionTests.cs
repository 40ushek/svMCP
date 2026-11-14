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
        // Every key is either placeable or refused, once, and the placeable ones are exactly
        // the ones the code allows today.
        var original = Chains(full).Select(chain => chain.GetProperty("key").GetString()!).ToArray();
        var placeable = SummaryKeys(summary);
        var refused = Refused(summary).Select(chain => chain.GetProperty("key").GetString()!).ToArray();
        Assert.Equal(original.OrderBy(key => key), placeable.Concat(refused).OrderBy(key => key));
        Assert.Equal(EligibleKeys(full), placeable);
        var byKey = Chains(full).ToDictionary(chain => chain.GetProperty("key").GetString()!);
        foreach (var chain in Chains(summary))
        {
            var source = byKey[chain.GetProperty("key").GetString()!];
            foreach (var field in new[] { "segments", "chainKind", "viewClass", "remark", "carries", "restoredBesides" })
                if (source.TryGetProperty(field, out var value) && value.ValueKind != JsonValueKind.Null)
                    Assert.Equal(value.GetRawText(), chain.GetProperty(field).GetRawText());
            foreach (var field in new[] { "points", "pointIds", "sources", "holes", "incomplete", "incompleteReason" })
                Assert.False(chain.TryGetProperty(field, out _));
        }
        foreach (var chain in Refused(summary))
        {
            var source = byKey[chain.GetProperty("key").GetString()!];
            Assert.Contains(chain.GetProperty("reason").GetString(), new[] { "repeat", "incomplete", "boltClass", "boltReadIncomplete" });
            if (source.TryGetProperty("incompleteReason", out var why) && why.ValueKind == JsonValueKind.String)
                Assert.Equal(why.GetString(), chain.GetProperty("note").GetString());
        }
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
        compact.CreateBatch(Batch(context, summary, SummaryKeys(summary)));
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
        Assert.Empty(Chains(summary));
        var refused = Assert.Single(Refused(summary));
        Assert.Equal("bolt-position-Bottom", refused.GetProperty("key").GetString());
        Assert.Equal("incomplete", refused.GetProperty("reason").GetString());
        Assert.Equal("blocked", refused.GetProperty("note").GetString());
        Assert.Equal(new[] { "key", "reason", "note" }, refused.EnumerateObject().Select(field => field.Name));
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
             {"key":"Bottom-location-bolts","viewClass":"Plan","segments":[20,60,20],"incomplete":false,"carries":"Bottom-location","points":[[0,0]]}]}]}
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
    public void RefusalsKeepTheCodesVerdictAndTheViewBlockers()
    {
        using var document = JsonDocument.Parse("""
            {"isComplete":true,"repeats":[{"key":"Top-overall","reason":"sameView","of":"Bottom-overall"}],
             "chainPreview":[{"side":"Bottom","chains":[
               {"key":"Bottom-overall","segments":[100],"incomplete":false},
               {"key":"Top-overall","segments":[100],"incomplete":true,"incompleteReason":"repeats Bottom-overall on this view","repeat":"sameView"},
               {"key":"Left-location","segments":[],"incomplete":true,"incompleteReason":"no part sticks out"}]},
              {"side":"Bolt","chains":[
               {"key":"bolt-a","viewClass":"Plan","segments":[50],"incomplete":false},
               {"key":"bolt-b","viewClass":"Side","segments":[50],"incomplete":false}]},
              {"side":"Bolt","readIncomplete":"unread parts 42","chains":[
               {"key":"bolt-c","viewClass":"Plan","segments":[50],"incomplete":false}]}]}
            """);
        var summary = DimensionAnswerProjection.Compact(document.RootElement);
        Assert.Equal(new[] { "Bottom-overall", "bolt-a" }, SummaryKeys(summary));
        Assert.Equal(new[] { ("Top-overall", "repeat"), ("Left-location", "incomplete"), ("bolt-b", "boltClass"), ("bolt-c", "boltReadIncomplete") },
            Refused(summary).Select(chain => (chain.GetProperty("key").GetString()!, chain.GetProperty("reason").GetString()!)));
        Assert.Equal("repeats Bottom-overall on this view", Refused(summary).First().GetProperty("note").GetString());
        Assert.Equal("Side", Refused(summary).ElementAt(2).GetProperty("note").GetString());
        var unread = summary.GetProperty("chainPreview")[2];
        Assert.Equal("unread parts 42", unread.GetProperty("readIncomplete").GetString());
        Assert.Empty(unread.GetProperty("chains").EnumerateArray());
        Assert.True(summary.GetProperty("isComplete").GetBoolean());
        Assert.False(summary.TryGetProperty("repeats", out _));
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

    private static CreateDimensionsBatchRequest Batch(ViewDimensionContext context, JsonElement answer, string[]? keys = null) => new() {
        ViewId = 7, ContextId = context.ContextId, PreviewPlanId = answer.GetProperty("previewPlanId").GetString(),
        RuleSet = "steel", Chains = (keys ?? EligibleKeys(answer)).Select(key => new BatchDimensionChain { Preview = key }).ToList()
    };

    /// <summary>The short answer's rule: place every key in chains.</summary>
    private static string[] SummaryKeys(JsonElement summary) =>
        Chains(summary).Select(chain => chain.GetProperty("key").GetString()!).ToArray();

    private static IEnumerable<JsonElement> Refused(JsonElement answer) => answer.GetProperty("chainPreview").EnumerateArray()
        .SelectMany(row => row.TryGetProperty("refused", out var refused) ? refused.EnumerateArray() : Enumerable.Empty<JsonElement>());

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
