using System.Collections.Generic;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DimensionViewContextBuilderTests
{
    [Fact]
    public void Build_CollectsPartsAndDeduplicatesBoltGroups()
    {
        var builder = new DimensionViewContextBuilder(
            new FakePartGeometryApi(
            [
                new PartGeometryInViewResult
                {
                    Success = true,
                    ViewId = 10,
                    ModelId = 101,
                    BboxMin = [0, 0, 0],
                    BboxMax = [10, 5, 0],
                    SolidVertices =
                    [
                        [0d, 0d, 0d],
                        [10d, 0d, 0d],
                        [10d, 5d, 0d],
                        [0d, 5d, 0d]
                    ]
                },
                new PartGeometryInViewResult
                {
                    Success = true,
                    ViewId = 10,
                    ModelId = 102,
                    BboxMin = [20, 0, 0],
                    BboxMax = [30, 10, 0],
                    SolidVertices =
                    [
                        [20d, 0d, 0d],
                        [30d, 0d, 0d],
                        [30d, 10d, 0d],
                        [20d, 10d, 0d]
                    ]
                }
            ]),
            new FakeBoltGeometryApi(new Dictionary<int, PartBoltGeometryInViewResult>
            {
                [101] = new PartBoltGeometryInViewResult
                {
                    Success = true,
                    ViewId = 10,
                    PartId = 101,
                    BoltGroups =
                    [
                        new BoltGroupGeometry { ModelId = 9001 },
                        new BoltGroupGeometry { ModelId = 9002 }
                    ]
                },
                [102] = new PartBoltGeometryInViewResult
                {
                    Success = true,
                    ViewId = 10,
                    PartId = 102,
                    BoltGroups =
                    [
                        new BoltGroupGeometry { ModelId = 9002 },
                        new BoltGroupGeometry { ModelId = 9003 }
                    ]
                }
            }),
            new FakeGridApi(new GetGridAxesResult
            {
                Success = true,
                ViewId = 10,
                Axes =
                [
                    new GridAxisInfo { Guid = "grid-b", Label = "B" },
                    new GridAxisInfo { Guid = "grid-a", Label = "A" },
                    new GridAxisInfo { Guid = "grid-a", Label = "A-duplicate" }
                ]
            }));

        var context = builder.Build(10, 20);

        Assert.Equal(10, context.ViewId);
        Assert.Equal(20, context.ViewScale);
        Assert.Equal([101, 102], context.Parts.ConvertAll(static part => part.ModelId));
        Assert.NotNull(context.PartsBounds);
        Assert.Equal(0, context.PartsBounds!.MinX, 3);
        Assert.Equal(0, context.PartsBounds.MinY, 3);
        Assert.Equal(30, context.PartsBounds.MaxX, 3);
        Assert.Equal(10, context.PartsBounds.MaxY, 3);
        Assert.Equal([0d, 30d, 30d, 20d, 0d], context.PartsHull.ConvertAll(static point => point.X));
        Assert.Equal([0d, 0d, 10d, 10d, 5d], context.PartsHull.ConvertAll(static point => point.Y));
        Assert.Equal([9001, 9002, 9003], context.Bolts.ConvertAll(static bolt => bolt.ModelId));
        Assert.Equal(["grid-a", "grid-b"], context.GridIds);
        Assert.Empty(context.Warnings);
        Assert.False(context.IsEmpty);
    }

    [Fact]
    public void Build_AddsWarningWhenBoltGeometryForPartIsUnavailable()
    {
        var builder = new DimensionViewContextBuilder(
            new FakePartGeometryApi(
            [
                new PartGeometryInViewResult { Success = true, ViewId = 10, ModelId = 101 }
            ]),
            new FakeBoltGeometryApi(new Dictionary<int, PartBoltGeometryInViewResult>
            {
                [101] = new PartBoltGeometryInViewResult
                {
                    Success = false,
                    ViewId = 10,
                    PartId = 101,
                    Error = "missing"
                }
            }),
            new FakeGridApi(new GetGridAxesResult
            {
                Success = false,
                ViewId = 10,
                Error = "grid_missing"
            }));

        var context = builder.Build(10, 15);

        Assert.Single(context.Parts);
        Assert.Empty(context.Bolts);
        Assert.Contains("bolt-part:101:missing", context.Warnings);
        Assert.Contains("grid:grid_missing", context.Warnings);
    }

    [Fact]
    public void Build_FallsBackToGridLabelWhenGuidIsUnavailable()
    {
        var builder = new DimensionViewContextBuilder(
            new FakePartGeometryApi([]),
            new FakeBoltGeometryApi(new Dictionary<int, PartBoltGeometryInViewResult>()),
            new FakeGridApi(new GetGridAxesResult
            {
                Success = true,
                ViewId = 10,
                Axes =
                [
                    new GridAxisInfo { Label = "A" },
                    new GridAxisInfo { Label = "A" },
                    new GridAxisInfo { Label = "1" }
                ]
            }));

        var context = builder.Build(10, 1);

        Assert.Equal(["1", "A"], context.GridIds);
        Assert.False(context.IsEmpty);
    }

    private sealed class FakePartGeometryApi(List<PartGeometryInViewResult> parts) : IDrawingPartGeometryApi
    {
        public List<PartGeometryInViewResult> GetAllPartsGeometryInView(int viewId) => [.. parts];

        public PartGeometryInViewResult GetPartGeometryInView(int viewId, int modelId)
        {
            foreach (var part in parts)
            {
                if (part.ModelId == modelId)
                    return part;
            }

            return new PartGeometryInViewResult
            {
                Success = false,
                ViewId = viewId,
                ModelId = modelId,
                Error = "missing"
            };
        }
    }

    private sealed class FakeBoltGeometryApi(Dictionary<int, PartBoltGeometryInViewResult> results) : IDrawingBoltGeometryApi
    {
        public BoltGroupGeometryInViewResult GetBoltGroupGeometryInView(int viewId, int modelId) =>
            new()
            {
                Success = false,
                ViewId = viewId,
                ModelId = modelId,
                Error = "not_used"
            };

        public PartBoltGeometryInViewResult GetPartBoltGeometryInView(int viewId, int partId)
        {
            return results.TryGetValue(partId, out var result)
                ? result
                : new PartBoltGeometryInViewResult
                {
                    Success = false,
                    ViewId = viewId,
                    PartId = partId,
                    Error = "missing"
                };
        }
    }

    private sealed class FakeGridApi(GetGridAxesResult result) : IDrawingGridApi
    {
        public GetGridAxesResult GetGridAxes(int viewId) => result;
    }
}
