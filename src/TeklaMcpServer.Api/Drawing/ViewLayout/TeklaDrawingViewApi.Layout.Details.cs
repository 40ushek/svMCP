using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Geometry3d;
using TeklaMcpServer.Api.Algorithms.Packing;
using TeklaMcpServer.Api.Diagnostics;
using TeklaMcpServer.Api.Drawing;

namespace TeklaMcpServer.Api.Drawing.ViewLayout;

public sealed partial class TeklaDrawingViewApi
{
    private List<ArrangedView> TryRepositionDetailViews(
        Tekla.Structures.Drawing.Drawing activeDrawing,
        List<View> views,
        List<ArrangedView> arranged,
        double usableMinX,
        double usableMaxX,
        double usableMinY,
        double usableMaxY,
        double gap,
        IReadOnlyList<ReservedRect> reserved,
        IReadOnlyDictionary<int, (double X, double Y)> preMovedFrameOffsets,
        bool applyChanges)
    {
        var topology = ViewTopologyGraph.Build(views);
        var detailViews = topology.SemanticViews.Details.ToList();
        if (detailViews.Count == 0)
            return arranged;

        var relations = topology.DetailRelations;
        if (relations.Count == 0)
            return arranged;

        var viewById = views.ToDictionary(v => v.GetIdentifier().ID);
        var blocked = new List<ReservedRect>(reserved);
        foreach (var view in views.Where(v => topology.SemanticViews.GetKind(v.GetIdentifier().ID) != ViewSemanticKind.Detail))
        {
            if (DrawingViewFrameGeometry.TryGetBoundingRect(view, out var rect))
                blocked.Add(rect);
        }

        var movedAny = false;
        for (var i = 0; i < detailViews.Count; i++)
        {
            var detailView = detailViews[i];
            var detailId = detailView.GetIdentifier().ID;
            if (!relations.TryGet(detailId, out var relation))
            {
                if (DrawingViewFrameGeometry.TryGetBoundingRect(detailView, out var currentRect))
                    blocked.Add(currentRect);
                continue;
            }

            var ownerView = relation.OwnerView;
            if (!viewById.ContainsKey(ownerView.GetIdentifier().ID))
            {
                if (DrawingViewFrameGeometry.TryGetBoundingRect(detailView, out var currentRect))
                    blocked.Add(currentRect);
                continue;
            }

            if (!DrawingViewFrameGeometry.TryGetBoundingRect(ownerView, out var ownerRect)
                || !DrawingViewFrameGeometry.TryGetBoundingRect(detailView, out var detailRect))
            {
                continue;
            }

            var detailWidth = detailRect.MaxX - detailRect.MinX;
            var detailHeight = detailRect.MaxY - detailRect.MinY;
            if (detailWidth <= 0 || detailHeight <= 0)
            {
                blocked.Add(detailRect);
                continue;
            }

            var anchorX = CenterX(ownerRect);
            var anchorY = CenterY(ownerRect);
            if (relation.AnchorX.HasValue)
                anchorX = relation.AnchorX.Value;
            if (relation.AnchorY.HasValue)
                anchorY = relation.AnchorY.Value;

            var decision = BaseProjectedDrawingArrangeStrategy.ProbeDetailPlacement(
                ownerRect,
                detailWidth,
                detailHeight,
                gap * 2.0,
                usableMinX,
                usableMaxX,
                usableMinY,
                usableMaxY,
                blocked,
                anchorX,
                anchorY);
            if (!decision.Success)
            {
                blocked.Add(detailRect);
                continue;
            }

            var candidateRect = decision.Rect;

            var targetCenterX = (candidateRect.MinX + candidateRect.MaxX) * 0.5;
            var targetCenterY = (candidateRect.MinY + candidateRect.MaxY) * 0.5;
            var currentCenterX = (detailRect.MinX + detailRect.MaxX) * 0.5;
            var currentCenterY = (detailRect.MinY + detailRect.MaxY) * 0.5;
            if (System.Math.Abs(currentCenterX - targetCenterX) < 0.5
                && System.Math.Abs(currentCenterY - targetCenterY) < 0.5)
            {
                blocked.Add(detailRect);
                continue;
            }

            var currentOrigin = detailView.Origin;
            if (currentOrigin == null)
            {
                blocked.Add(detailRect);
                continue;
            }
            var origin = new Point(currentOrigin.X, currentOrigin.Y, currentOrigin.Z);

            // Use the frame offset captured BEFORE any moves in this fit cycle.
            // Re-reading the bbox here would return a stale value (center == origin, offset = 0)
            // because Tekla doesn't update the bbox immediately after Modify/CommitChanges.
            // offsetById stores (center - origin) * scale, so sheet-space offset = stored / scale.
            var detailScale = detailView.Attributes.Scale > 0 ? detailView.Attributes.Scale : 1.0;
            if (preMovedFrameOffsets.TryGetValue(detailId, out var preOffset))
            {
                origin.X = targetCenterX - preOffset.X / detailScale;
                origin.Y = targetCenterY - preOffset.Y / detailScale;
            }
            else if (DrawingViewFrameGeometry.TryGetCenterOffsetFromOrigin(detailView, out var offsetX, out var offsetY))
            {
                origin.X = targetCenterX - offsetX;
                origin.Y = targetCenterY - offsetY;
            }
            else
            {
                origin.X = targetCenterX;
                origin.Y = targetCenterY;
            }

            if (applyChanges)
            {
                detailView.Origin = origin;
                if (!detailView.Modify())
                {
                    blocked.Add(detailRect);
                    continue;
                }
            }

            movedAny = true;
            blocked.Add(candidateRect);
            for (var ai = 0; ai < arranged.Count; ai++)
            {
                if (arranged[ai].Id != detailId)
                    continue;

                arranged[ai] = new ArrangedView
                {
                    Id = arranged[ai].Id,
                    ViewType = arranged[ai].ViewType,
                    OriginX = origin.X,
                    OriginY = origin.Y,
                    PreferredPlacementSide = arranged[ai].PreferredPlacementSide,
                    ActualPlacementSide = arranged[ai].ActualPlacementSide,
                    PlacementFallbackUsed = arranged[ai].PlacementFallbackUsed,
                    LayoutMargin = arranged[ai].LayoutMargin,
                    LayoutGap = arranged[ai].LayoutGap
                };
                break;
            }
        }

        if (movedAny && applyChanges)
            activeDrawing.CommitChanges();

        return arranged;
    }

