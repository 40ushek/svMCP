using System;

namespace TeklaMcpServer.Api.Drawing.ViewLayout.Constraints;

/// <summary>
/// Which side of the base view a view belongs on, from the direction it looks in.
/// The view type is not asked: on a drawing of a vertical member the base view is
/// turned, and a "top view" stands beside it, not above.
/// </summary>
internal static class ConstraintViewSideResolver
{
    /// <summary>How closely a view's line of sight must follow a base view axis to count as looking along it.</summary>
    private const double AxisAlignmentThreshold = 0.92;

    /// <param name="baseAxisX">X axis of the base view's coordinate system, in model coordinates.</param>
    /// <param name="baseAxisY">Y axis of the base view's coordinate system.</param>
    /// <param name="viewAxisX">X axis of the view's coordinate system.</param>
    /// <param name="viewAxisY">Y axis of the view's coordinate system.</param>
    /// <param name="standsWhereItLooks">
    /// True for a section or an end view: it stands on the side its section arrows
    /// point to, away from the observer. False for a projection of the base view:
    /// it stands on the observer's side, the top view above.
    /// </param>
    /// <returns>
    /// Unknown when the view does not look along either axis of the base view, for
    /// one a view that looks the same way as the base view or straight back at it.
    /// </returns>
    public static SectionPlacementSide Resolve(
        (double X, double Y, double Z) baseAxisX,
        (double X, double Y, double Z) baseAxisY,
        (double X, double Y, double Z) viewAxisX,
        (double X, double Y, double Z) viewAxisY,
        bool standsWhereItLooks)
    {
        // The view's Z axis points out of the paper, from the model toward the observer.
        var towardObserver = Normalize(Cross(viewAxisX, viewAxisY));
        var alongX = Normalize(baseAxisX);
        var alongY = Normalize(baseAxisY);
        if (towardObserver == null || alongX == null || alongY == null)
            return SectionPlacementSide.Unknown;

        var observerOnX = Dot(towardObserver.Value, alongX.Value);
        if (Math.Abs(observerOnX) >= AxisAlignmentThreshold)
        {
            return (observerOnX > 0) != standsWhereItLooks
                ? SectionPlacementSide.Right
                : SectionPlacementSide.Left;
        }

        var observerOnY = Dot(towardObserver.Value, alongY.Value);
        if (Math.Abs(observerOnY) >= AxisAlignmentThreshold)
        {
            return (observerOnY > 0) != standsWhereItLooks
                ? SectionPlacementSide.Top
                : SectionPlacementSide.Bottom;
        }

        return SectionPlacementSide.Unknown;
    }

    private static (double X, double Y, double Z) Cross((double X, double Y, double Z) a, (double X, double Y, double Z) b)
        => ((a.Y * b.Z) - (a.Z * b.Y), (a.Z * b.X) - (a.X * b.Z), (a.X * b.Y) - (a.Y * b.X));

    private static double Dot((double X, double Y, double Z) a, (double X, double Y, double Z) b)
        => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

    private static (double X, double Y, double Z)? Normalize((double X, double Y, double Z) v)
    {
        var length = Math.Sqrt(Dot(v, v));
        return length <= 1e-9 ? null : (v.X / length, v.Y / length, v.Z / length);
    }
}
