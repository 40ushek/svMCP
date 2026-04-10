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

    [Fact]
    public void BuildFromProjectedPolygon_CreatesResolvedAxisGeometry()
    {
        var polygon = new List<double[]>
        {
            new[] { 80.0, 45.0 },
            new[] { 120.0, 45.0 },
            new[] { 120.0, 55.0 },
            new[] { 80.0, 55.0 }
        };

        var geometry = MarkGeometryFactory.BuildFromProjectedPolygon(
            polygon,
            axisDx: 1,
            axisDy: 0,
            source: "Axis",
            isReliable: true);

        Assert.Equal(100, geometry.CenterX);
        Assert.Equal(50, geometry.CenterY);
        Assert.Equal(40, geometry.Width, 6);
        Assert.Equal(10, geometry.Height, 6);
        Assert.Equal(80, geometry.MinX, 6);
        Assert.Equal(120, geometry.MaxX, 6);
        Assert.Equal(45, geometry.MinY, 6);
        Assert.Equal(55, geometry.MaxY, 6);
        Assert.True(geometry.HasAxis);
        Assert.Equal("Axis", geometry.Source);
        Assert.True(geometry.IsReliable);
        Assert.Equal(4, geometry.Corners.Count);
    }

    private sealed class FakeLinePlacing
    {
        public Point? StartPoint { get; init; }
        public Point? EndPoint { get; init; }
    }
}
