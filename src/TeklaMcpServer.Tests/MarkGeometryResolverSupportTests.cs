using Tekla.Structures.Geometry3d;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class MarkGeometryResolverSupportTests
{
    [Fact]
    public void TryGetPlacingLineAxis_UsesReflectionStartAndEndPoints()
    {
        var placing = new FakeLinePlacing
        {
            StartPoint = new Point(10, 20, 0),
            EndPoint = new Point(13, 24, 0),
        };

        var success = MarkPlacementAxisResolver.TryGetPlacingLineAxis(placing, out var axisDx, out var axisDy);

        Assert.True(success);
        Assert.Equal(0.6, axisDx, 6);
        Assert.Equal(0.8, axisDy, 6);
    }

    [Fact]
    public void TryGetPlacingLineAxis_ReturnsFalseForZeroLengthLine()
    {
        var placing = new FakeLinePlacing
        {
            StartPoint = new Point(10, 20, 0),
            EndPoint = new Point(10, 20, 0),
        };

        var success = MarkPlacementAxisResolver.TryGetPlacingLineAxis(placing, out var axisDx, out var axisDy);

        Assert.False(success);
        Assert.Equal(0, axisDx);
        Assert.Equal(0, axisDy);
    }

    [Fact]
    public void TryGetAngleAxis_ReturnsNormalizedDirection()
    {
        var success = MarkPlacementAxisResolver.TryGetAngleAxis(90, out var axisDx, out var axisDy);

        Assert.True(success);
        Assert.Equal(0, axisDx, 6);
        Assert.Equal(1, axisDy, 6);
    }

    private sealed class FakeLinePlacing
    {
        public Point? StartPoint { get; init; }
        public Point? EndPoint { get; init; }
    }
}
