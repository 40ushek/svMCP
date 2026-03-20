using Tekla.Structures.Geometry3d;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class SectionPlacementSideResolverTests
{
    [Fact]
    public void ResolveFromCoordinateSystems_ReturnsTopWhenViewNormalMatchesReferenceZ()
    {
        var reference = CreateCoordinateSystem(
            axisX: new Vector(1, 0, 0),
            axisY: new Vector(0, 1, 0));
        var topLikeView = CreateCoordinateSystem(
            axisX: new Vector(1, 0, 0),
            axisY: new Vector(0, 1, 0));

        var placementSide = SectionPlacementSideResolver.ResolveFromCoordinateSystems(reference, topLikeView);

        Assert.Equal(SectionPlacementSide.Top, placementSide);
    }

    [Fact]
    public void ResolveFromCoordinateSystems_ReturnsBottomWhenViewNormalOpposesReferenceZ()
    {
        var reference = CreateCoordinateSystem(
            axisX: new Vector(1, 0, 0),
            axisY: new Vector(0, 1, 0));
        var bottomLikeView = CreateCoordinateSystem(
            axisX: new Vector(1, 0, 0),
            axisY: new Vector(0, -1, 0));

        var placementSide = SectionPlacementSideResolver.ResolveFromCoordinateSystems(reference, bottomLikeView);

        Assert.Equal(SectionPlacementSide.Bottom, placementSide);
    }

    [Fact]
    public void ResolveFromCoordinateSystems_ReturnsRightWhenViewNormalMatchesReferenceAxis()
    {
        var reference = CreateCoordinateSystem(
            axisX: new Vector(1, 0, 0),
            axisY: new Vector(0, 1, 0));
        var rightLikeView = CreateCoordinateSystem(
            axisX: new Vector(0, 1, 0),
            axisY: new Vector(0, 0, 1));

        var placementSide = SectionPlacementSideResolver.ResolveFromCoordinateSystems(reference, rightLikeView);

        Assert.Equal(SectionPlacementSide.Right, placementSide);
    }

    [Fact]
    public void ResolveFromCoordinateSystems_ReturnsLeftWhenViewNormalOpposesReferenceAxis()
    {
        var reference = CreateCoordinateSystem(
            axisX: new Vector(1, 0, 0),
            axisY: new Vector(0, 1, 0));
        var leftLikeView = CreateCoordinateSystem(
            axisX: new Vector(0, 0, 1),
            axisY: new Vector(0, 1, 0));

        var placementSide = SectionPlacementSideResolver.ResolveFromCoordinateSystems(reference, leftLikeView);

        Assert.Equal(SectionPlacementSide.Left, placementSide);
    }

    [Fact]
    public void ResolveFromCoordinateSystems_ReturnsUnknownWhenViewNormalIsSkewed()
    {
        var reference = CreateCoordinateSystem(
            axisX: new Vector(1, 0, 0),
            axisY: new Vector(0, 1, 0));
        var skewedView = CreateCoordinateSystem(
            axisX: new Vector(1, 0, 0),
            axisY: new Vector(0, 1, 1));

        var placementSide = SectionPlacementSideResolver.ResolveFromCoordinateSystems(reference, skewedView);

        Assert.Equal(SectionPlacementSide.Unknown, placementSide);
    }

    private static CoordinateSystem CreateCoordinateSystem(Vector axisX, Vector axisY)
        => new(new Point(0, 0, 0), axisX, axisY);
}
