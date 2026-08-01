using System.Globalization;
using Tekla.Structures.Drawing;
using Tekla.Structures.Geometry3d;

namespace TeklaMcpServer.Api.Drawing;

internal static class DimensionCreatePlacementHelper
{
    private static readonly Vector HorizontalDirection = new(0, 1, 0);
    private static readonly Vector HorizontalDownDirection = new(0, -1, 0);
    private static readonly Vector VerticalDirection = new(1, 0, 0);
    private static readonly Vector VerticalLeftDirection = new(-1, 0, 0);

    /// <summary>
    /// Resolves the vector handed to <c>CreateDimensionSet</c>. It picks BOTH the axis and the
    /// side the dimension line sits on: (0,1,0) / (0,-1,0) for a horizontal dimension,
    /// (1,0,0) / (-1,0,0) for a vertical one.
    ///
    /// Flip the vector to move the line to the other side — do NOT negate the distance instead.
    /// Measured on TS2025: the same points with (-1,0,0) and distance +200 put the line exactly at
    /// X=-200 with Distance reading back as 200, while (1,0,0) with distance -200 produced a set
    /// that did not survive. A negative distance also makes Distance report values unrelated to
    /// the requested offset.
    ///
    /// The vector does not affect the reading order: Tekla normalizes the point list by view
    /// coordinates, so a chain may run top-to-bottom or right-to-left whatever order was passed.
    /// </summary>
    internal static Vector ResolveDirection(string? direction)
        => TryResolveDirection(direction)
           ?? throw new System.ArgumentException(
               $"unknown dimension direction '{direction}'. Use horizontal / horizontal-down / " +
               "vertical / vertical-left, or an explicit 'dx,dy,dz' vector with a dot as the " +
               "decimal separator.",
               nameof(direction));

    /// <summary>
    /// Returns null for anything unrecognised instead of guessing.
    ///
    /// Falling back to horizontal used to hide typos, and a wrong axis is not a cosmetic problem:
    /// recreate_dimension would silently rebuild a vertical chain as a horizontal one.
    /// </summary>
    internal static Vector? TryResolveDirection(string? direction)
    {
        return (direction ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "vertical" or "v" or "vertical-right" => VerticalDirection,
            "vertical-left" or "v-" => VerticalLeftDirection,
            "horizontal" or "h" or "horizontal-up" => HorizontalDirection,
            "horizontal-down" or "h-" => HorizontalDownDirection,
            _ => TryParseVector(direction)
        };
    }

    internal static string? NormalizeAttributesFile(string? attributesFile)
        => string.IsNullOrWhiteSpace(attributesFile) ? null : attributesFile!.Trim();

    internal static StraightDimensionSet.StraightDimensionSetAttributes CreateAttributes(string? attributesFile)
    {
#pragma warning disable CS0618
        var attributes = new StraightDimensionSet.StraightDimensionSetAttributes();
#pragma warning restore CS0618
        var normalizedAttributes = NormalizeAttributesFile(attributesFile);
        if (!string.IsNullOrEmpty(normalizedAttributes))
            attributes.LoadAttributes(normalizedAttributes);

        return attributes;
    }

    internal static Vector? TryParseVector(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return null;

        var value = s!.Trim();
        var parts = value.Split(',');
        if (parts.Length == 3 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) &&
            double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
        {
            return new Vector(x, y, z);
        }

        return null;
    }
}
