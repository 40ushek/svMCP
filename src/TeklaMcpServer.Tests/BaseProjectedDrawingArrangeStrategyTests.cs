using System.Collections.Generic;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class BaseProjectedDrawingArrangeStrategyTests
{
    [Theory]
    [InlineData(60, true)]
    [InlineData(50, true)]
    [InlineData(40, false)]
    public void ShouldPreferRelaxedLayout_UsesScaleCutoff(double scale, bool expected)
    {
        Assert.Equal(expected, BaseProjectedDrawingArrangeStrategy.ShouldPreferRelaxedLayout(scale));
    }

    [Fact]
    public void ComputeFreeArea_ShrinksForWideEdgeBands()
    {
        var free = BaseProjectedDrawingArrangeStrategy.ComputeFreeArea(
            sheetWidth: 420,
            sheetHeight: 297,
            margin: 10,
            gap: 8,
            reservedAreas: new List<ReservedRect>
            {
                new(5, 5, 410, 82),
                new(10, 280, 410, 297)
            });

        Assert.Equal(10, free.minX, 6);
        Assert.Equal(410, free.maxX, 6);
        Assert.Equal(90, free.minY, 6);
        Assert.Equal(272, free.maxY, 6);
    }

    [Fact]
    public void ComputeFreeArea_DoesNotShrinkForLocalCornerTables()
    {
        var free = BaseProjectedDrawingArrangeStrategy.ComputeFreeArea(
            sheetWidth: 420,
            sheetHeight: 297,
            margin: 10,
            gap: 8,
            reservedAreas: new List<ReservedRect>
            {
                new(5, 5, 220, 82),
                new(380, 280, 420, 297)
            });

        Assert.Equal(10, free.minX, 6);
        Assert.Equal(410, free.maxX, 6);
        Assert.Equal(10, free.minY, 6);
        Assert.Equal(287, free.maxY, 6);
    }

    [Fact]
    public void TryCreateCenteredRect_CentersInsideAvailableBand()
    {
        var ok = BaseProjectedDrawingArrangeStrategy.TryCreateCenteredRect(
            width: 120,
            height: 80,
            minX: 60,
            maxX: 360,
            minY: 90,
            maxY: 250,
            out var rect);

        Assert.True(ok);
        Assert.Equal(150, rect.MinX, 6);
        Assert.Equal(270, rect.MaxX, 6);
        Assert.Equal(130, rect.MinY, 6);
        Assert.Equal(210, rect.MaxY, 6);
    }

    [Fact]
    public void TryCreateCenteredRect_ReturnsFalseWhenBandIsTooSmall()
    {
        var ok = BaseProjectedDrawingArrangeStrategy.TryCreateCenteredRect(
            width: 180,
            height: 90,
            minX: 100,
            maxX: 220,
            minY: 80,
            maxY: 200,
            out _);

        Assert.False(ok);
    }

    [Fact]
    public void TryPackSupplementalViews_PlacesViewsOutsideCoreBounds()
    {
        var packed = BaseProjectedDrawingArrangeStrategy.TryPackSupplementalViews(
            new List<(double width, double height)>
            {
                (40, 25),
                (30, 20)
            },
            freeMinX: 10,
            freeMaxX: 300,
            freeMinY: 10,
            freeMaxY: 200,
            gap: 8,
            blockedRect: new ReservedRect(100, 60, 200, 140),
            out var placements);

        Assert.True(packed);
        Assert.Equal(2, placements.Count);

        var sizes = new List<(double width, double height)>
        {
            (40, 25),
            (30, 20)
        };

        for (var i = 0; i < placements.Count; i++)
        {
            var placement = placements[i];
            var size = sizes[i];
            var minX = placement.centerX - size.width / 2.0;
            var maxX = placement.centerX + size.width / 2.0;
            var minY = placement.centerY - size.height / 2.0;
            var maxY = placement.centerY + size.height / 2.0;

            Assert.True(
                maxX <= 100 || minX >= 200 || maxY <= 60 || minY >= 140,
                $"placement unexpectedly overlaps core bounds at ({placement.centerX}, {placement.centerY})");
        }
    }

    [Fact]
    public void TryPackSupplementalViews_ReturnsFalseWhenNoRoomOutsideCore()
    {
        var packed = BaseProjectedDrawingArrangeStrategy.TryPackSupplementalViews(
            new List<(double width, double height)>
            {
                (40, 40)
            },
            freeMinX: 10,
            freeMaxX: 110,
            freeMinY: 10,
            freeMaxY: 110,
            gap: 8,
            blockedRect: new ReservedRect(20, 20, 100, 100),
            out _);

        Assert.False(packed);
    }
}
