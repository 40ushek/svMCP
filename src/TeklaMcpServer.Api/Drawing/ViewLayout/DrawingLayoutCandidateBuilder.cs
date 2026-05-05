using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using TeklaMcpServer.Api.Drawing;

namespace TeklaMcpServer.Api.Drawing.ViewLayout;

internal static class DrawingLayoutCandidateBuilder
{
    public static DrawingLayoutCandidate FromRuntimeLayout(
        string name,
        DrawingLayoutWorkspace workspace,
        IReadOnlyList<View> views,
        IReadOnlyList<ArrangedView> arranged,
        IReadOnlyDictionary<int, ReservedRect> actualRects)
    {
        var arrangedById = arranged.ToDictionary(static view => view.Id);
        var candidate = new DrawingLayoutCandidate
        {
            Name = name,
            Drawing = workspace.Source.Drawing,
            Sheet = workspace.Source.Sheet,
            ReservedLayout = workspace.Source.ReservedLayout
        };

        foreach (var view in views)
        {
            var viewId = view.GetIdentifier().ID;
            var layoutRect = actualRects.TryGetValue(viewId, out var rect) ? rect : null;
            var frame = layoutRect != null
                ? (Width: layoutRect.Width, Height: layoutRect.Height)
                : workspace.GetSelectedFrameSize(viewId, view.Width, view.Height);

            arrangedById.TryGetValue(viewId, out var arrangedView);
            candidate.Views.Add(new DrawingLayoutCandidateView
            {
                Id = viewId,
                ViewType = view.ViewType.ToString(),
                SemanticKind = workspace.GetSemanticKind(viewId).ToString(),
                Name = view.Name ?? string.Empty,
                OriginX = view.Origin?.X ?? 0.0,
                OriginY = view.Origin?.Y ?? 0.0,
                Scale = view.Attributes.Scale > 0 ? view.Attributes.Scale : 1.0,
                Width = frame.Width,
                Height = frame.Height,
                BBoxMinX = layoutRect?.MinX,
                BBoxMinY = layoutRect?.MinY,
                BBoxMaxX = layoutRect?.MaxX,
                BBoxMaxY = layoutRect?.MaxY,
                LayoutRect = layoutRect,
                PreferredPlacementSide = arrangedView?.PreferredPlacementSide ?? string.Empty,
                ActualPlacementSide = arrangedView?.ActualPlacementSide ?? string.Empty,
                PlacementFallbackUsed = arrangedView?.PlacementFallbackUsed ?? false
            });
        }

        return candidate;
    }

    public static DrawingLayoutCandidate FromPlannedLayout(
        string name,
        DrawingLayoutWorkspace workspace,
        IReadOnlyList<View> views,
        IReadOnlyList<ArrangedView> arranged)
    {
        return FromPlannedViews(
            name,
            workspace,
            BuildPlannedViews(workspace, views, arranged));
    }

    public static DrawingLayoutCandidate FromPlannedViews(
        string name,
        DrawingLayoutWorkspace workspace,
        IReadOnlyList<DrawingLayoutPlannedView> plannedViews)
        => DrawingLayoutCandidateFactory.FromPlannedViews(
            name,
            workspace.Source.Drawing,
            workspace.Source.Sheet,
            workspace.Source.ReservedLayout,
            plannedViews);

    private static List<DrawingLayoutPlannedView> BuildPlannedViews(
        DrawingLayoutWorkspace workspace,
        IReadOnlyList<View> views,
        IReadOnlyList<ArrangedView> arranged)
    {
        var arrangedById = arranged.ToDictionary(static view => view.Id);
        var plannedViews = new List<DrawingLayoutPlannedView>(views.Count);

        foreach (var view in views)
        {
            var viewId = view.GetIdentifier().ID;
            arrangedById.TryGetValue(viewId, out var arrangedView);
            var originX = arrangedView?.OriginX ?? view.Origin?.X ?? 0.0;
            var originY = arrangedView?.OriginY ?? view.Origin?.Y ?? 0.0;
            var frame = workspace.GetSelectedFrameSize(viewId, view.Width, view.Height);
            var layoutRect = ViewPlacementGeometryService.CreateCandidateRect(
                view,
                originX,
                originY,
                frame.Width,
                frame.Height);

            plannedViews.Add(new DrawingLayoutPlannedView
            {
                Id = viewId,
                ViewType = view.ViewType.ToString(),
                SemanticKind = workspace.GetSemanticKind(viewId).ToString(),
                Name = view.Name ?? string.Empty,
                OriginX = originX,
                OriginY = originY,
                Scale = view.Attributes.Scale > 0 ? view.Attributes.Scale : 1.0,
                Width = frame.Width,
                Height = frame.Height,
                LayoutRect = layoutRect,
                PreferredPlacementSide = arrangedView?.PreferredPlacementSide ?? string.Empty,
                ActualPlacementSide = arrangedView?.ActualPlacementSide ?? string.Empty,
                PlacementFallbackUsed = arrangedView?.PlacementFallbackUsed ?? false
            });
        }

        return plannedViews;
    }
}
