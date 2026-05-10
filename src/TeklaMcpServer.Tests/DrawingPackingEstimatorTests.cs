using System.Collections.Generic;
using TeklaMcpServer.Api.Drawing;
using TeklaMcpServer.Api.Drawing.ViewLayout;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DrawingPackingEstimatorTests
{
    [Fact]
    public void CheckRelaxedMaxRectsFit_ReturnsTrue_WhenAnyOrderCanPack()
    {
        var frames = new List<(double w, double h)>
        {
            (60, 60),
            (40, 40),
            (40, 40)
        };

        var result = DrawingPackingEstimator.CheckRelaxedMaxRectsFit(
            frames,
            sheetWidth: 110,
            sheetHeight: 110,
            margin: 0,
            gap: 0,
            reservedAreas: new List<ReservedRect>());

        Assert.True(result.Fits);
        Assert.Equal(3, result.FrameCount);
        Assert.True(result.Attempts > 0);
        Assert.False(string.IsNullOrWhiteSpace(result.Order));
    }

    [Fact]
    public void CheckRelaxedMaxRectsFit_ReturnsFalse_WhenReservedAreaBlocksSheet()
    {
        var frames = new List<(double w, double h)>
        {
            (30, 30)
        };
        var reserved = new List<ReservedRect>
        {
            new(0, 0, 100, 100)
        };

        var result = DrawingPackingEstimator.CheckRelaxedMaxRectsFit(
            frames,
            sheetWidth: 100,
            sheetHeight: 100,
            margin: 0,
            gap: 0,
            reservedAreas: reserved);

        Assert.False(result.Fits);
        Assert.Equal(1, result.ReservedAreaCount);
        Assert.True(result.Attempts > 0);
    }
}
