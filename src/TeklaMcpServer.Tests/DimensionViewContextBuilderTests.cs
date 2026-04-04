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
                new PartGeometryInViewResult { Success = true, ViewId = 10, ModelId = 101 },
                new PartGeometryInViewResult { Success = true, ViewId = 10, ModelId = 102 }
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
            }));

        var context = builder.Build(10, 20);

        Assert.Equal(10, context.ViewId);
        Assert.Equal(20, context.ViewScale);
        Assert.Equal([101, 102], context.Parts.ConvertAll(static part => part.ModelId));
        Assert.Equal([9001, 9002, 9003], context.Bolts.ConvertAll(static bolt => bolt.ModelId));
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
            }));

        var context = builder.Build(10, 15);

        Assert.Single(context.Parts);
        Assert.Empty(context.Bolts);
        Assert.Contains("bolt-part:101:missing", context.Warnings);
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
}