    private List<ArrangedView> TryRepositionFreeViews(
        Tekla.Structures.Drawing.Drawing activeDrawing,
        DrawingLayoutWorkspace workspace,
        List<View> views,
        List<ArrangedView> arranged,
        double usableMinX,
        double usableMaxX,
        double usableMinY,
        double usableMaxY,
        double gap,
        IReadOnlyList<ReservedRect> reserved,
        bool applyChanges)
    {
        var arrangedById = arranged.ToDictionary(static view => view.Id);
        var freeViews = new List<(View View, ReservedRect Rect, double Area, bool AnchorDriven)>();
        foreach (var view in views)
        {
            var id = view.GetIdentifier().ID;
            var anchorDriven = IsAnchorDrivenFreeSection(workspace, id);
            if (!IsFreePlacementKind(workspace.GetSemanticKind(id)) && !anchorDriven)
                continue;

            if (!TryGetCurrentLayoutRect(workspace, arrangedById, view, out var rect))
                continue;

            freeViews.Add((view, rect, GetArea(rect), anchorDriven));
        }

        freeViews = freeViews
            .OrderBy(item => item.AnchorDriven)
            .ThenByDescending(item => item.Area)
            .ToList();
        if (freeViews.Count == 0)
            return arranged;

        var blockersById = new Dictionary<int, ReservedRect>();
        foreach (var view in views)
        {
            var id = view.GetIdentifier().ID;
            if (IsFreePlacementKind(workspace.GetSemanticKind(id)) || IsAnchorDrivenFreeSection(workspace, id))
                continue;

            if (TryGetCurrentLayoutRect(workspace, arrangedById, view, out var rect))
                blockersById[id] = rect;
        }

        var movedAny = false;
        foreach (var item in freeViews)
        {
            var view = item.View;
            var id = view.GetIdentifier().ID;
            var currentRect = item.Rect;

            var width = currentRect.MaxX - currentRect.MinX;
            var height = currentRect.MaxY - currentRect.MinY;
            if (width <= 0 || height <= 0)
                continue;

            if (item.AnchorDriven)
            {
                DrawingProjectionAlignmentService.Log(
                    $"FREE_VIEW_REPOSITION frame id={id} rect=[{currentRect.MinX:F1},{currentRect.MinY:F1},{currentRect.MaxX:F1},{currentRect.MaxY:F1}] size=({width:F1},{height:F1})");
            }

            var blocked = BuildFreeViewBlockedRectangles(
                usableMinX,
                usableMaxX,
                usableMinY,
                usableMaxY,
                gap,
                reserved,
                blockersById.Values);
            var packer = new MaxRectsBinPacker(
                usableMaxX - usableMinX,
                usableMaxY - usableMinY,
                allowRotation: false,
                blocked);

            var targetX = (usableMinX + usableMaxX) * 0.5;
            var targetY = (usableMinY + usableMaxY) * 0.5;
            var isAnchorDriven = false;
            if (item.AnchorDriven
                && TryGetAdjustedParentAnchor(
                    workspace,
                    arrangedById,
                    blockersById,
                    id,
                    out var anchorX,
                    out var anchorY))
            {
                targetX = anchorX;
                targetY = anchorY;
                isAnchorDriven = true;
            }
            ReservedRect candidateRect;
            var packerTargetX = targetX - usableMinX;
            var packerTargetY = usableMaxY - targetY;
            var placed = isAnchorDriven
                ? packer.TryInsertClosestToAnchor(width, height, packerTargetX, packerTargetY, out var placement)
                : packer.TryInsertClosestToPoint(width, height, packerTargetX, packerTargetY, out placement);
            if (placed)
            {
                candidateRect = new ReservedRect(
                    usableMinX + placement.X,
                    usableMaxY - placement.Y - height,
                    usableMinX + placement.X + width,
                    usableMaxY - placement.Y);
                var validation = ViewPlacementValidator.Validate(
                    candidateRect,
                    usableMinX,
                    usableMaxX,
                    usableMinY,
                    usableMaxY,
                    reserved,
                    blockersById);
                if (!validation.Fits)
                {
                    blockersById[id] = currentRect;
                    DrawingProjectionAlignmentService.Log($"FREE_VIEW_REPOSITION result=reject reason={validation.Reason} kind={workspace.GetSemanticKind(id)} anchorDriven={(isAnchorDriven ? 1 : 0)}");
                    continue;
                }

                if (isAnchorDriven)
                {
                    var cx = (candidateRect.MinX + candidateRect.MaxX) * 0.5;
                    var cy = (candidateRect.MinY + candidateRect.MaxY) * 0.5;
                    var dist = System.Math.Sqrt((cx - targetX) * (cx - targetX) + (cy - targetY) * (cy - targetY));
                    DrawingProjectionAlignmentService.Log($"FREE_VIEW_REPOSITION anchor-placed id={id} boundaryTarget=({targetX:F1},{targetY:F1}) candidate=({cx:F1},{cy:F1}) dist={dist:F1}");
                }
            }
            else
            {
                // Anchor-driven sections must not use best-effort overlap fallback.
                // If no non-overlapping placement exists, leave in place.
                if (isAnchorDriven)
                {
                    blockersById[id] = currentRect;
                    DrawingProjectionAlignmentService.Log($"FREE_VIEW_REPOSITION result=skip-anchor-no-space id={id} kind={workspace.GetSemanticKind(id)} anchor=({targetX:F1},{targetY:F1})");
                    continue;
                }

                if (!TryFindBestEffortPosition(
                        width, height,
                        usableMinX, usableMaxX, usableMinY, usableMaxY,
                        reserved, blockersById, currentRect,
                        out candidateRect, out var bestOverlap, out var currentOverlap))
                {
                    blockersById[id] = currentRect;
                    DrawingProjectionAlignmentService.Log($"FREE_VIEW_REPOSITION result=reject reason=no-space kind={workspace.GetSemanticKind(id)} currentOverlap={currentOverlap:F1} bestOverlap={bestOverlap:F1}");
                    continue;
                }

                DrawingProjectionAlignmentService.Log($"FREE_VIEW_REPOSITION best-effort kind={workspace.GetSemanticKind(id)} candidate=[{candidateRect.MinX:F1},{candidateRect.MinY:F1},{candidateRect.MaxX:F1},{candidateRect.MaxY:F1}] bestOverlap={bestOverlap:F1} currentOverlap={currentOverlap:F1}");
            }

            var runtimeOrigin = view.Origin;
            if (runtimeOrigin == null)
            {
                blockersById[id] = currentRect;
                continue;
            }

            var dx = CenterX(candidateRect) - CenterX(currentRect);
            var dy = CenterY(candidateRect) - CenterY(currentRect);
            if (System.Math.Abs(dx) < 0.5 && System.Math.Abs(dy) < 0.5)
            {
                blockersById[id] = currentRect;
                continue;
            }

            var sourceOriginX = arrangedById.TryGetValue(id, out var currentArranged)
                ? currentArranged.OriginX
                : runtimeOrigin.X;
            var sourceOriginY = currentArranged?.OriginY ?? runtimeOrigin.Y;
            var origin = new Point(sourceOriginX + dx, sourceOriginY + dy, runtimeOrigin.Z);
            if (applyChanges)
            {
                view.Origin = origin;
                if (!view.Modify())
                {
                    blockersById[id] = currentRect;
                    DrawingProjectionAlignmentService.Log($"FREE_VIEW_REPOSITION result=reject reason=modify-failed kind={workspace.GetSemanticKind(id)}");
                    continue;
                }
            }

            movedAny = true;
            blockersById[id] = candidateRect;
            if (UpdateArrangedOrigin(arranged, id, origin.X, origin.Y) is { } updatedView)
                arrangedById[id] = updatedView;
            DrawingProjectionAlignmentService.Log(
                $"FREE_VIEW_REPOSITION result=ok kind={workspace.GetSemanticKind(id)} anchorDriven={(isAnchorDriven ? 1 : 0)} dx={dx:F1} dy={dy:F1}");
        }

        if (movedAny && applyChanges)
            activeDrawing.CommitChanges();

        return arranged;
    }

