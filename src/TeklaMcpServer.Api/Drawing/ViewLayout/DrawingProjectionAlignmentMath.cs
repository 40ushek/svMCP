using System;
using System.Collections.Generic;
using System.Linq;
using TeklaMcpServer.Api.Drawing;

namespace TeklaMcpServer.Api.Drawing.ViewLayout;

internal static class DrawingProjectionAlignmentMath
{
    public static bool TryGetSectionAlignmentAxis(SectionPlacementSide placementSide, out bool alignX)
    {
        switch (placementSide)
        {
            case SectionPlacementSide.Top:
            case SectionPlacementSide.Bottom:
                alignX = true;
                return true;

            case SectionPlacementSide.Left:
            case SectionPlacementSide.Right:
                alignX = false;
                return true;

            default:
                alignX = false;
                return false;
        }
    }

    public static bool TryGetNeighborAlignmentAxis(NeighborRole role, out bool alignX)
    {
        switch (role)
        {
            case NeighborRole.Top:
            case NeighborRole.Bottom:
                alignX = true;
                return true;

            case NeighborRole.SideLeft:
            case NeighborRole.SideRight:
                alignX = false;
                return true;

            default:
                alignX = false;
                return false;
        }
    }

    public static (double X, double Y) LocalToSheet(ProjectionViewState view, double localX, double localY)
    {
        var scale = view.Scale > 0 ? view.Scale : 1.0;
        return (view.OriginX + (localX / scale), view.OriginY + (localY / scale));
    }

    public static double LocalCoordinateToSheet(ProjectionViewState view, double localCoordinate, string direction)
    {
        var scale = view.Scale > 0 ? view.Scale : 1.0;
        return string.Equals(direction, "Y", StringComparison.OrdinalIgnoreCase)
            ? view.OriginY + (localCoordinate / scale)
            : view.OriginX + (localCoordinate / scale);
    }

    public static ProjectionRect GetFrameRect(ProjectionViewState view)
    {
        return new ProjectionRect(
            view.FrameCenterX - (view.Width * 0.5),
            view.FrameCenterY - (view.Height * 0.5),
            view.FrameCenterX + (view.Width * 0.5),
            view.FrameCenterY + (view.Height * 0.5));
    }

    public static ProjectionViewState TranslateOrigin(ProjectionViewState view, double dx, double dy)
    {
        return new ProjectionViewState(
            view.ViewId,
            view.OriginX + dx,
            view.OriginY + dy,
            view.Scale,
            view.Width,
            view.Height,
            view.FrameOffsetSheetX,
            view.FrameOffsetSheetY);
    }

    public static bool IsWithinUsableArea(ProjectionRect rect, double margin, double sheetWidth, double sheetHeight)
    {
        return rect.MinX >= margin
            && rect.MaxX <= sheetWidth - margin
            && rect.MinY >= margin
            && rect.MaxY <= sheetHeight - margin;
    }

    public static bool IntersectsAnyReserved(ProjectionRect rect, IReadOnlyList<ReservedRect> reservedAreas)
    {
        foreach (var area in reservedAreas)
        {
            if (Intersects(rect, area))
                return true;
        }

        return false;
    }

    public static bool IntersectsAnyView(ProjectionRect candidate, IReadOnlyList<ProjectionViewState>? otherViews)
    {
        if (otherViews == null) return false;
        foreach (var v in otherViews)
        {
            var rect = GetFrameRect(v);
            if (Intersects(candidate, rect))
                return true;
        }
        return false;
    }

    public static bool TrySelectCommonAxis(
        IReadOnlyList<GridAxisInfo> frontAxes,
        IReadOnlyList<GridAxisInfo> targetAxes,
        string requiredDirection,
        out GridAxisInfo frontAxis,
        out GridAxisInfo targetAxis)
    {
        frontAxis = new GridAxisInfo();
        targetAxis = new GridAxisInfo();

        var orderedFrontAxes = frontAxes
            .Where(a => string.Equals(a.Direction, requiredDirection, StringComparison.OrdinalIgnoreCase))
            .OrderBy(a => a.Coordinate)
            .ThenBy(a => Normalize(a.Guid))
            .ThenBy(a => Normalize(a.Label))
            .ToList();
        if (orderedFrontAxes.Count == 0)
            return false;

        var targetByGuid = targetAxes
            .Where(a => string.Equals(a.Direction, requiredDirection, StringComparison.OrdinalIgnoreCase))
            .Where(a => !string.IsNullOrWhiteSpace(a.Guid))
            .GroupBy(a => Normalize(a.Guid))
            .ToDictionary(g => g.Key, g => g.OrderBy(a => a.Coordinate).First());

        var targetByFallback = targetAxes
            .Where(a => string.Equals(a.Direction, requiredDirection, StringComparison.OrdinalIgnoreCase))
            .GroupBy(BuildFallbackKey)
            .ToDictionary(g => g.Key, g => g.OrderBy(a => a.Coordinate).First());

        foreach (var candidate in orderedFrontAxes)
        {
            var guidKey = Normalize(candidate.Guid);
            if (!string.IsNullOrWhiteSpace(guidKey) && targetByGuid.TryGetValue(guidKey, out var guidMatch))
            {
                frontAxis = candidate;
                targetAxis = guidMatch;
                return true;
            }

            if (targetByFallback.TryGetValue(BuildFallbackKey(candidate), out var fallbackMatch))
            {
                frontAxis = candidate;
                targetAxis = fallbackMatch;
                return true;
            }
        }

        return false;
    }

    private static bool Intersects(ProjectionRect rect, ReservedRect area)
    {
        return !(rect.MaxX <= area.MinX
            || area.MaxX <= rect.MinX
            || rect.MaxY <= area.MinY
            || area.MaxY <= rect.MinY);
    }

    private static bool Intersects(ProjectionRect a, ProjectionRect b)
    {
        return !(a.MaxX <= b.MinX
            || b.MaxX <= a.MinX
            || a.MaxY <= b.MinY
            || b.MaxY <= a.MinY);
    }

    private static string BuildFallbackKey(GridAxisInfo axis)
    {
        return $"{Normalize(axis.Direction)}|{Normalize(axis.Label)}";
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value!.Trim().ToUpperInvariant();
    }
}

