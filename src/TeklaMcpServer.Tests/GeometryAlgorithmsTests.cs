using System.Linq;
using Tekla.Structures.Geometry3d;
using TeklaMcpServer.Api.Algorithms.Geometry;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class GeometryAlgorithmsTests
{
    [Fact]
    public void ConvexHull_RemovesDuplicatesAndInteriorPoints()
    {
        var points = new[]
        {
            new Point(0, 0, 0),
            new Point(2, 0, 5),
            new Point(2, 1, 0),
            new Point(0, 1, 0),
            new Point(1, 0.5, 0),
            new Point(0, 0, 10),
            new Point(2, 0, -3)
        };

        var hull = ConvexHull.Compute(points);

        Assert.Equal(4, hull.Count);
        Assert.Equal((0.0, 0.0), (hull[0].X, hull[0].Y));
        Assert.Equal((2.0, 0.0), (hull[1].X, hull[1].Y));
        Assert.Equal((2.0, 1.0), (hull[2].X, hull[2].Y));
        Assert.Equal((0.0, 1.0), (hull[3].X, hull[3].Y));
    }

    [Fact]
    public void ConvexHull_ForCollinearPoints_ReturnsEndpoints()
    {
        var points = new[]
        {
            new Point(0, 0, 0),
            new Point(1, 0, 0),
            new Point(2, 0, 0),
            new Point(3, 0, 0)
        };

        var hull = ConvexHull.Compute(points);

        Assert.Equal(2, hull.Count);
        Assert.Equal((0.0, 0.0), (hull[0].X, hull[0].Y));
        Assert.Equal((3.0, 0.0), (hull[1].X, hull[1].Y));
    }

    [Fact]
    public void FarthestPointPair_FindsRectangleDiagonal()
    {
        var points = new[]
        {
            new Point(0, 0, 0),
            new Point(4, 0, 0),
            new Point(4, 3, 0),
            new Point(0, 3, 0),
            new Point(2, 1, 0)
        };

        var result = FarthestPointPair.Find(points);
        var pair = new[]
        {
            (result.First.X, result.First.Y),
            (result.Second.X, result.Second.Y)
        }.OrderBy(p => p.Item1).ThenBy(p => p.Item2).ToArray();

        Assert.Equal(25, result.DistanceSquared, 6);
        Assert.Equal((0.0, 0.0), pair[0]);
        Assert.Equal((4.0, 3.0), pair[1]);
    }

    [Fact]
    public void FarthestPointPair_ForSinglePoint_ReturnsSamePointTwice()
    {
        var point = new Point(7, 9, 1);

        var result = FarthestPointPair.Find(new[] { point });

        Assert.Equal((7.0, 9.0), (result.First.X, result.First.Y));
        Assert.Equal((7.0, 9.0), (result.Second.X, result.Second.Y));
        Assert.Equal(0, result.DistanceSquared);
    }
}
