using System.Collections.Generic;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class TeklaDrawingMarkApiQueryTests
{
    [Fact]
    public void BuildOverlaps_SkipsZeroSizeMarkGeometry()
    {
        var marks = new List<DrawingMarkInfo>
        {
            CreateMark(1, minX: 10, minY: 10, maxX: 10.02, maxY: 10.03),
            CreateMark(2, minX: 10, minY: 10, maxX: 30, maxY: 30)
        };

        var overlaps = TeklaDrawingMarkApi.BuildOverlaps(marks);

        Assert.Empty(overlaps);
    }

    [Fact]
    public void BuildOverlaps_DetectsOverlapWhenOnlyOneDimensionIsThin()
    {
        var marks = new List<DrawingMarkInfo>
        {
            CreateMark(1, minX: 10, minY: 10, maxX: 10.05, maxY: 30),
            CreateMark(2, minX: 9.9, minY: 15, maxX: 25, maxY: 25)
        };

        var overlaps = TeklaDrawingMarkApi.BuildOverlaps(marks);

        var overlap = Assert.Single(overlaps);
        Assert.Equal(1, overlap.IdA);
        Assert.Equal(2, overlap.IdB);
    }

    [Fact]
    public void ShouldSkipOverlapComparison_UsesBothDimensionsForDegeneracy()
    {
        var mark = CreateMark(1, minX: 10, minY: 10, maxX: 10.05, maxY: 30);

        var shouldSkip = TeklaDrawingMarkApi.ShouldSkipOverlapComparison(mark);

        Assert.False(shouldSkip);
    }

    [Fact]
    public void CreateResolvedGeometryInfo_FromMarkGeometryContext_NormalizesAxisFromCorners()
    {
        var geometry = new MarkGeometryContext
        {
            Bounds = TeklaDrawingDimensionsApi.CreateBoundsInfo(10, 20, 30, 40),
            Center = new DrawingPointInfo { X = 20, Y = 30 },
            Width = 20,
            Height = 10,
            Source = "Context",
            IsReliable = true,
        };
        geometry.Corners.Add(new DrawingPointInfo { X = 10, Y = 20, Order = 0 });
        geometry.Corners.Add(new DrawingPointInfo { X = 30, Y = 20, Order = 1 });
        geometry.Corners.Add(new DrawingPointInfo { X = 30, Y = 40, Order = 2 });
        geometry.Corners.Add(new DrawingPointInfo { X = 10, Y = 40, Order = 3 });

        var info = TeklaDrawingMarkApi.CreateResolvedGeometryInfo(geometry);

        Assert.Equal("Context", info.Source);
        Assert.True(info.IsReliable);
        Assert.Equal(20, info.Width);
        Assert.Equal(10, info.Height);
        Assert.Equal(20, info.CenterX);
        Assert.Equal(30, info.CenterY);
        Assert.Equal(0, info.AngleDeg);
        Assert.Equal(1, info.AxisDx);
        Assert.Equal(0, info.AxisDy);
        Assert.Equal(4, info.Corners.Count);
    }

    [Fact]
    public void CreateAxisInfo_MapsContextFieldsWithExpectedRounding()
    {
        var axis = new MarkAxisContext
        {
            Start = new DrawingPointInfo { X = 10.123, Y = 20.456 },
            End = new DrawingPointInfo { X = 30.789, Y = 40.012 },
            Direction = new DrawingVectorInfo { X = 0.707106, Y = 0.707106 },
            Length = 15.678,
            AngleDeg = 45.123,
            IsReliable = true,
        };

        var info = TeklaDrawingMarkApi.CreateAxisInfo(axis);

        Assert.Equal(10.12, info.StartX);
        Assert.Equal(20.46, info.StartY);
        Assert.Equal(30.79, info.EndX);
        Assert.Equal(40.01, info.EndY);
        Assert.Equal(0.7071, info.Dx);
        Assert.Equal(0.7071, info.Dy);
        Assert.Equal(15.68, info.Length);
        Assert.Equal(45.12, info.AngleDeg);
        Assert.True(info.IsReliable);
    }

    private static DrawingMarkInfo CreateMark(int id, double minX, double minY, double maxX, double maxY)
    {
        return new DrawingMarkInfo
        {
            Id = id,
            BboxMinX = minX,
            BboxMinY = minY,
            BboxMaxX = maxX,
            BboxMaxY = maxY,
            ResolvedGeometry = new MarkResolvedGeometryInfo
            {
                MinX = minX,
                MinY = minY,
                MaxX = maxX,
                MaxY = maxY,
                Width = maxX - minX,
                Height = maxY - minY
            }
        };
    }
}
