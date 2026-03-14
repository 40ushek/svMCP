using System.Collections.Generic;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class MarkGeometryHelperTests
{
    [Fact]
    public void PolygonsIntersect_ForPerpendicularBoxesWithoutActualCrossing_ReturnsFalse()
    {
        var vertical = new List<double[]>
        {
            new[] { 1397.3, 4402.69 },
            new[] { 1397.3, 4019.37 },
            new[] { 1508.3, 4019.37 },
            new[] { 1508.3, 4402.69 }
        };

        var horizontal = new List<double[]>
        {
            new[] { 1216.14, 3777.9 },
            new[] { 1599.46, 3777.9 },
            new[] { 1599.46, 3888.9 },
            new[] { 1216.14, 3888.9 }
        };

        Assert.False(MarkGeometryHelper.PolygonsIntersect(vertical, horizontal));
        Assert.False(MarkGeometryHelper.RectanglesOverlap(
            1397.3, 4019.37, 1508.3, 4402.69,
            1216.14, 3777.9, 1599.46, 3888.9));
    }

    [Fact]
    public void PolygonsIntersect_ForCrossingRotatedBoxes_ReturnsTrue()
    {
        var first = new List<double[]>
        {
            new[] { 587.13, 4289.42 },
            new[] { 627.51, 4186.03 },
            new[] { 1018.64, 4338.77 },
            new[] { 978.26, 4442.16 }
        };

        var second = new List<double[]>
        {
            new[] { 700.0, 4240.0 },
            new[] { 1090.0, 4240.0 },
            new[] { 1090.0, 4350.0 },
            new[] { 700.0, 4350.0 }
        };

        Assert.True(MarkGeometryHelper.PolygonsIntersect(first, second));
    }
}
