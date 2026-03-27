using System.Collections.Generic;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class ViewPlacementValidatorTests
{
    [Fact]
    public void Validate_ReturnsOutOfBounds_WhenRectExitsArea()
    {
        var rect = new ReservedRect(5, 10, 25, 30);

        var result = ViewPlacementValidator.Validate(
            rect,
            minX: 10,
            maxX: 100,
            minY: 10,
            maxY: 100,
            reservedAreas: null,
            otherViewRects: null);

        Assert.False(result.Fits);
        Assert.Equal("out-of-bounds", result.Reason);
        Assert.Empty(result.Blockers);
    }

    [Fact]
    public void Validate_ReturnsReservedOverlap_WhenReservedAreaBlocksCandidate()
    {
        var rect = new ReservedRect(20, 20, 40, 40);
        var reserved = new List<ReservedRect> { new(35, 30, 50, 60) };

        var result = ViewPlacementValidator.Validate(
            rect,
            minX: 0,
            maxX: 100,
            minY: 0,
            maxY: 100,
            reservedAreas: reserved,
            otherViewRects: null);

        Assert.False(result.Fits);
        Assert.Equal("reserved-overlap", result.Reason);
        Assert.Single(result.Blockers);
        Assert.Equal(ViewPlacementBlockerKind.ReservedArea, result.Blockers[0].Kind);
    }

    [Fact]
    public void Validate_ReturnsViewOverlap_WithBlockingViewId()
    {
        var rect = new ReservedRect(20, 20, 40, 40);
        var views = new Dictionary<int, ReservedRect>
        {
            [42] = new(35, 30, 50, 60)
        };

        var result = ViewPlacementValidator.Validate(
            rect,
            minX: 0,
            maxX: 100,
            minY: 0,
            maxY: 100,
            reservedAreas: System.Array.Empty<ReservedRect>(),
            otherViewRectsById: views);

        Assert.False(result.Fits);
        Assert.Equal("view-overlap", result.Reason);
        Assert.Single(result.Blockers);
        Assert.Equal(ViewPlacementBlockerKind.View, result.Blockers[0].Kind);
        Assert.Equal(42, result.Blockers[0].ViewId);
    }
}
