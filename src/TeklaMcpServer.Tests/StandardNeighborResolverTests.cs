using Tekla.Structures.Geometry3d;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class StandardNeighborResolverTests
{
    [Fact]
    public void ResolveFromCoordinateSystems_ReturnsTopWhenViewNormalMatchesReferenceY()
    {
        var reference = CreateCoordinateSystem(
            axisX: new Vector(1, 0, 0),
            axisY: new Vector(0, 0, 1));
        var topLikeView = CreateCoordinateSystem(
            axisX: new Vector(-1, 0, 0),
            axisY: new Vector(0, -1, 0));

        var role = StandardNeighborResolver.ResolveFromCoordinateSystems(reference, topLikeView);

        Assert.Equal(NeighborRole.Top, role);
    }

    [Fact]
    public void ResolveFromCoordinateSystems_ReturnsBottomWhenViewNormalOpposesReferenceY()
    {
        var reference = CreateCoordinateSystem(
            axisX: new Vector(1, 0, 0),
            axisY: new Vector(0, 0, 1));
        var bottomLikeView = CreateCoordinateSystem(
            axisX: new Vector(1, 0, 0),
            axisY: new Vector(0, -1, 0));

        var role = StandardNeighborResolver.ResolveFromCoordinateSystems(reference, bottomLikeView);

        Assert.Equal(NeighborRole.Bottom, role);
    }

    [Fact]
    public void ResolveFromCoordinateSystems_ReturnsSideLeftWhenViewNormalMatchesReferenceX()
    {
        var reference = CreateCoordinateSystem(
            axisX: new Vector(1, 0, 0),
            axisY: new Vector(0, 0, 1));
        var leftNeighborLikeView = CreateCoordinateSystem(
            axisX: new Vector(0, -1, 0),
            axisY: new Vector(0, 0, 1));

        var role = StandardNeighborResolver.ResolveFromCoordinateSystems(reference, leftNeighborLikeView);

        Assert.Equal(NeighborRole.SideLeft, role);
    }

    [Fact]
    public void ResolveFromCoordinateSystems_ReturnsSideRightWhenViewNormalOpposesReferenceX()
    {
        var reference = CreateCoordinateSystem(
            axisX: new Vector(1, 0, 0),
            axisY: new Vector(0, 0, 1));
        var rightNeighborLikeView = CreateCoordinateSystem(
            axisX: new Vector(0, 1, 0),
            axisY: new Vector(0, 0, 1));

        var role = StandardNeighborResolver.ResolveFromCoordinateSystems(reference, rightNeighborLikeView);

        Assert.Equal(NeighborRole.SideRight, role);
    }

    [Fact]
    public void ResolveFromCoordinateSystems_ReturnsUnknownWhenViewNormalIsSkewed()
    {
        var reference = CreateCoordinateSystem(
            axisX: new Vector(1, 0, 0),
            axisY: new Vector(0, 0, 1));
        var skewedView = CreateCoordinateSystem(
            axisX: new Vector(1, 0, 0),
            axisY: new Vector(0, 1, 1));

        var role = StandardNeighborResolver.ResolveFromCoordinateSystems(reference, skewedView);

        Assert.Equal(NeighborRole.Unknown, role);
    }

    private static CoordinateSystem CreateCoordinateSystem(Vector axisX, Vector axisY)
        => new(new Point(0, 0, 0), axisX, axisY);
}
