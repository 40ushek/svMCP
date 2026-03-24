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
