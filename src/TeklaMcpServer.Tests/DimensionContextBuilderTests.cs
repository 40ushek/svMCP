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
        Assert.Empty(context.SourceDrawingObjectIds);
        Assert.Equal(new[] { 101 }, context.SourceModelIds);
        Assert.Single(context.RelatedSources);
        Assert.Equal(2, context.PointAssociations.Count);
        Assert.All(context.PointAssociations, static association => Assert.Equal(DimensionPointObjectMappingStatus.Matched, association.Status));
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
        Assert.Contains("missing", context.AssociationWarnings);
    }

    [Fact]
    public void Build_UsesSnapshotCandidatesForPointAssociations()
    {
        var builder = CreateBuilder(new FakePartPointApi(new Dictionary<int, GetPartPointsResult>
        {
            [101] = CreatePartPointsResult(101, [0, 0], [100, 40])
        }));
        var item = CreatePartItem(1, referenceY: -20);

        var context = builder.Build(item);

        Assert.Equal(2, context.PointAssociations.Count);
        Assert.All(context.PointAssociations, static association => Assert.Equal(DimensionPointObjectMappingStatus.Matched, association.Status));
    }

    [Fact]
    public void Build_PopulatesAnnotationGeometryFromSnapshotPath()
    {
        var builder = CreateBuilder(new FakePartPointApi([]));
        var item = CreatePartItem(1, referenceY: -20);

        var context = builder.Build(item);

        Assert.NotNull(context.AnnotationGeometry.ReferenceLine);
        Assert.NotNull(context.AnnotationLineDirection);
        Assert.NotNull(context.AnnotationNormalDirection);
        Assert.Equal(0, context.AnnotationStartAlong);
        Assert.Equal(100, context.AnnotationEndAlong);
        Assert.Equal(1, context.AnnotationSegmentGeometryCount);
        Assert.True(context.AnnotationHasTextBounds);
        Assert.NotNull(context.AnnotationTextBounds);
        Assert.NotNull(context.AnnotationGeometry.DimensionLineStart);
        Assert.NotNull(context.AnnotationGeometry.DimensionLineEnd);
        Assert.Equal(-20, context.AnnotationGeometry.DimensionLineStart!.Y);
        Assert.Equal(-20, context.AnnotationGeometry.DimensionLineEnd!.Y);
        Assert.NotNull(context.AnnotationGeometry.LocalBand);
        Assert.Equal(-20, context.AnnotationBandMinOffset);
        Assert.Equal(0, context.AnnotationBandMaxOffset);
        Assert.Empty(context.AnnotationGeometryWarnings);
    }

    [Fact]
    public void Build_CreatesPartialAnnotationGeometryWhenReferenceLineIsMissing()
    {
        var builder = CreateBuilder(new FakePartPointApi([]));
        var item = CreatePartItem(1, referenceY: -20);
        item.ReferenceLine = null;
        item.Dimension.Segments.Clear();

        var context = builder.Build(item);

        Assert.Null(context.AnnotationGeometry.ReferenceLine);
        Assert.NotNull(context.AnnotationLineDirection);
        Assert.Null(context.AnnotationGeometry.LocalBand);
        Assert.Contains("reference_line_unavailable", context.AnnotationGeometryWarnings);
    }

    [Fact]
    public void Build_UsesSegmentDimensionLineWhenReferenceLineIsMissing()
    {
        var builder = CreateBuilder(new FakePartPointApi([]));
        var item = CreatePartItem(1, referenceY: -20);
        item.ReferenceLine = null;

        var context = builder.Build(item);

        Assert.Null(context.AnnotationGeometry.ReferenceLine);
        Assert.NotNull(context.AnnotationGeometry.DimensionLineStart);
        Assert.NotNull(context.AnnotationGeometry.DimensionLineEnd);
        Assert.Equal(-20, context.AnnotationGeometry.DimensionLineStart!.Y);
        Assert.Equal(-20, context.AnnotationGeometry.DimensionLineEnd!.Y);
        Assert.Equal(0, context.AnnotationStartAlong);
        Assert.Equal(100, context.AnnotationEndAlong);
        Assert.NotNull(context.AnnotationGeometry.LocalBand);
        Assert.Contains("reference_line_unavailable", context.AnnotationGeometryWarnings);
    }

    [Fact]
    public void Build_AddsWarningWhenTextBoundsAreUnavailable()
    {
        var builder = CreateBuilder(new FakePartPointApi([]));
        var item = CreatePartItem(1, referenceY: -20);
        item.Dimension.Segments[0].TextBounds = null;

        var context = builder.Build(item);

        Assert.False(context.AnnotationHasTextBounds);
        Assert.Null(context.AnnotationTextBounds);
        Assert.Contains("text_bounds_unavailable", context.AnnotationGeometryWarnings);
    }

    private static DimensionContextBuilder CreateBuilder(IDrawingPartPointApi partPointApi)
    {
        return new DimensionContextBuilder(new DimensionSourceAssociationResolver(null, partPointApi));
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

        dimension.MeasuredPoints.Add(new DrawingPointInfo { X = 0, Y = 0, Order = 0 });
        dimension.MeasuredPoints.Add(new DrawingPointInfo { X = 100, Y = 0, Order = 1 });
        dimension.Segments.Add(new DimensionSegmentInfo
        {
            Id = dimensionId,
            StartX = 0,
            StartY = 0,
            EndX = 100,
            EndY = 0,
            Distance = 20,
            DirectionX = 1,
            DirectionY = 0,
            TopDirection = -1,
            TextBounds = new DrawingBoundsInfo
            {
                MinX = 35,
                MinY = -28,
                MaxX = 65,
                MaxY = -18
            },
            DimensionLine = new DrawingLineInfo
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
            }
        });

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
