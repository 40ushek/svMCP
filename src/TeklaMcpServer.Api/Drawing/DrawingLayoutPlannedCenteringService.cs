using System;
using System.Collections.Generic;
using System.Linq;
using TeklaMcpServer.Api.Drawing.ViewLayout;

namespace TeklaMcpServer.Api.Drawing;

internal static class DrawingLayoutPlannedCenteringService
{
    public static IReadOnlyList<DrawingLayoutPlannedView> TryCenterViews(
        IReadOnlyList<DrawingLayoutPlannedView> plannedViews,
        double sheetWidth,
        double sheetHeight,
        double margin,
        IReadOnlyList<ReservedRect> reservedAreas)
    {
        var nonDetailRects = plannedViews
            .Where(static v => !string.Equals(v.SemanticKind, "Detail", StringComparison.OrdinalIgnoreCase))
            .Select(static v => v.LayoutRect)
            .ToList<ReservedRect>();

        if (nonDetailRects.Count == 0)
            return plannedViews;

        var usableMinX = margin;
        var usableMaxX = sheetWidth - margin;
        var usableMinY = margin;
        var usableMaxY = sheetHeight - margin;

        var dx = 0.0;
        if (ViewGroupCenteringGeometry.TryFindCenteringDelta(
            nonDetailRects, usableMinX, usableMaxX, reservedAreas, horizontal: true, out var foundDx))
        {
            dx = foundDx;
            nonDetailRects = ViewGroupCenteringGeometry.ShiftRects(nonDetailRects, dx, 0);
        }

        var dy = 0.0;
        if (ViewGroupCenteringGeometry.TryFindCenteringDelta(
            nonDetailRects, usableMinY, usableMaxY, reservedAreas, horizontal: false, out var foundDy))
            dy = foundDy;

        if (Math.Abs(dx) < 1.0 && Math.Abs(dy) < 1.0)
            return plannedViews;

        return plannedViews
            .Select(v =>
            {
                var isDetail = string.Equals(v.SemanticKind, "Detail", StringComparison.OrdinalIgnoreCase);
                var shiftX = isDetail ? 0.0 : dx;
                var shiftY = isDetail ? 0.0 : dy;
                return new DrawingLayoutPlannedView
                {
                    Id = v.Id,
                    ViewType = v.ViewType,
                    SemanticKind = v.SemanticKind,
                    Name = v.Name,
                    OriginX = v.OriginX + shiftX,
                    OriginY = v.OriginY + shiftY,
                    Scale = v.Scale,
                    Width = v.Width,
                    Height = v.Height,
                    LayoutRect = new ReservedRect(
                        v.LayoutRect.MinX + shiftX,
                        v.LayoutRect.MinY + shiftY,
                        v.LayoutRect.MaxX + shiftX,
                        v.LayoutRect.MaxY + shiftY),
                    PreferredPlacementSide = v.PreferredPlacementSide,
                    ActualPlacementSide = v.ActualPlacementSide,
                    PlacementFallbackUsed = v.PlacementFallbackUsed
                };
            })
            .ToList();
    }
}
