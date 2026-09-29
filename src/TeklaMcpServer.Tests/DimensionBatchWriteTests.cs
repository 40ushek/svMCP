using System.Text.Json;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DimensionBatchWriteTests
{
    [Fact]
    public void InvalidLaterPointIdPreventsEveryWrite()
    {
        var context = ViewDimensionContextTests.Context();
        var ids = BottomIds(context);
        var writes = 0;
        var reads = 0;
        var provider = new ViewDimensionContextProvider(() => "drawing-a", (_, _) => context, () => { },
            (_, _) => { writes++; return new CreateDimensionResult { Created = true }; },
            readDimensions: _ => { reads++; return new GetDimensionsResult(); },
            validateAttributes: _ => { });
        provider.Get(7);

        var batch = Batch(context.ContextId, Chain("first", ids, 120),
            Chain("second", [ids[0], "unknown-point"], 240));
        Assert.ThrowsAny<Exception>(() => provider.CreateBatch(batch));
        Assert.Equal(0, writes);
        Assert.Equal(0, reads);
    }

    [Fact]
    public void ReportsOverallMergedWhenOnlyLocationSetRemains()
    {
        var context = ViewDimensionContextTests.Context();
        var ids = BottomIds(context);
        var writes = new List<double>();
        double[] points = [];
        var readCount = 0;
        var provider = new ViewDimensionContextProvider(() => "drawing-a", (_, _) => context, () => { },
            (request, distance) => {
                writes.Add(distance);
                points = request.Points.ToArray();
                return new CreateDimensionResult { Created = true, DimensionId = writes.Count == 1 ? 101 : 102 };
            },
            readDimensions: _ => ++readCount == 1 ? new GetDimensionsResult() : Snapshot(101, points, 120),
            validateAttributes: _ => { });
        provider.Get(7);

        var result = provider.CreateBatch(Batch(context.ContextId,
            Chain("location", ids, 120), Chain("overall", ids, 240)));

        Assert.Equal(new[] { 120d, 240d }, writes);
        Assert.Equal(2, readCount);
        Assert.Equal("created", result.Chains[0].Status);
        Assert.Equal("merged", result.Chains[1].Status);
        Assert.Equal(101, result.Chains[1].MergedIntoDimensionId);
        Assert.Single(result.FinalDimensions);
    }

    [Fact]
    public void ExistingMatchingChainIsRetainedWithoutAnotherWrite()
    {
        var context = ViewDimensionContextTests.Context();
        var ids = BottomIds(context);
        var resolved = context.ResolvePointIds(ids, "horizontal-down");
        var writes = 0;
        var provider = new ViewDimensionContextProvider(() => "drawing-a", (_, _) => context, () => { },
            (_, _) => { writes++; return new CreateDimensionResult { Created = true }; },
            readDimensions: _ => Snapshot(101, resolved, 120),
            validateAttributes: _ => { });
        provider.Get(7);

        var result = provider.CreateBatch(Batch(context.ContextId, Chain("location", ids, 120)));

        Assert.Equal(0, writes);
        Assert.Equal("retained", result.Chains[0].Status);
        Assert.Equal(101, result.Chains[0].DimensionId);
    }

    [Fact]
    public void HyphenatedDimensionTypeMatchesTeklaReadback()
    {
        var context = ViewDimensionContextTests.Context();
        var ids = BottomIds(context);
        var resolved = context.ResolvePointIds(ids, "horizontal-down");
        var writes = 0;
        var provider = new ViewDimensionContextProvider(() => "drawing-a", (_, _) => context, () => { },
            (_, _) => { writes++; return new CreateDimensionResult { Created = true }; },
            readDimensions: _ => Snapshot(101, resolved, 120, "RelativeAndAbsolute"),
            validateAttributes: _ => { });
        provider.Get(7);

        var chain = Chain("location", ids, 120);
        chain.DimensionType = "relative-and-absolute";
        var result = provider.CreateBatch(Batch(context.ContextId, chain));

        Assert.Equal(0, writes);
        Assert.Equal("retained", result.Chains[0].Status);
        Assert.Equal(101, result.Chains[0].DimensionId);
    }

    [Fact]
    public void FailedWriteStopsLaterChainsAndKeepsEarlierSuccessVisible()
    {
        var context = ViewDimensionContextTests.Context();
        var ids = BottomIds(context);
        var writes = 0;
        double[] points = [];
        var reads = 0;
        var provider = new ViewDimensionContextProvider(() => "drawing-a", (_, _) => context, () => { },
            (request, _) => {
                writes++;
                points = request.Points.ToArray();
                return writes == 1
                    ? new CreateDimensionResult { Created = true, DimensionId = 101 }
                    : new CreateDimensionResult { Error = "write failed", WriteState = new DimensionWriteState { NewDimensionRemoved = true } };
            },
            readDimensions: _ => ++reads == 1 ? new GetDimensionsResult() : Snapshot(101, points, 120),
            validateAttributes: _ => { });
        provider.Get(7);

        var result = provider.CreateBatch(Batch(context.ContextId,
            Chain("first", ids, 120), Chain("second", ids, 240), Chain("third", ids, 360)));

        Assert.Equal(2, writes);
        Assert.Equal(new[] { "created", "failed", "skipped" }, result.Chains.Select(item => item.Status));
        Assert.Single(result.FinalDimensions);
    }

    private static string[] BottomIds(ViewDimensionContext context)
    {
        var json = context.Query("dimensionPoints", "Bottom");
        var points = json.GetProperty("dimensionPoints").GetProperty("sides")[0].GetProperty("points")
            .EnumerateArray().Select(point => point.GetProperty("pointId").GetString()!).ToArray();
        return [points[0], points[^1]];
    }

    private static BatchDimensionChain Chain(string key, string[] ids, double distance) => new()
    {
        Key = key, PointIds = ids, Direction = "horizontal-down", Distance = distance
    };

    private static CreateDimensionsBatchRequest Batch(string contextId, params BatchDimensionChain[] chains) => new()
    {
        ViewId = 7, ContextId = contextId, Chains = chains.ToList()
    };

    private static GetDimensionsResult Snapshot(int id, double[] points, double distance, string? teklaType = null) => new()
    {
        Groups = [new DimensionGroupInfo {
            DimensionType = "Horizontal", TopDirection = -1,
            Items = [new DimensionItemInfo {
                Id = id, DimensionType = "Horizontal", TeklaDimensionType = teklaType ?? string.Empty, Distance = distance,
                ReferenceLine = new DrawingLineInfo { StartY = -distance, EndY = -distance },
                PointList = Enumerable.Range(0, points.Length / 3).Select(i => new DrawingPointInfo {
                    X = points[3 * i], Y = points[3 * i + 1], Order = i
                }).ToList(),
                LengthList = [Math.Abs(points[3] - points[0])]
            }]
        }]
    };
}
