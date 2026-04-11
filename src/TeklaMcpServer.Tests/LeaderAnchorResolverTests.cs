using TeklaMcpServer.Api.Algorithms.Geometry;
using TeklaMcpServer.Api.Algorithms.Marks;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class LeaderAnchorResolverTests
{
    [Fact]
    public void TryResolveAnchorTarget_PlacesAnchorInsidePolygonAtRequestedDepthFromNearestEdge()
    {
        var polygon = new[]
        {
            new[] { 0.0, 0.0 },
            new[] { 100.0, 0.0 },
            new[] { 100.0, 50.0 },
            new[] { 0.0, 50.0 }
        };

        var resolved = LeaderAnchorResolver.TryResolveAnchorTarget(
            polygon,
            bodyCenterX: 140.0,
            bodyCenterY: 25.0,
            depthMm: 10.0,
            out var anchorX,
            out var anchorY);

        Assert.True(resolved);
        Assert.True(PolygonGeometry.ContainsPoint(polygon, anchorX, anchorY));
        Assert.Equal(90.0, anchorX, 6);
        Assert.Equal(25.0, anchorY, 6);
    }

    [Fact]
    public void TryResolveAnchorTarget_ShrinksDepthForThinDetailsUntilPointIsInside()
    {
        var polygon = new[]
        {
            new[] { 0.0, 0.0 },
            new[] { 6.0, 0.0 },
            new[] { 6.0, 40.0 },
            new[] { 0.0, 40.0 }
        };

        var resolved = LeaderAnchorResolver.TryResolveAnchorTarget(
            polygon,
            bodyCenterX: 40.0,
            bodyCenterY: 20.0,
            depthMm: 10.0,
            out var anchorX,
            out var anchorY);

        Assert.True(resolved);
        Assert.True(PolygonGeometry.ContainsPoint(polygon, anchorX, anchorY));
        Assert.True(anchorX > 0.0);
        Assert.True(anchorX < 6.0);
        Assert.Equal(20.0, anchorY, 6);
    }

    [Fact]
    public void TryResolveAnchorTarget_ReturnsFalseForDegeneratePolygon()
    {
        var polygon = new[]
        {
            new[] { 0.0, 0.0 },
            new[] { 50.0, 0.0 }
        };

        var resolved = LeaderAnchorResolver.TryResolveAnchorTarget(
            polygon,
            bodyCenterX: 100.0,
            bodyCenterY: 20.0,
            depthMm: 10.0,
            out _,
            out _);

        Assert.False(resolved);
    }
}
