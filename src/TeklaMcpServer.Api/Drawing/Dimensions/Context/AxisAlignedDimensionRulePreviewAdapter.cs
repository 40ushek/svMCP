using System;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>Adapter for the current four-side preview response.</summary>
internal static class AxisAlignedDimensionRulePreviewAdapter
{
    private static OutsideOutlineDimensionPlacement GetPlacement(DimensionRuleResult result) =>
        result.Placement is OutsideOutlineDimensionPlacement placement ? placement
            : throw new NotSupportedException("The current preview requires outside-outline placement.");

    public static int GetRow(DimensionRuleResult result) => GetPlacement(result).Row;

    public static DimensionChainSide GetSide(DimensionRuleResult result)
    {
        _ = GetPlacement(result);
        if (TryGetSide(result, out var side)) return side;
        throw new NotSupportedException("The current preview supports only ascending axis-aligned chains placed outside the outline.");
    }

    public static bool TryGetSide(DimensionRuleResult result, out DimensionChainSide side)
    {
        side = default;
        if (!(result.Placement is OutsideOutlineDimensionPlacement placement)) return false;
        var direction = result.Direction;
        var normal = placement.OutwardNormal;
        if (direction.X == 1 && direction.Y == 0 && normal.X == 0)
        {
            if (normal.Y == 1) { side = DimensionChainSide.Top; return true; }
            if (normal.Y == -1) { side = DimensionChainSide.Bottom; return true; }
        }
        if (direction.X == 0 && direction.Y == 1 && normal.Y == 0)
        {
            if (normal.X == 1) { side = DimensionChainSide.Right; return true; }
            if (normal.X == -1) { side = DimensionChainSide.Left; return true; }
        }
        return false;
    }

    public static object ToPreview(DimensionRuleResult result)
    {
        var side = GetSide(result).ToString();
        if (result.Note != null)
            return new { side, kind = result.Kind, pointIds = Array.Empty<string>(),
                segments = Array.Empty<double>(), points = Array.Empty<object>(), note = result.Note };
        return new {
            side, kind = result.Kind, pointIds = result.Points.Select(point => point.Id).ToArray(),
            segments = result.Segments, points = Array.Empty<object>()
        };
    }
}
