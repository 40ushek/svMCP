using System;
using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// Turns the scalar positions of one calculated chain into visible lines. This is a drawing
/// adapter; <see cref="CalcDimensionChains"/> remains independent of views and overlays.
/// </summary>
public static class DimensionChainDebugOverlayBuilder
{
    public static IReadOnlyList<DrawingDebugShape> CreateLines(
        GeometryGroup group,
        DimensionChainSide side,
        int viewId)
    {
        if (group == null)
            throw new ArgumentNullException(nameof(group));
        if (group.DimensionChains == null)
            throw new InvalidOperationException("Calculate dimension chains before drawing them.");
        if (group.Extent == null)
            throw new InvalidOperationException("A group with no extent cannot be drawn.");

        var extent = group.Extent;
        var middleX = (extent.MinX + extent.MaxX) / 2;
        var middleY = (extent.MinY + extent.MaxY) / 2;
        var shapes = new List<DrawingDebugShape>();
        foreach (var position in group.DimensionChains[side].Positions)
        {
            var line = new DrawingDebugShape
            {
                Kind = "line",
                ViewId = viewId,
                Color = Color(side),
                LineType = "DashDot"
            };

            if (side == DimensionChainSide.Top || side == DimensionChainSide.Bottom)
            {
                line.X1 = position.Coordinate;
                line.Y1 = side == DimensionChainSide.Top ? middleY : extent.MinY;
                line.X2 = position.Coordinate;
                line.Y2 = side == DimensionChainSide.Top ? extent.MaxY : middleY;
            }
            else
            {
                line.X1 = side == DimensionChainSide.Right ? middleX : extent.MinX;
                line.Y1 = position.Coordinate;
                line.X2 = side == DimensionChainSide.Right ? extent.MaxX : middleX;
                line.Y2 = position.Coordinate;
            }

            shapes.Add(line);
        }

        return shapes;
    }

    private static string Color(DimensionChainSide side) => side switch
    {
        DimensionChainSide.Top => "red",
        DimensionChainSide.Bottom => "blue",
        DimensionChainSide.Left => "green",
        DimensionChainSide.Right => "orange",
        _ => throw new ArgumentOutOfRangeException(nameof(side), side, "Unknown dimension chain side.")
    };
}
