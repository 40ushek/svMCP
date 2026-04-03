using Tekla.Structures.Drawing;
using Tekla.Structures.Geometry3d;

namespace TeklaMcpServer.Api.Drawing;

internal static class DimensionCreatePlacementHelper
{
    private static readonly Vector HorizontalDirection = new(0, 1, 0);
    private static readonly Vector VerticalDirection = new(1, 0, 0);

    internal static Vector ResolveDirection(string? direction)
    {
        return (direction ?? "horizontal").Trim().ToLowerInvariant() switch
        {
            "vertical" or "v" => VerticalDirection,
            "horizontal" or "h" => HorizontalDirection,
            _ => TryParseVector(direction) ?? HorizontalDirection
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
            double.TryParse(parts[0], out var x) &&
            double.TryParse(parts[1], out var y) &&
            double.TryParse(parts[2], out var z))
        {
            return new Vector(x, y, z);
        }

        return null;
    }
}