    private static bool TryFindBestEffortPosition(
        double width,
        double height,
        double usableMinX,
        double usableMaxX,
        double usableMinY,
        double usableMaxY,
        IReadOnlyList<ReservedRect> reserved,
        IReadOnlyDictionary<int, ReservedRect> blockersById,
        ReservedRect currentRect,
        out ReservedRect best,
        out double bestOverlap,
        out double currentOverlap)
    {
        var usableW = usableMaxX - usableMinX;
        var usableH = usableMaxY - usableMinY;
        var stepX = System.Math.Max(10.0, usableW / 12.0);
        var stepY = System.Math.Max(10.0, usableH / 12.0);

        var allBlockers = reserved.Concat(blockersById.Values).ToList();
        currentOverlap = allBlockers.Sum(b => IntersectionArea(currentRect, b));

        bestOverlap = double.MaxValue;
        best = currentRect;
        var found = false;

        for (var x = usableMinX; x + width <= usableMaxX + 0.1; x += stepX)
        {
            for (var y = usableMinY; y + height <= usableMaxY + 0.1; y += stepY)
            {
                var cx = System.Math.Min(x, usableMaxX - width);
                var cy = System.Math.Min(y, usableMaxY - height);
                var candidate = new ReservedRect(cx, cy, cx + width, cy + height);

                var overlap = 0.0;
                foreach (var blocker in allBlockers)
                    overlap += IntersectionArea(candidate, blocker);

                if (overlap < bestOverlap)
                {
                    bestOverlap = overlap;
                    best = candidate;
                    found = true;
                }

                if (bestOverlap == 0.0)
                    break;
            }

            if (bestOverlap == 0.0)
                break;
        }

        if (!found)
            return false;

        return bestOverlap < currentOverlap;
    }

