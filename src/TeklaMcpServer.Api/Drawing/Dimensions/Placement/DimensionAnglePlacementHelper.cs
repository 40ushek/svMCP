using Tekla.Structures.Geometry3d;

namespace TeklaMcpServer.Api.Drawing;

internal static class DimensionAnglePlacementHelper
{
    internal static string NormalizeAttributesFile(string? attributesFile)
        => string.IsNullOrWhiteSpace(attributesFile) ? "standard" : attributesFile!.Trim();

    /// <summary>
    /// True when the plate normal points opposite to the view normal. In that
    /// case the contour winding (CCW in the plate's own work plane) appears
    /// mirrored in the view, so prev/next must be swapped to keep selecting the
    /// interior angle at each vertex.
    /// </summary>
    internal static bool IsContourFlipped(Vector plateNormal, Vector viewNormal)
    {
        var plate = new Vector(plateNormal.X, plateNormal.Y, plateNormal.Z);
        var view = new Vector(viewNormal.X, viewNormal.Y, viewNormal.Z);
        plate.Normalize();
        view.Normalize();
        return plate.Dot(view) < 0.0;
    }

    /// <summary>
    /// Returns the contour indices to use as the angle's first/second leg
    /// endpoints for the vertex at <paramref name="index"/>. When the contour is
    /// flipped in the view, prev and next are swapped.
    /// </summary>
    internal static (int First, int Second) ResolveNeighbors(int index, int count, bool flipped)
    {
        var before = (((index - 1) % count) + count) % count;
        var after = (index + 1) % count;
        return flipped ? (after, before) : (before, after);
    }

    /// <summary>
    /// Angle in degrees (0..180) between the two contour legs meeting at
    /// <paramref name="vertex"/>, where the legs run to <paramref name="first"/>
    /// and <paramref name="second"/>.
    /// </summary>
    internal static double LegAngleDegrees(Point vertex, Point first, Point second)
    {
        var ux = first.X - vertex.X;
        var uy = first.Y - vertex.Y;
        var wx = second.X - vertex.X;
        var wy = second.Y - vertex.Y;
        var cross = (ux * wy) - (uy * wx);
        var dot = (ux * wx) + (uy * wy);
        return System.Math.Atan2(System.Math.Abs(cross), dot) * (180.0 / System.Math.PI);
    }

    internal static bool IsRightAngle(double angleDegrees, double toleranceDegrees = 0.5)
        => System.Math.Abs(angleDegrees - 90.0) <= toleranceDegrees;
}
