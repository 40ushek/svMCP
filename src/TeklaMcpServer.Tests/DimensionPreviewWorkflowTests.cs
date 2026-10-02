using System.Text.Json;
using TeklaMcpServer.Api.Drawing;
using TeklaMcpServer.Tools;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DimensionPreviewWorkflowTests
{
    [Theory]
    [InlineData("points")]
    [InlineData("chain,dimensionPoints")]
    [InlineData("edges")]
    [InlineData("all")]
    [InlineData("contacts")]
    [InlineData("contactDetails")]
    [InlineData("placement")]
    [InlineData("")]
    [InlineData("chain,")]
    public void PublicMcpRejectsSourceQuestionsWithoutInvokingBridge(string questions)
    {
        Assert.Throws<ArgumentException>(() => ModelTools.GetViewDimensionContext(7, questions));
        Assert.Throws<ArgumentException>(() => DrawingCommandParsers.NormalizePreviewQuestions(questions));
    }

    [Fact]
    public void DefaultAndDetailedQuestionsArePreparedChains()
    {
        Assert.Equal("chain,scale", DrawingCommandParsers.NormalizePreviewQuestions(null));
        Assert.Equal("chain,chaindetails,scale",
            DrawingCommandParsers.NormalizePreviewQuestions(" Chain,chainDetails, SCALE,chain "));
        var parameter = typeof(ModelTools).GetMethod(nameof(ModelTools.GetViewDimensionContext))!
            .GetParameters().Single(p => p.Name == "questions");
        Assert.Equal("chain,scale", parameter.DefaultValue);
        var context = ViewDimensionContextTests.Context();
        var reply = context.Query(DrawingCommandParsers.NormalizePreviewQuestions(null));
        Assert.True(reply.TryGetProperty("chainPreview", out var rows));
        Assert.False(reply.TryGetProperty("sides", out _));
        Assert.False(reply.TryGetProperty("dimensionPoints", out _));
        Assert.All(rows.EnumerateArray().SelectMany(row => row.GetProperty("chains").EnumerateArray()),
            chain => {
                Assert.False(chain.TryGetProperty("row", out _));
                Assert.True(chain.TryGetProperty("incomplete", out var flag));
                if (flag.GetBoolean()) Assert.True(chain.TryGetProperty("incompleteReason", out _));
            });
        // Internal point catalog is still available to existing tests and lower-level APIs.
        Assert.True(context.Query("dimensionPoints").TryGetProperty("dimensionPoints", out _));
    }

    [Fact]
    public void RowsAreIndependentPerSideAndSupportMoreThanTwoChains()
    {
        var context = ViewDimensionContextTests.Context();
        var writes = new List<(string Direction, double Distance)>();
        var provider = Provider(context, () => new GetDimensionsResult(),
            (r, distance) => writes.Add((r.Direction, distance)));
        provider.CreateBatch(Batch(context,
            Chain(context, "b1", "Bottom", "horizontal-down"),
            Chain(context, "right-overall", "Right", "vertical"),
            Chain(context, "b2", "Bottom", "horizontal-down"),
            Chain(context, "left", "Left", "vertical-left"),
            Chain(context, "b3", "Bottom", "horizontal-down")));
        Assert.Equal(new[] { 80d, 80d, 160d, 80d, 240d }, writes.Select(w => w.Distance));
    }

    [Fact]
    public void RepeatWithoutOffsetsRetainsUniqueExistingPlacement()
    {
        var context = ViewDimensionContextTests.Context();
        var chain = Chain(context, "bottom", "Bottom", "horizontal-down");
        var points = context.ResolvePointIds(chain.PointIds!, chain.Direction);
        var writes = 0;
        var provider = Provider(context, () => Snapshot(101, points, 275), (_, _) => writes++);
        var result = provider.CreateBatch(Batch(context, chain));
        Assert.Equal(0, writes);
        Assert.Equal("retained", result.Chains.Single().Status);
        Assert.Equal(101, result.Chains.Single().DimensionId);
    }

    [Fact]
    public void OccupiedSideRequiresExplicitOffsetForNewChainsBeforeAnyWrite()
    {
        var context = ViewDimensionContextTests.Context();
        var bottom = Chain(context, "new-bottom", "Bottom", "horizontal-down");
        var points = context.ResolvePointIds(bottom.PointIds!, bottom.Direction);
        var writes = 0;
        // Existing chain on the same side has different points.
        var shifted = points.Select((v, i) => i % 3 == 0 ? v + 10 : v).ToArray();
        var provider = Provider(context, () => Snapshot(101, shifted, 120), (_, _) => writes++);
        var batch = Batch(context, Chain(context, "right", "Right", "vertical"), bottom);
        var error = Assert.Throws<ArgumentException>(() => provider.CreateBatch(batch));
        Assert.Contains("occupied Bottom", error.Message);
        Assert.Equal(0, writes);
        bottom.PaperGapMm = 16;
        provider.CreateBatch(batch);
        Assert.Equal(2, writes);
    }

    [Fact]
    public void MultipleGeometryMatchesRequireExplicitOffset()
    {
        var context = ViewDimensionContextTests.Context();
        var chain = Chain(context, "bottom", "Bottom", "horizontal-down");
        var points = context.ResolvePointIds(chain.PointIds!, chain.Direction);
        var snapshot = Snapshot(101, points, 120);
        snapshot.Groups.Add(Snapshot(102, points, 240).Groups[0]);
        var writes = 0;
        var provider = Provider(context, () => snapshot, (_, _) => writes++);
        Assert.Throws<ArgumentException>(() => provider.CreateBatch(Batch(context, chain)));
        Assert.Equal(0, writes);
        chain.Distance = 120;
        var result = provider.CreateBatch(Batch(context, chain));
        Assert.Equal("retained", result.Chains.Single().Status);
        Assert.Equal(101, result.Chains.Single().DimensionId);
        Assert.Equal(0, writes);
    }

    [Fact]
    public void ExplicitDifferentOffsetCreatesInsteadOfRetainingOldPlacement()
    {
        var context = ViewDimensionContextTests.Context();
        var chain = Chain(context, "bottom", "Bottom", "horizontal-down");
        var points = context.ResolvePointIds(chain.PointIds!, chain.Direction);
        chain.Distance = 240;
        var writes = new List<double>();
        var provider = Provider(context, () => Snapshot(101, points, 120),
            (_, distance) => writes.Add(distance));
        var result = provider.CreateBatch(Batch(context, chain));
        Assert.Equal(new[] { 240d }, writes);
        Assert.NotEqual("retained", result.Chains.Single().Status);
    }

    [Fact]
    public void RequestedTypeMismatchIsNotRetainedWithoutExplicitPlacement()
    {
        var context = ViewDimensionContextTests.Context();
        var chain = Chain(context, "bottom", "Bottom", "horizontal-down");
        chain.DimensionType = "Absolute";
        var points = context.ResolvePointIds(chain.PointIds!, chain.Direction);
        var provider = Provider(context, () => Snapshot(101, points, 120),
            (_, _) => throw new Exception("Unexpected write"));
        Assert.Throws<ArgumentException>(() => provider.CreateBatch(Batch(context, chain)));
    }

    private static ViewDimensionContextProvider Provider(ViewDimensionContext context,
        Func<GetDimensionsResult> read, Action<CreateDimensionRequest, double> write)
    {
        var id = 200;
        var provider = new ViewDimensionContextProvider(() => "drawing-a", (_, _) => context, () => { },
            (r, distance) => { write(r, distance); return new CreateDimensionResult { Created = true, DimensionId = ++id }; },
            readDimensions: _ => read(), validateAttributes: _ => { });
        provider.Get(7);
        return provider;
    }

    private static BatchDimensionChain Chain(ViewDimensionContext context, string key, string side, string direction)
    {
        var points = context.Query("dimensionPoints", side).GetProperty("dimensionPoints")
            .GetProperty("sides")[0].GetProperty("points").EnumerateArray()
            .Select(p => p.GetProperty("pointId").GetString()!).ToArray();
        return new() { Key = key, Direction = direction, PointIds = [points[0], points[^1]] };
    }

    private static CreateDimensionsBatchRequest Batch(ViewDimensionContext context, params BatchDimensionChain[] chains) =>
        new() { ViewId = 7, ContextId = context.ContextId, Chains = chains.ToList() };

    private static GetDimensionsResult Snapshot(int id, double[] points, double distance) => new() {
        Groups = [new DimensionGroupInfo {
            DimensionType = "Horizontal", TopDirection = -1,
            Items = [new DimensionItemInfo {
                Id = id, DimensionType = "Horizontal", TeklaDimensionType = "Relative", Distance = distance,
                ReferenceLine = new DrawingLineInfo { StartY = -distance, EndY = -distance },
                PointList = Enumerable.Range(0, points.Length / 3).Select(i => new DrawingPointInfo {
                    X = points[3 * i], Y = points[3 * i + 1], Order = i
                }).ToList()
            }]
        }]
    };
}