    private static double IntersectionArea(ReservedRect a, ReservedRect b)
    {
        var ox = System.Math.Min(a.MaxX, b.MaxX) - System.Math.Max(a.MinX, b.MinX);
        var oy = System.Math.Min(a.MaxY, b.MaxY) - System.Math.Max(a.MinY, b.MinY);
        return ox > 0 && oy > 0 ? ox * oy : 0.0;
    }

    private static bool IsFreePlacementKind(ViewSemanticKind kind)
        => kind == ViewSemanticKind.Other || kind == ViewSemanticKind.Model3D;

    private static bool IsAnchorDrivenFreeSection(
        DrawingLayoutWorkspace workspace,
        int viewId)
        => workspace.GetLayoutViewKind(viewId) == LayoutViewKind.AnchorDetailSection;

    private static bool TryGetCurrentLayoutRect(
        DrawingLayoutWorkspace workspace,
        IReadOnlyDictionary<int, ArrangedView> arrangedById,
        View view,
        out ReservedRect rect)
    {
        var id = view.GetIdentifier().ID;
        if (arrangedById.TryGetValue(id, out var arranged))
        {
            var size = workspace.GetSelectedFrameSize(id, view.Width, view.Height);
            if (size.Width > 0 && size.Height > 0)
            {
                rect = ViewPlacementGeometryService.CreateRectFromOrigin(
                    workspace,
                    view,
                    arranged.OriginX,
                    arranged.OriginY,
                    size.Width,
                    size.Height);
                return true;
            }
        }

        return DrawingViewFrameGeometry.TryGetBoundingRect(
            view,
            workspace.ActualViewRectsById,
            out rect);
    }

