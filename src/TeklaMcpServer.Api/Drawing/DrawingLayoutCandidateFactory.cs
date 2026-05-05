using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing;

internal static class DrawingLayoutCandidateFactory
{
    public static DrawingLayoutCandidate FromPlannedViews(
        string name,
        DrawingInfo drawing,
        DrawingSheetContext sheet,
        DrawingReservedLayoutContext reservedLayout,
        IReadOnlyList<DrawingLayoutPlannedView> plannedViews)
    {
        var candidate = new DrawingLayoutCandidate
        {
            Name = name,
            Drawing = drawing,
            Sheet = sheet,
            ReservedLayout = reservedLayout
        };

        foreach (var view in plannedViews)
        {
            candidate.Views.Add(new DrawingLayoutCandidateView
            {
                Id = view.Id,
                ViewType = view.ViewType,
                SemanticKind = view.SemanticKind,
                Name = view.Name,
                OriginX = view.OriginX,
                OriginY = view.OriginY,
                Scale = view.Scale,
                Width = view.Width,
                Height = view.Height,
                BBoxMinX = view.LayoutRect.MinX,
                BBoxMinY = view.LayoutRect.MinY,
                BBoxMaxX = view.LayoutRect.MaxX,
                BBoxMaxY = view.LayoutRect.MaxY,
                LayoutRect = view.LayoutRect,
                PreferredPlacementSide = view.PreferredPlacementSide,
                ActualPlacementSide = view.ActualPlacementSide,
                PlacementFallbackUsed = view.PlacementFallbackUsed
            });
        }

        return candidate;
    }
}
