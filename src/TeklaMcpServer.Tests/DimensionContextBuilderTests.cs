using System.Collections.Generic;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DimensionContextBuilderTests
{
    [Fact]
    public void Build_CreatesExternalContextForPartDimensionOutsideLocalBounds()
    {
        var builder = CreateBuilder(new FakePartPointApi(new Dictionary<int, GetPartPointsResult>
        {
            [101] = CreatePartPointsResult(101, [0, 0], [100, 40])
        }));
        var item = CreatePartItem(1, referenceY: -20);

        var context = builder.Build(item);

        Assert.Equal(DimensionContextRole.External, context.Role);
        Assert.True(context.HasSourceGeometry);
        Assert.NotNull(context.LocalBounds);
        Assert.Equal(new[] { 101 }, context.SourceObjectIds);
    }

    [Fact]
    public void Build_CreatesInternalContextForPartDimensionInsideLocalBounds()
    {
        var builder = CreateBuilder(new FakePartPointApi(new Dictionary<int, GetPartPointsResult>
        {
            [101] = CreatePartPointsResult(101, [0, 0], [100, 40])
        }));
        var item = CreatePartItem(1, referenceY: 10);

        var context = builder.Build(item);

        Assert.Equal(DimensionContextRole.Internal, context.Role);
        Assert.True(context.HasSourceGeometry);
    }

    [Fact]
    public void Build_ClassifiesFreeDimensionAsControl()
    {
        var builder = CreateBuilder(new FakePartPointApi([]));
        var item = CreateBaseItem(1, DimensionType.Free, DimensionSourceKind.Unknown, DimensionGeometryKind.Free, referenceY: -20);

        var context = builder.Build(item);

        Assert.Equal(DimensionContextRole.Control, context.Role);
        Assert.False(context.HasSourceGeometry);
    }

    [Fact]
    public void Build_ClassifiesGridDimensionWithoutSourceGeometry()
    {
        var builder = CreateBuilder(new FakePartPointApi([]));
        var item = CreateBaseItem(1, DimensionType.Horizontal, DimensionSourceKind.Grid, DimensionGeometryKind.Horizontal, referenceY: -20);

        var context = builder.Build(item);

        Assert.Equal(DimensionContextRole.Grid, context.Role);
        Assert.False(context.HasSourceGeometry);
        Assert.Empty(context.GeometryWarnings);
    }

    [Fact]
    public void Build_ReturnsPartialContextWhenPartGeometryIsUnavailable()
    {
        var builder = CreateBuilder(new FakePartPointApi([]));
        var item = CreatePartItem(1, referenceY: -20);

        var context = builder.Build(item);

        Assert.Equal(DimensionContextRole.NoSourceGeometry, context.Role);
        Assert.False(context.HasSourceGeometry);
        Assert.Contains("source_geometry_unavailable", context.GeometryWarnings);
    }

    private static DimensionContextBuilder CreateBuilder(IDrawingPartPointApi partPointApi)
    {
        return new DimensionContextBuilder(partPointApi);
    }

    private static DimensionItem CreatePartItem(int dimensionId, double referenceY)
    {
        var item = CreateBaseItem(
            dimensionId,
            DimensionType.Horizontal,
            DimensionSourceKind.Part,
            DimensionGeometryKind.Horizontal,
            referenceY);
        item.Dimension.SourceObjectIds.Add(101);
        return item;
    }

    private static DimensionItem CreateBaseItem(
        int dimensionId,
        DimensionType domainType,
        DimensionSourceKind sourceKind,
        DimensionGeometryKind geometryKind,
        double referenceY)
    {
        var dimension = new DrawingDimensionInfo
        {
            Id = dimensionId,
            ViewId = 10,
            ViewType = "FrontView",
            ViewScale = 15,
            SourceKind = sourceKind,
            GeometryKind = geometryKind,
            ClassifiedDimensionType = domainType
        };

        var item = new DimensionItem
        {
            DimensionId = dimensionId,
            ViewId = 10,
            ViewType = "FrontView",
            ViewScale = 15,
            DomainDimensionType = domainType,
            SourceKind = sourceKind,
            GeometryKind = geometryKind,
            TeklaDimensionType = domainType.ToString(),
            StartX = 0,
            StartY = 0,
            EndX = 100,
            EndY = 0,
            CenterX = 50,
            CenterY = 0,
            StartPointOrder = 0,
            EndPointOrder = 1,
            Distance = 20,
            DirectionX = 1,
            DirectionY = 0,
            TopDirection = -1,
            ReferenceLine = new DrawingLineInfo
            {
                StartX = 0,
                StartY = referenceY,
                EndX = 100,
                EndY = referenceY
            },
            LeadLineMain = new DrawingLineInfo
            {
                StartX = 0,
                StartY = 0,
                EndX = 0,
                EndY = referenceY
            },
            LeadLineSecond = new DrawingLineInfo
            {
                StartX = 100,
                StartY = 0,
                EndX = 100,
                EndY = referenceY
            },
            Dimension = dimension
        };

        item.PointList.Add(new DrawingPointInfo { X = 0, Y = 0, Order = 0 });
        item.PointList.Add(new DrawingPointInfo { X = 100, Y = 0, Order = 1 });
        item.LengthList.Add(100);
        item.RealLengthList.Add(100);
        return item;
    }

    private static GetPartPointsResult CreatePartPointsResult(int modelId, double[] min, double[] max)
    {
        return new GetPartPointsResult
        {
            Success = true,
            ViewId = 10,
            ModelId = modelId,
            Points =
            [
                new DrawingPartPointInfo { Point = [min[0], min[1], 0] },
                new DrawingPartPointInfo { Point = [max[0], min[1], 0] },
                new DrawingPartPointInfo { Point = [max[0], max[1], 0] },
                new DrawingPartPointInfo { Point = [min[0], max[1], 0] }
            ]
        };
    }

    private sealed class FakePartPointApi(Dictionary<int, GetPartPointsResult> results) : IDrawingPartPointApi
    {
        public GetPartPointsResult GetPartPointsInView(int viewId, int modelId)
        {
            return results.TryGetValue(modelId, out var result)
                ? result
                : new GetPartPointsResult
                {
                    Success = false,
                    ViewId = viewId,
                    ModelId = modelId,
                    Error = "missing"
                };
        }

        public List<GetPartPointsResult> GetAllPartPointsInView(int viewId) => [];
    }
}