    private static bool TryGetAdjustedParentAnchor(
        DrawingLayoutWorkspace workspace,
        IReadOnlyDictionary<int, ArrangedView> arrangedById,
        IReadOnlyDictionary<int, ReservedRect> blockersById,
        int viewId,
        out double anchorX,
        out double anchorY)
    {
        anchorX = 0;
        anchorY = 0;

        var view = workspace.TryGetView(viewId);
        if (view?.ParentAnchorX is not { } storedAnchorX
            || view.ParentAnchorY is not { } storedAnchorY)
        {
            return false;
        }

        anchorX = storedAnchorX;
        anchorY = storedAnchorY;
        if (view.ParentViewId is not { } parentId
            || workspace.TryGetView(parentId) is not { } originalParent
            || !arrangedById.TryGetValue(parentId, out var arrangedParent))
        {
            return true;
        }

        var deltaX = arrangedParent.OriginX - originalParent.OriginX;
        var deltaY = arrangedParent.OriginY - originalParent.OriginY;
        anchorX += deltaX;
        anchorY += deltaY;

        var adjustedAnchorX = anchorX;
        var adjustedAnchorY = anchorY;
        var boundarySide = "none";
        if (blockersById.TryGetValue(parentId, out var parentRect))
        {
            ProjectAnchorToNearestParentBoundary(
                parentRect,
                adjustedAnchorX,
                adjustedAnchorY,
                out anchorX,
                out anchorY,
                out boundarySide);
        }

        DrawingProjectionAlignmentService.Log(
            $"FREE_VIEW_REPOSITION anchor-adjust id={viewId} parent={parentId} raw=({storedAnchorX:F1},{storedAnchorY:F1}) delta=({deltaX:F1},{deltaY:F1}) adjusted=({adjustedAnchorX:F1},{adjustedAnchorY:F1}) boundary={boundarySide} target=({anchorX:F1},{anchorY:F1})");
        return true;
    }

