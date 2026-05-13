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
                Scale = workspace.GetSelectedScale(
                    viewId,
                    view.Attributes.Scale > 0 ? view.Attributes.Scale : 1.0),
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

        AttachFallbackStackOrderGroups(candidate, views);
        return candidate;
    }

    public static DrawingLayoutCandidate FromPlannedLayout(
        string name,
        DrawingLayoutWorkspace workspace,
        IReadOnlyList<View> views,
        IReadOnlyList<ArrangedView> arranged)
    {
        var candidate = DrawingLayoutCandidateFactory.FromPlannedViews(
            name,
            workspace.Source.Drawing,
            workspace.Source.Sheet,
            workspace.Source.ReservedLayout,
            ToPlannedViews(workspace, views, arranged));
        AttachFallbackStackOrderGroups(candidate, views);
        return candidate;
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

    public static List<DrawingLayoutPlannedView> ToPlannedViews(
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
            var layoutRect = ViewPlacementGeometryService.CreateRectFromOrigin(
                workspace,
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
                Scale = workspace.GetSelectedScale(
                    viewId,
                    view.Attributes.Scale > 0 ? view.Attributes.Scale : 1.0),
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

    public static void AttachFallbackStackOrderGroups(
        DrawingLayoutCandidate candidate,
        IReadOnlyList<View> views)
    {
        if (candidate.Views.Count == 0 || views.Count == 0)
            return;

        candidate.StackOrderGroups.Clear();

        var sectionTargetIds = candidate.Views
            .Where(static view => view.PlacementFallbackUsed)
            .Where(static view => string.Equals(view.ViewType, "SectionView", System.StringComparison.OrdinalIgnoreCase))
            .Select(static view => view.Id)
            .ToHashSet();
        if (sectionTargetIds.Count == 0)
            return;

        var sectionTargets = views
            .Where(view => sectionTargetIds.Contains(view.GetIdentifier().ID))
            .ToList();
        var sectionRelations = DetailRelationResolver.BuildSectionMarkRelations(views, sectionTargets);
        if (sectionRelations.Count == 0)
            return;

        var groups = candidate.Views
            .Where(static view => view.PlacementFallbackUsed)
            .Where(static view => view.LayoutRect != null)
            .Where(static view => !string.IsNullOrWhiteSpace(view.PreferredPlacementSide))
            .Where(static view => !string.IsNullOrWhiteSpace(view.ActualPlacementSide))
            .GroupBy(static view => new
            {
                view.PreferredPlacementSide,
                view.ActualPlacementSide,
                view.ViewType
            })
            .Where(static group => group.Count() >= 2);

        foreach (var group in groups)
        {
            if (!System.Enum.TryParse<SectionPlacementSide>(group.Key.ActualPlacementSide, ignoreCase: true, out var actualSide))
                continue;

            var related = group
                .Select(view =>
                {
                    var hasSourceKey = TryGetSectionSourceOrderKey(view.Id, sectionRelations, actualSide, out var sourceKey);
                    return new
                    {
                        View = view,
                        HasSourceKey = hasSourceKey,
                        SourceKey = sourceKey,
                        ActualKey = GetCandidateStackOrderKey(view, actualSide)
                    };
                })
                .Where(static item => item.HasSourceKey)
                .ToList();
            if (related.Count < 2)
                continue;

            candidate.StackOrderGroups.Add(new DrawingLayoutCandidateStackOrderGroup
            {
                PreferredPlacementSide = group.Key.PreferredPlacementSide,
                ActualPlacementSide = group.Key.ActualPlacementSide,
                ViewType = group.Key.ViewType,
                ExpectedViewIds = related
                    .OrderBy(static item => item.SourceKey)
                    .ThenBy(static item => item.View.Id)
                    .Select(static item => item.View.Id)
                    .ToList(),
                ActualViewIds = related
                    .OrderBy(static item => item.ActualKey)
                    .ThenBy(static item => item.View.Id)
                    .Select(static item => item.View.Id)
                    .ToList()
            });
        }
    }

    private static bool TryGetSectionSourceOrderKey(
        int viewId,
        DetailRelationSet sectionRelations,
        SectionPlacementSide actualSide,
        out double orderKey)
    {
        orderKey = 0;
        if (!sectionRelations.TryGet(viewId, out var relation))
            return false;

        if (actualSide is SectionPlacementSide.Left or SectionPlacementSide.Right)
        {
            if (!relation.AnchorY.HasValue)
                return false;

            orderKey = -relation.AnchorY.Value;
            return true;
        }

        if (actualSide is SectionPlacementSide.Top or SectionPlacementSide.Bottom)
        {
            if (!relation.AnchorX.HasValue)
                return false;

            orderKey = relation.AnchorX.Value;
            return true;
        }

        return false;
    }

    private static double GetCandidateStackOrderKey(
        DrawingLayoutCandidateView view,
        SectionPlacementSide actualSide)
    {
        var rect = view.LayoutRect!;
        return actualSide switch
        {
            SectionPlacementSide.Left or SectionPlacementSide.Right => -rect.MaxY,
            SectionPlacementSide.Top or SectionPlacementSide.Bottom => rect.MinX,
            _ => -rect.MaxY
        };
    }
}
