using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class BoltDimensionWriteTests
{
    [Fact]
    public void RepeatedPartResolutionUsesCachedPreviewAndReturnsIndependentCoordinates()
    {
        var context = Context();
        var sides = (DimensionChainSide[])Enum.GetValues(typeof(DimensionChainSide));
        var preview = context.BuildBoltPreview(sides);
        var first = context.ResolveBoltProposal("bolt-part-10-Bottom", 10, "horizontal-down");
        var expected = first.ToArray();
        first[0] = 999;
        Assert.Equal(expected, context.ResolveBoltProposal("bolt-part-10-Bottom", 10, "horizontal-down"));
        Assert.Equal(preview, context.BuildBoltPreview(sides));
        Assert.Throws<ArgumentException>(() => context.ResolveBoltProposal("bolt-part-10-Bottom", 10, "horizontal"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitBoltSelectionUsesIndependentCoordinatesAndSharedWriteReadback(bool edge)
    {
        var context = Context();
        var writes = new List<CreateDimensionRequest>();
        var provider = Provider(context, writes);
        var proposal = edge ? "bolt-42-X-0-part-10-edge-min" : "bolt-42-X-0";
        var chain = new BatchDimensionChain { BoltProposal = proposal, PartId = 10,
            Direction = "horizontal-down", Distance = 80 };
        var result = provider.CreateBatch(Batch(context, chain));
        var request = Assert.Single(writes);
        Assert.Equal(edge ? new[] { 0d, 20, 0, 10, 20, 0 } : new[] { 10d, 20, 0, 70, 20, 0 }, request.Points);
        Assert.Empty(request.PointIds);
        Assert.Equal(proposal, Assert.Single(result.Chains).Key);
        Assert.Equal("created", result.Chains[0].Status);
        Assert.Contains("unverified", result.Chains[0].BoltGeometryVerification);
        Assert.Null(chain.PointIds);
        Assert.Empty(chain.Key); // Resolution does not mutate the caller's selection.
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("wrong-part")]
    [InlineData("edge-other-part")]
    [InlineData("wrong-axis")]
    [InlineData("no-direction")]
    [InlineData("no-part")]
    [InlineData("point-ids")]
    [InlineData("preview")]
    [InlineData("blocked")]
    public void InvalidBoltReferencesStopBeforeAnyWrite(string mode)
    {
        var context = Context(mode == "blocked");
        var writes = new List<CreateDimensionRequest>();
        var provider = Provider(context, writes);
        var chain = new BatchDimensionChain { BoltProposal = "bolt-42-X-0", PartId = 10,
            Direction = "horizontal-down", Distance = 80 };
        switch (mode)
        {
            case "unknown": chain.BoltProposal = "missing"; break;
            case "wrong-part": chain.PartId = 99; break;
            case "edge-other-part": chain.BoltProposal = "bolt-42-X-0-part-10-edge-min"; chain.PartId = 20; break;
            case "wrong-axis": chain.Direction = "vertical"; break;
            case "no-direction": chain.Direction = ""; break;
            case "no-part": chain.PartId = null; break;
            case "point-ids": chain.PointIds = []; break;
            case "preview": chain.Preview = "Top-location"; break;
        }
        Assert.Throws<ArgumentException>(() => provider.CreateBatch(Batch(context, chain)));
        Assert.Empty(writes);
    }

    [Fact]
    public void EntireBatchIsPreflightedBeforeWritingAValidEarlierBoltChain()
    {
        var context = Context();
        var writes = new List<CreateDimensionRequest>();
        var provider = Provider(context, writes);
        Assert.Throws<ArgumentException>(() => provider.CreateBatch(Batch(context,
            new BatchDimensionChain { BoltProposal = "bolt-42-X-0", PartId = 10, Direction = "horizontal-down", Distance = 80 },
            new BatchDimensionChain { BoltProposal = "missing", PartId = 10, Direction = "horizontal-down", Distance = 100 })));
        Assert.Empty(writes);
    }

    [Fact]
    public void ExpiredContextCannotResolveAnOtherwiseValidBoltProposal()
    {
        var context = Context();
        var writes = new List<CreateDimensionRequest>();
        var provider = Provider(context, writes);
        provider.Get(7, refresh: true);
        var batch = Batch(context, new BatchDimensionChain { BoltProposal = "bolt-42-X-0", PartId = 10,
            Direction = "horizontal-down", Distance = 80 });
        batch.ContextId = "expired";
        Assert.Throws<InvalidOperationException>(() => provider.CreateBatch(batch));
        Assert.Empty(writes);
    }

    [Fact]
    public void BoltProposalParserKeepsIndependentReferenceFields()
    {
        var request = DrawingCommandParsers.ParseCreateDimensionsBatchRequest([
            "create_dimensions_batch", "7", "ctx", "[{\"boltProposal\":\"bolt-42-X-0\",\"partId\":10,\"direction\":\"horizontal-down\"}]"
        ]);
        var chain = Assert.Single(request.Chains);
        Assert.Equal("bolt-42-X-0", chain.BoltProposal);
        Assert.Equal(10, chain.PartId);
        Assert.False(chain.PointIdsSpecified);
    }

    private static CreateDimensionsBatchRequest Batch(ViewDimensionContext context, params BatchDimensionChain[] chains) =>
        new() { ViewId = 7, ContextId = context.ContextId, Chains = chains.ToList() };

    private static ViewDimensionContext Context(bool outside = false)
    {
        var group = new BoltGroupGeometry { ModelId = 42, Shape = "BoltArray", PartToBeBoltedId = 10,
            FirstPosition = [10, 20, 0], SecondPosition = [70, 20, 0],
            Positions = [new() { Index = 5, Point = [10, 20, 0] }, new() { Index = 9, Point = [70, 20, 0] }] };
        var bolts = ViewBoltGeometrySnapshot.Capture(7, [10], id => new() {
            Success = true, ViewId = 7, PartId = id, BoltGroups = [group]
        }, new DepthBox(outside ? 30 : -100, -100, -100, 1000, 1000, 1000), point => point, true);
        return ViewDimensionContextTests.Context(bolts: bolts);
    }

    private static ViewDimensionContextProvider Provider(ViewDimensionContext context, List<CreateDimensionRequest> writes)
    {
        var provider = new ViewDimensionContextProvider(() => "drawing", (_, _) => context, () => { },
            write: (request, _) => { writes.Add(request); return new CreateDimensionResult { Created = true, DimensionId = 123 }; },
            readDimensions: _ => writes.Count == 0 ? new GetDimensionsResult() : new() {
                Groups = [new() { DimensionType = "Horizontal", TopDirection = -1,
                    Items = [new() { Id = 123, DimensionType = "Horizontal", TeklaDimensionType = "Relative", Distance = 80,
                        PointList = Enumerable.Range(0, writes[0].Points.Length / 3).Select(index => new DrawingPointInfo {
                            X = writes[0].Points[index * 3], Y = writes[0].Points[index * 3 + 1], Order = index
                        }).ToList() }] }]
            }, validateAttributes: _ => { });
        provider.Get(7);
        return provider;
    }
}
