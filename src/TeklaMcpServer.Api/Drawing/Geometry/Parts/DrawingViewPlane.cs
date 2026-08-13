using Tekla.Structures.Geometry3d;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// Guards the one step that makes model geometry mean something on a drawing.
///
/// A part's solid is read in model coordinates. The only thing that turns it into view
/// coordinates is setting the model work plane to the view's own coordinate system before
/// reading. If that system comes back as the identity, the setting is a no-op: the read
/// still succeeds, the numbers still look like coordinates, and every one of them is in the
/// wrong space. A live drawing has produced exactly that - part geometry in model
/// coordinates beside dimensions in view coordinates - and nothing downstream can tell.
///
/// So the identity is refused here rather than reported as a successful read.
/// </summary>
internal static class DrawingViewPlane
{
    /// <summary>
    /// How far a view axis may sit from the model axis and still count as the identity.
    /// Loose on purpose: this is not measuring geometry, only recognising that no rotation
    /// or origin shift is present at all.
    /// </summary>
    private const double IdentityTolerance = 1e-9;

    /// <summary>
    /// True when the view's coordinate system is the model's own, so switching the work
    /// plane to it would change nothing.
    ///
    /// A drawing view of a real assembly never reports this: its origin is wherever the
    /// assembly stands in the model, and a front view's axes are not the model axes. Seeing
    /// it means the drawing is in a state where Tekla will not say where its views are -
    /// after an interrupted close, for instance.
    /// </summary>
    public static bool IsModelPlane(CoordinateSystem? coordinateSystem)
    {
        if (coordinateSystem == null)
            return true;

        return IsZero(coordinateSystem.Origin)
            && PointsAlong(coordinateSystem.AxisX, 1, 0, 0)
            && PointsAlong(coordinateSystem.AxisY, 0, 1, 0);
    }

    /// <summary>The reason to hand back, so every caller refuses in the same words.</summary>
    public const string ModelPlaneReason =
        "The view reports the model coordinate system, so geometry cannot be read in view coordinates. " +
        "Reopen the drawing: this state follows an interrupted close.";

    private static bool IsZero(Point? point) =>
        point == null
        || (System.Math.Abs(point.X) <= IdentityTolerance
            && System.Math.Abs(point.Y) <= IdentityTolerance
            && System.Math.Abs(point.Z) <= IdentityTolerance);

    /// <summary>
    /// Whether an axis points along the given model axis, compared as a direction.
    ///
    /// Direction rather than value, because Tekla coordinate-system axes are not unit
    /// vectors - a part's own axis carries the part's length. An axis of no length says
    /// nothing about orientation and is treated as no evidence against the identity.
    /// </summary>
    private static bool PointsAlong(Vector? axis, double x, double y, double z)
    {
        if (axis == null)
            return true;

        var length = System.Math.Sqrt((axis.X * axis.X) + (axis.Y * axis.Y) + (axis.Z * axis.Z));
        if (length <= IdentityTolerance)
            return true;

        return System.Math.Abs((axis.X / length) - x) <= IdentityTolerance
            && System.Math.Abs((axis.Y / length) - y) <= IdentityTolerance
            && System.Math.Abs((axis.Z / length) - z) <= IdentityTolerance;
    }
}
