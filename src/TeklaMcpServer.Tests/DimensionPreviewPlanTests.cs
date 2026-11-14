using System.Text.Json;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DimensionPreviewPlanTests
{
    [Fact]
    public void IntermediateReadsCannotTurnARefusedRepeatIntoAWritableChain()
    {
        var context = DimensionChainPreviewTests.Context(68);
        var answer = context.Query("chain,repeats", readSheet: () => []);
        context.Query("chain");
        context.Query("scale,parts");
        var writes = 0;
        var provider = Provider(context, () => [], () => writes++);
        var batch = Batch(context, answer, "Top-overall");
        var error = Assert.Throws<ArgumentException>(() => provider.CreateBatch(batch));
        Assert.Contains("not to be created", error.Message);
        Assert.Equal(0, writes);
        batch.PreviewPlanId = null;
        Assert.Throws<InvalidOperationException>(() => provider.CreateBatch(batch));
        Assert.Equal(0, writes);
    }

    [Fact]
    public void DifferentPlansRetainTheirOwnSelectionAndScope()
    {
        var context = DimensionChainPreviewTests.Context(68);
        var first = context.Query("chain", "Top");
        var second = context.Query("chain", "Bottom");
        var writes = 0;
        var provider = Provider(context, () => [], () => writes++);
        provider.CreateBatch(Batch(context, first, "Top-location"));
        provider.CreateBatch(Batch(context, second, "Bottom-location"));
        Assert.Equal(2, writes);
        Assert.Throws<ArgumentException>(() => provider.CreateBatch(Batch(context, first, "Bottom-location")));
        var mismatch = Batch(context, first, "Top-location");
        mismatch.RuleSet = "panel";
        Assert.Throws<InvalidOperationException>(() => provider.CreateBatch(mismatch));
        Assert.Equal(2, writes);
    }

    [Fact]
    public void ChangedSheetRefusesThePlanBeforeWriting()
    {
        var context = DimensionChainPreviewTests.Context(68);
        var sheet = new List<DimensionChainRepeats.Placed>();
        var answer = context.Query("chain", readSheet: () => sheet);
        var writes = 0;
        var provider = Provider(context, () => sheet, () => writes++);
        sheet.Add(new(900, 8, "FrontView", [100], true));
        var error = Assert.Throws<InvalidOperationException>(() => provider.CreateBatch(Batch(context, answer, "Top-location")));
        Assert.Contains("changed", error.Message);
        Assert.Equal(0, writes);
    }

    [Fact]
    public void EvictedPlansAreRefusedAndUnknownPlansNeverTriggerCalculation()
    {
        var plans = new DimensionPreviewPlans();
        var first = plans.Publish("steel", "chain", JsonSerializer.SerializeToElement(new[] { 0 }), null);
        for (var i = 1; i <= 32; i++)
            plans.Publish("steel", "chain", JsonSerializer.SerializeToElement(new[] { i }), null);
        Assert.Throws<InvalidOperationException>(() => plans.Require(first, "steel", "chain"));
        Assert.Throws<InvalidOperationException>(() => plans.Require("unknown", "steel", "chain"));
    }

    [Fact]
    public void FingerprintIncludesPersistedPlacementEvenWhenSegmentsAreUnchanged()
    {
        GetDimensionsResult Snapshot(double distance) => new() { Groups = [new() {
            DimensionType = "Horizontal", Direction = new() { X = 1 }, ViewType = "FrontView", ViewId = 7,
            Items = [new() { Id = 42, Distance = distance,
                PointList = [new() { X = 0 }, new() { X = 100 }], LengthList = [100] }]
        }] };
        var first = DimensionChainRepeats.FromSheet(Snapshot(80));
        var moved = DimensionChainRepeats.FromSheet(Snapshot(160));
        Assert.Equal(first[0].Segments, moved[0].Segments);
        Assert.NotEqual(DimensionPreviewPlans.Fingerprint(first), DimensionPreviewPlans.Fingerprint(moved));
    }

    private static CreateDimensionsBatchRequest Batch(ViewDimensionContext context, JsonElement answer, string key) => new() {
        ViewId = 7, ContextId = context.ContextId, RuleSet = "steel",
        PreviewPlanId = answer.GetProperty("previewPlanId").GetString(), Chains = [new() { Preview = key }]
    };

    private static ViewDimensionContextProvider Provider(ViewDimensionContext context,
        Func<IReadOnlyList<DimensionChainRepeats.Placed>> sheet, Action write)
    {
        var provider = new ViewDimensionContextProvider(() => "drawing", (_, _) => context, () => { },
            (_, _) => { write(); return new() { Created = true, DimensionId = 1 }; },
            readDimensions: _ => new(), validateAttributes: _ => { }, readSheet: sheet);
        provider.Get(7);
        return provider;
    }
}
