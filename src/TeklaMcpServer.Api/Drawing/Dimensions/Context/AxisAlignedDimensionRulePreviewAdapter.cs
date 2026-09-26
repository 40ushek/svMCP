using System;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>Adapter for the current four-side preview response.</summary>
internal static class AxisAlignedDimensionRulePreviewAdapter
{
    public static DimensionChainSide GetSide(DimensionRuleResult result)
    {
        if (result.Placement is OutsideOutlineDimensionPlacement placement)
        {
            var direction = result.Direction;
            var normal = placement.OutwardNormal;
            if (direction.X == 1 && direction.Y == 0 && normal.X == 0)
            {
                if (normal.Y == 1) return DimensionChainSide.Top;
                if (normal.Y == -1) return DimensionChainSide.Bottom;
            }
            if (direction.X == 0 && direction.Y == 1 && normal.Y == 0)
            {
                if (normal.X == 1) return DimensionChainSide.Right;
                if (normal.X == -1) return DimensionChainSide.Left;
            }
        }
        throw new NotSupportedException("The current preview supports only ascending axis-aligned chains placed outside the outline.");
    }

    public static object ToPreview(DimensionRuleResult result)
    {
        var side = GetSide(result).ToString();
        if (result.Note != null)
            return new { side, kind = result.Kind, pointIds = Array.Empty<string>(),
                segments = Array.Empty<double>(), points = Array.Empty<object>(), note = result.Note };
        return new {
            side, kind = result.Kind, pointIds = result.Points.Select(point => point.Id).ToArray(),
            segments = result.Segments, points = Array.Empty<object>(),
            row = ((OutsideOutlineDimensionPlacement)result.Placement).Row
        };
    }
}