    private static void ProjectAnchorToNearestParentBoundary(
        ReservedRect parentRect,
        double anchorX,
        double anchorY,
        out double targetX,
        out double targetY,
        out string boundarySide)
    {
        targetX = System.Math.Max(parentRect.MinX, System.Math.Min(anchorX, parentRect.MaxX));
        targetY = System.Math.Max(parentRect.MinY, System.Math.Min(anchorY, parentRect.MaxY));

        var insideX = anchorX >= parentRect.MinX && anchorX <= parentRect.MaxX;
        var insideY = anchorY >= parentRect.MinY && anchorY <= parentRect.MaxY;
        if (!insideX || !insideY)
        {
            boundarySide = "nearest";
            return;
        }

        var left = anchorX - parentRect.MinX;
        var right = parentRect.MaxX - anchorX;
        var bottom = anchorY - parentRect.MinY;
        var top = parentRect.MaxY - anchorY;
        var nearest = System.Math.Min(System.Math.Min(left, right), System.Math.Min(bottom, top));

        if (nearest == left)
        {
            targetX = parentRect.MinX;
            boundarySide = "left";
        }
        else if (nearest == right)
        {
            targetX = parentRect.MaxX;
            boundarySide = "right";
        }
        else if (nearest == bottom)
        {
            targetY = parentRect.MinY;
            boundarySide = "bottom";
        }
        else
        {
            targetY = parentRect.MaxY;
            boundarySide = "top";
        }
    }

    private static double GetArea(ReservedRect rect)
        => System.Math.Max(0, rect.MaxX - rect.MinX) * System.Math.Max(0, rect.MaxY - rect.MinY);

    private static List<PackedRectangle> BuildFreeViewBlockedRectangles(
        double usableMinX,
        double usableMaxX,
        double usableMinY,
        double usableMaxY,
        double gap,
        IReadOnlyList<ReservedRect> reserved,
        IEnumerable<ReservedRect> viewRects)
    {
        var result = new List<PackedRectangle>();
        foreach (var rect in reserved.Concat(viewRects))
        {
            var minX = System.Math.Max(usableMinX, rect.MinX - gap);
            var minY = System.Math.Max(usableMinY, rect.MinY - gap);
            var maxX = System.Math.Min(usableMaxX, rect.MaxX + gap);
            var maxY = System.Math.Min(usableMaxY, rect.MaxY + gap);
            if (maxX <= minX || maxY <= minY)
                continue;

            var packed = new PackedRectangle(
                minX - usableMinX,
                usableMaxY - maxY,
                maxX - minX,
                maxY - minY);
            if (packed.Width <= 0 || packed.Height <= 0)
                continue;

            result.Add(packed);
        }

        return result;
    }

    private static ArrangedView? UpdateArrangedOrigin(
        List<ArrangedView> arranged,
        int id,
        double originX,
        double originY)
    {
        for (var i = 0; i < arranged.Count; i++)
        {
            if (arranged[i].Id != id)
                continue;

            var updated = new ArrangedView
            {
                Id = arranged[i].Id,
                ViewType = arranged[i].ViewType,
                OriginX = originX,
                OriginY = originY,
                PreferredPlacementSide = arranged[i].PreferredPlacementSide,
                ActualPlacementSide = arranged[i].ActualPlacementSide,
                PlacementFallbackUsed = arranged[i].PlacementFallbackUsed,
                LayoutMargin = arranged[i].LayoutMargin,
                LayoutGap = arranged[i].LayoutGap
            };
            arranged[i] = updated;
            return updated;
        }

        return null;
    }

    private static bool TryResolveDetailAnchorSheet(View ownerView, DetailMarkInfo detailMark, out double anchorX, out double anchorY)
    {
        if (TryResolveDetailAnchorSheet(ownerView, detailMark.LabelPoint, out anchorX, out anchorY))
            return true;
        if (TryResolveDetailAnchorSheet(ownerView, detailMark.BoundaryPoint, out anchorX, out anchorY))
            return true;
        return TryResolveDetailAnchorSheet(ownerView, detailMark.CenterPoint, out anchorX, out anchorY);
    }

    private static bool TryResolveDetailAnchorSheet(View ownerView, Tekla.Structures.Drawing.DetailMark detailMark, out double anchorX, out double anchorY)
    {
        if (detailMark.LabelPoint != null && TryResolveDetailAnchorSheet(ownerView, new[] { detailMark.LabelPoint.X, detailMark.LabelPoint.Y }, out anchorX, out anchorY))
            return true;
        if (detailMark.BoundaryPoint != null && TryResolveDetailAnchorSheet(ownerView, new[] { detailMark.BoundaryPoint.X, detailMark.BoundaryPoint.Y }, out anchorX, out anchorY))
            return true;
        if (detailMark.CenterPoint != null && TryResolveDetailAnchorSheet(ownerView, new[] { detailMark.CenterPoint.X, detailMark.CenterPoint.Y }, out anchorX, out anchorY))
            return true;
        anchorX = 0;
        anchorY = 0;
        return false;
    }

