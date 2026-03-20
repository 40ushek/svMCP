using Tekla.Structures.Geometry3d;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class CutOrientationResolverTests
{
    [Fact]
    public void ResolveFromCoordinateSystems_ReturnsHorizontalWhenViewNormalMatchesReferenceZ()
    {
        var reference = CreateCoordinateSystem(
            axisX: new Vector(1, 0, 0),
            axisY: new Vector(0, 1, 0));
        var topLikeView = CreateCoordinateSystem(
            axisX: new Vector(1, 0, 0),
            axisY: new Vector(0, 1, 0));

        var orientation = CutOrientationResolver.ResolveFromCoordinateSystems(reference, topLikeView);

        Assert.Equal(CutOrientation.Horizontal, orientation);
    }

    [Fact]
    public void ResolveFromCoordinateSystems_ReturnsVerticalWhenViewNormalMatchesReferenceAxis()
    {
        var reference = CreateCoordinateSystem(
            axisX: new Vector(1, 0, 0),
            axisY: new Vector(0, 1, 0));
        var frontLikeView = CreateCoordinateSystem(
            axisX: new Vector(1, 0, 0),
            axisY: new Vector(0, 0, 1));

        var orientation = CutOrientationResolver.ResolveFromCoordinateSystems(reference, frontLikeView);

        Assert.Equal(CutOrientation.Vertical, orientation);
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

        var orientation = CutOrientationResolver.ResolveFromCoordinateSystems(reference, skewedView);

        Assert.Equal(CutOrientation.Unknown, orientation);
    }

    private static CoordinateSystem CreateCoordinateSystem(Vector axisX, Vector axisY)
        => new(new Point(0, 0, 0), axisX, axisY);
}
