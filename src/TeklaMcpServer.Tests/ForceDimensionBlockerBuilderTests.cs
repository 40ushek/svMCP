using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class ForceDimensionBlockerBuilderTests
{
    [Fact]
    public void BuildSyntheticObstacles_ComputesBoundsFromPolygonGetBounds()
    {
        var polygon = new[]
        {
            new[] { 10.0, 20.0 },
            new[] { 30.0, 22.0 },
            new[] { 28.0, 40.0 },
            new[] { 8.0,  38.0 }
        };

        var obstacles = ForceDimensionBlockerBuilder.BuildSyntheticObstacles([polygon]);

        Assert.Single(obstacles);
        var obstacle = obstacles[0];
        Assert.Equal(8.0, obstacle.MinX, 6);
        Assert.Equal(20.0, obstacle.MinY, 6);
        Assert.Equal(30.0, obstacle.MaxX, 6);
        Assert.Equal(40.0, obstacle.MaxY, 6);
        Assert.Same(polygon, obstacle.Polygon);
    }

    [Fact]
    public void BuildSyntheticObstacles_AssignsNegativeUniqueIds()
    {
        var polygonA = new[]
        {
            new[] { 0.0, 0.0 },
            new[] { 1.0, 0.0 },
            new[] { 1.0, 1.0 },
            new[] { 0.0, 1.0 }
        };
        var polygonB = new[]
        {
            new[] { 10.0, 10.0 },
            new[] { 11.0, 10.0 },
            new[] { 11.0, 11.0 },
            new[] { 10.0, 11.0 }
        };

        var obstacles = ForceDimensionBlockerBuilder.BuildSyntheticObstacles([polygonA, polygonB]);

        Assert.Equal(2, obstacles.Count);
        Assert.True(obstacles[0].ModelId < 0);
        Assert.True(obstacles[1].ModelId < 0);
        Assert.NotEqual(obstacles[0].ModelId, obstacles[1].ModelId);
    }

    [Fact]
    public void BuildSyntheticObstacles_SkipsDegeneratePolygons()
    {
        var degenerate = new[]
        {
            new[] { 0.0, 0.0 },
            new[] { 1.0, 0.0 }
        };

        var obstacles = ForceDimensionBlockerBuilder.BuildSyntheticObstacles([degenerate]);

        Assert.Empty(obstacles);
    }

    [Fact]
    public void BuildSyntheticObstacles_SkipsCollinearZeroAreaPolygons()
    {
        var collinear = new[]
        {
            new[] { 0.0, 5.0 },
            new[] { 1.0, 5.0 },
            new[] { 2.0, 5.0 },
            new[] { 3.0, 5.0 }
        };

        var obstacles = ForceDimensionBlockerBuilder.BuildSyntheticObstacles([collinear]);

        Assert.Empty(obstacles);
    }

    [Fact]
    public void BuildSyntheticObstacles_SkipsZeroWidthPolygons()
    {
        var zeroWidth = new[]
        {
            new[] { 5.0, 0.0 },
            new[] { 5.0, 1.0 },
            new[] { 5.0, 2.0 },
            new[] { 5.0, 3.0 }
        };

        var obstacles = ForceDimensionBlockerBuilder.BuildSyntheticObstacles([zeroWidth]);

        Assert.Empty(obstacles);
    }
}