    private static bool TryResolveDetailAnchorSheet(View ownerView, double[] point, out double anchorX, out double anchorY)
    {
        anchorX = 0;
        anchorY = 0;
        if (point == null || point.Length < 2)
            return false;

        return BaseProjectedDrawingArrangeStrategy.TryProjectViewLocalPointToSheet(
            ownerView,
            new Point(point[0], point[1], 0),
            out anchorX,
            out anchorY);
    }

    private static Point? TrySectionMarkMidPoint(Tekla.Structures.Drawing.SectionMark sectionMark)
    {
        try
        {
            var lp = sectionMark.LeftPoint;
            var rp = sectionMark.RightPoint;
            if (lp != null && rp != null)
                return new Point((lp.X + rp.X) * 0.5, (lp.Y + rp.Y) * 0.5, 0);
            return lp ?? rp;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// After packing, the view group may be biased toward one side because reserved areas
    /// only block a corner. Shift the whole group toward the center of the usable area
    /// on X and Y independently, without overlapping reserved areas.
    /// </summary>
    private static List<ArrangedView> TryCenterViewGroup(
        Tekla.Structures.Drawing.Drawing activeDrawing,
        List<View> views,
        List<ArrangedView> arranged,
        double usableMinX, double usableMaxX,
        double usableMinY, double usableMaxY,
        IReadOnlyList<ReservedRect> reserved,
        bool applyChanges)
    {
        if (views.Count == 0)
            return arranged;

        var rects = GetViewRects(views);
        if (rects.Count != views.Count)
            return arranged;

        var dx = 0.0;
        if (ViewGroupCenteringGeometry.TryFindCenteringDelta(rects, usableMinX, usableMaxX, reserved, horizontal: true, out var foundDx))
        {
            dx = foundDx;
            rects = ViewGroupCenteringGeometry.ShiftRects(rects, dx, 0);
        }

        var dy = 0.0;
        if (ViewGroupCenteringGeometry.TryFindCenteringDelta(rects, usableMinY, usableMaxY, reserved, horizontal: false, out var foundDy))
            dy = foundDy;

        if (System.Math.Abs(dx) < 1.0 && System.Math.Abs(dy) < 1.0)
            return arranged;

        foreach (var v in views)
        {
            var currentOrigin = v.Origin;
            if (currentOrigin == null)
                continue;

            var o = new Point(currentOrigin.X, currentOrigin.Y, currentOrigin.Z);
            o.X += dx;
            o.Y += dy;
            if (applyChanges)
            {
                v.Origin = o;
                v.Modify();
            }
        }

        if (applyChanges)
            activeDrawing.CommitChanges();

        PerfTrace.Write("api-view", applyChanges ? "center_group" : "center_group_plan", 0,
            $"applied={(applyChanges ? 1 : 0)} dx={dx:F1} dy={dy:F1} usableX={usableMinX:F1}-{usableMaxX:F1} usableY={usableMinY:F1}-{usableMaxY:F1}");

        return arranged.Select(a => new ArrangedView
        {
            Id       = a.Id,
            ViewType = a.ViewType,
            OriginX  = a.OriginX + dx,
            OriginY  = a.OriginY + dy,
            PreferredPlacementSide = a.PreferredPlacementSide,
            ActualPlacementSide = a.ActualPlacementSide,
            PlacementFallbackUsed = a.PlacementFallbackUsed,
            LayoutMargin = a.LayoutMargin,
            LayoutGap = a.LayoutGap
        }).ToList();
    }

    private static double CenterX(ReservedRect rect) => (rect.MinX + rect.MaxX) / 2.0;

    private static double CenterY(ReservedRect rect) => (rect.MinY + rect.MaxY) / 2.0;
}
