using System.Collections.Generic;
using TeklaMcpServer.Api.Drawing;
using TeklaMcpServer.Api.Drawing.ViewLayout;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DrawingLayoutCenteringTests
{
    [Fact]
    public void TryFindCenteringDelta_FindsHorizontalShiftTowardCenter()
    {
        var rects = new List<ReservedRect>
        {
            new(20, 100, 70, 150),
            new(80, 110, 120, 160)
        };

        var ok = ViewGroupCenteringGeometry.TryFindCenteringDelta(
            rects,
            usableMin: 10,
            usableMax: 210,
            reserved: [],
            horizontal: true,
            out var delta);

        Assert.True(ok);
        Assert.InRange(delta, 39.5, 40.5);
    }

    [Fact]
    public void TryFindCenteringDelta_FindsVerticalShiftTowardCenter()
    {
        var rects = new List<ReservedRect>
        {
            new(100, 20, 150, 70),
            new(110, 80, 160, 120)
        };

        var ok = ViewGroupCenteringGeometry.TryFindCenteringDelta(
            rects,
            usableMin: 10,
            usableMax: 210,
            reserved: [],
            horizontal: false,
            out var delta);

        Assert.True(ok);
        Assert.InRange(delta, 39.5, 40.5);
    }

    [Fact]
    public void TryFindCenteringDelta_StopsBeforeReservedOverlap()
    {
        var rects = new List<ReservedRect>
        {
            new(20, 100, 70, 150)
        };
        var reserved = new List<ReservedRect>
        {
            new(95, 95, 130, 155)
        };

        var ok = ViewGroupCenteringGeometry.TryFindCenteringDelta(
            rects,
            usableMin: 10,
            usableMax: 210,
            reserved: reserved,
            horizontal: true,
            out var delta);

        Assert.True(ok);
        Assert.InRange(delta, 24, 25);
    }
}
