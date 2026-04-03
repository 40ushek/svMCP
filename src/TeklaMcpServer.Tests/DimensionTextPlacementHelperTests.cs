using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public class DimensionTextPlacementHelperTests
{
    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(1, -1, -1)]
    [InlineData(-1, 1, -1)]
    [InlineData(-1, -1, 1)]
    [InlineData(0, 1, 1)]
    [InlineData(1, 0, 1)]
    public void ResolveTextSideSign_NormalizesZeroAndMultipliesDirections(
        int topDirection,
        int placingDirectionSign,
        int expected)
    {
        var actual = DimensionTextPlacementHelper.ResolveTextSideSign(topDirection, placingDirectionSign);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ApplyLineOffsets_ClampsNegativeOffsets()
    {
        var trimmed = DimensionTextPlacementHelper.ApplyLineOffsets(
            new DrawingLineInfo { StartX = 0, StartY = 0, EndX = 100, EndY = 0 },
            startOffset: -20,
            endOffset: 10);

        Assert.Equal(0, trimmed.StartX, 3);
        Assert.Equal(90, trimmed.EndX, 3);
    }

    [Fact]
    public void CreateFallbackPolygon_AppliesAlongLineAndAboveLineOffsets()
    {
        var polygon = DimensionTextPolygonPlacementHelper.CreateFallbackPolygon(
            new DrawingLineInfo { StartX = 0, StartY = 0, EndX = 100, EndY = 0 },
            widthAlongLine: 20,
            heightPerpendicularToLine: 10,
            viewScale: 8,
            new DimensionTextPlacementContext("AboveDimensionLine", 1, 0, 0),
            upDirection: (0, 1));

        Assert.NotNull(polygon);
        Assert.Collection(
            polygon!,
            p => { Assert.Equal(42, p[0], 3); Assert.Equal(0, p[1], 3); },
            p => { Assert.Equal(62, p[0], 3); Assert.Equal(0, p[1], 3); },
            p => { Assert.Equal(62, p[0], 3); Assert.Equal(10, p[1], 3); },
            p => { Assert.Equal(42, p[0], 3); Assert.Equal(10, p[1], 3); });
    }
}
