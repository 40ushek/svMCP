using Tekla.Structures.Geometry3d;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

/// <summary>
/// The one check standing between a drawing in a bad state and confident nonsense.
///
/// Reading a part's solid gives model coordinates. Setting the work plane to the view's
/// coordinate system is what makes them view coordinates, and when that system is the
/// model's own the setting does nothing at all - while the read still succeeds. A live
/// drawing produced this after an interrupted close: every number plausible, every number
/// in the wrong space, and two separate bridge processes agreeing on it.
/// </summary>
public sealed class DrawingViewPlaneTests
{
    private static CoordinateSystem System(
        double originX, double originY, double originZ,
        double axisXx, double axisXy, double axisXz,
        double axisYx, double axisYy, double axisYz) =>
        new()
        {
            Origin = new Point(originX, originY, originZ),
            AxisX = new Vector(axisXx, axisXy, axisXz),
            AxisY = new Vector(axisYx, axisYy, axisYz)
        };

    [Fact]
    public void TheModelSystemItselfIsRefused()
    {
        Assert.True(DrawingViewPlane.IsModelPlane(System(0, 0, 0, 1, 0, 0, 0, 1, 0)));
    }

    [Fact]
    public void AxisLengthIsNotOrientation()
    {
        // Tekla coordinate-system axes are not unit vectors - a part's axis carries the
        // part's length. Only the direction says whether this is the model system.
        Assert.True(DrawingViewPlane.IsModelPlane(System(0, 0, 0, 1739.9, 0, 0, 0, 1000, 0)));
    }

    [Fact]
    public void ARealViewOfARealAssemblyIsAccepted()
    {
        // Measured on a live panel: the view sits where the assembly stands in the model,
        // and its axes are not the model axes.
        var view = System(-7497.21, 9845.95, -3225.5, 0, 1, 0, 0, 0, 1);

        Assert.False(DrawingViewPlane.IsModelPlane(view));
    }

    [Fact]
    public void AnOriginAwayFromZeroIsEnoughToAccept()
    {
        Assert.False(DrawingViewPlane.IsModelPlane(System(10, 0, 0, 1, 0, 0, 0, 1, 0)));
    }

    [Fact]
    public void ATurnedViewAtTheOriginIsAccepted()
    {
        Assert.False(DrawingViewPlane.IsModelPlane(System(0, 0, 0, 0, 1, 0, -1, 0, 0)));
    }

    [Fact]
    public void NothingToJudgeCountsAsTheModelSystem()
    {
        // A view that will not say where it is cannot be read in its own coordinates
        // either, so it is refused for the same reason rather than a different one.
        Assert.True(DrawingViewPlane.IsModelPlane(null));
    }

    [Fact]
    public void TheReasonNamesTheStateAndTheCure()
    {
        Assert.Contains("view coordinates", DrawingViewPlane.ModelPlaneReason);
        Assert.Contains("Reopen the drawing", DrawingViewPlane.ModelPlaneReason);
    }
}
