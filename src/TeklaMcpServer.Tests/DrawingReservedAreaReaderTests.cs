using System.Collections.Generic;
using Tekla.Common.Geometry;
using Tekla.Structures.DrawingPresentationModel;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DrawingReservedAreaReaderTests
{
    [Fact]
    public void TryGetSegmentBounds_ForRectangularTableFrame_ReturnsMinMaxBox()
    {
        var segment = CreateSegment();
        segment.Primitives.Add(new LinePrimitive(new Vector2(10, 20), new Vector2(110, 20)));
        segment.Primitives.Add(new LinePrimitive(new Vector2(110, 20), new Vector2(110, 70)));
        segment.Primitives.Add(new LinePrimitive(new Vector2(110, 70), new Vector2(10, 70)));
        segment.Primitives.Add(new LinePrimitive(new Vector2(10, 70), new Vector2(10, 20)));

        var ok = DrawingReservedAreaReader.TryGetSegmentBounds(segment, out var bounds);

        Assert.True(ok);
        Assert.Equal(10, bounds.MinX, 6);
        Assert.Equal(20, bounds.MinY, 6);
        Assert.Equal(110, bounds.MaxX, 6);
        Assert.Equal(70, bounds.MaxY, 6);
    }

    [Fact]
    public void TryGetSegmentBounds_ForPolygonAndNestedGroup_ReturnsCombinedBox()
    {
        var outerLoop = new LoopPrimitive(new IPathable[]
        {
            new LinePrimitive(new Vector2(20, 30), new Vector2(60, 30)),
            new LinePrimitive(new Vector2(60, 30), new Vector2(60, 55)),
            new LinePrimitive(new Vector2(60, 55), new Vector2(20, 55)),
            new LinePrimitive(new Vector2(20, 55), new Vector2(20, 30))
        });

        var nested = new PrimitiveGroup(2, new Pen(1, 1, 1), new SolidColorBrush(1), 0);
        nested.Primitives.Add(new LinePrimitive(new Vector2(5, 10), new Vector2(8, 12)));

        var segment = CreateSegment();
        segment.Primitives.Add(new PolygonPrimitive(outerLoop));
        segment.Primitives.Add(nested);

        var ok = DrawingReservedAreaReader.TryGetSegmentBounds(segment, out var bounds);

        Assert.True(ok);
        Assert.Equal(5, bounds.MinX, 6);
        Assert.Equal(10, bounds.MinY, 6);
        Assert.Equal(60, bounds.MaxX, 6);
        Assert.Equal(55, bounds.MaxY, 6);
    }

    [Fact]
    public void TryGetSegmentBounds_ForEmptySegment_ReturnsFalse()
    {
        var segment = CreateSegment();

        var ok = DrawingReservedAreaReader.TryGetSegmentBounds(segment, out var bounds);

        Assert.False(ok);
        Assert.Equal(0, bounds.MinX, 6);
        Assert.Equal(0, bounds.MinY, 6);
        Assert.Equal(0, bounds.MaxX, 6);
        Assert.Equal(0, bounds.MaxY, 6);
    }

    [Fact]
    public void BuildLayoutTableGeometryInfo_ForEmptySegment_MarksTableAsInactive()
    {
        var info = DrawingReservedAreaReader.BuildLayoutTableGeometryInfo(123, "t", CreateSegment());

        Assert.Equal(123, info.TableId);
        Assert.False(info.HasGeometry);
        Assert.Null(info.Bounds);
    }

    [Fact]
    public void BuildLayoutTableGeometryInfo_ForTableFrame_ReturnsBounds()
    {
        var segment = CreateSegment();
        segment.Primitives.Add(new LinePrimitive(new Vector2(10, 20), new Vector2(110, 20)));
        segment.Primitives.Add(new LinePrimitive(new Vector2(110, 20), new Vector2(110, 70)));
        segment.Primitives.Add(new LinePrimitive(new Vector2(110, 70), new Vector2(10, 70)));
        segment.Primitives.Add(new LinePrimitive(new Vector2(10, 70), new Vector2(10, 20)));

        var info = DrawingReservedAreaReader.BuildLayoutTableGeometryInfo(456, "t", segment);

        Assert.Equal(456, info.TableId);
        Assert.True(info.HasGeometry);
        Assert.NotNull(info.Bounds);
        Assert.Equal(10, info.Bounds!.MinX, 6);
        Assert.Equal(20, info.Bounds.MinY, 6);
        Assert.Equal(110, info.Bounds.MaxX, 6);
        Assert.Equal(70, info.Bounds.MaxY, 6);
    }

    private static Segment CreateSegment()
    {
        return new Segment(1, new Pen(1, 1, 1), new SolidColorBrush(1), 0, 0, 0);
    }
}
