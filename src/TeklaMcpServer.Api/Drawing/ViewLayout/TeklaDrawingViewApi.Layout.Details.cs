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
    private List<ArrangedView> TryRepositionFreeViews(
        DrawingLayoutWorkspace workspace,
        List<View> views,
        List<ArrangedView> arranged,
        double usableMinX,
        double usableMaxX,
        double usableMinY,
        double usableMaxY,
        double gap,
        IReadOnlyList<ReservedRect> reserved)
    {
        var plan = BuildFreeViewRepositionPlan(
            workspace,
            views,
            arranged,
            usableMinX,
            usableMaxX,
            usableMinY,
            usableMaxY,
            gap,
            reserved);
        ApplyFreeViewRepositionPlan(arranged, views, plan, usableMinX, gap);

        return arranged;
    }

    internal static void ApplyFreeViewRepositionPlan(
        List<ArrangedView> arranged,
        IReadOnlyList<View> views,
        FreeViewRepositionPlan plan,
        double layoutMargin,
        double layoutGap)
    {
        var viewsById = views.ToDictionary(static view => view.GetIdentifier().ID);
        var arrangedById = arranged.ToDictionary(static view => view.Id);
        foreach (var decision in plan.Decisions)
        {
            var ok = string.Equals(decision.Reason, "ok", System.StringComparison.Ordinal)
                     && decision.PlannedOriginX.HasValue
                     && decision.PlannedOriginY.HasValue;

            if (ok)
            {
                if (UpdateArrangedOrigin(
                        arranged,
                        decision.ViewId,
                        decision.PlannedOriginX.Value,
                        decision.PlannedOriginY.Value) is { } updated)
                {
                    arrangedById[decision.ViewId] = updated;
                }
                else if (viewsById.TryGetValue(decision.ViewId, out var view))
                {
                    var added = new ArrangedView
                    {
                        Id = decision.ViewId,
                        ViewType = view.ViewType.ToString(),
                        OriginX = decision.PlannedOriginX.Value,
                        OriginY = decision.PlannedOriginY.Value,
                        LayoutMargin = layoutMargin,
                        LayoutGap = layoutGap
                    };
                    arranged.Add(added);
                    arrangedById[decision.ViewId] = added;
                }
            }
            else if (decision.Reason.StartsWith("reject", System.StringComparison.Ordinal)
                     && !arrangedById.ContainsKey(decision.ViewId)
                     && viewsById.TryGetValue(decision.ViewId, out var fallbackView)
                     && fallbackView.Origin != null)
            {
                // Free-view reposition found no valid position — add the view at its
                // current drawing origin so the scorer sees the real overlap and can
                // penalise this candidate vs. alternatives that place it properly.
                var fallback = new ArrangedView
                {
                    Id = decision.ViewId,
                    ViewType = fallbackView.ViewType.ToString(),
                    OriginX = fallbackView.Origin.X,
                    OriginY = fallbackView.Origin.Y,
                    LayoutMargin = layoutMargin,
                    LayoutGap = layoutGap
                };
                arranged.Add(fallback);
                arrangedById[decision.ViewId] = fallback;

                DrawingProjectionAlignmentService.Log(
                    $"FREE_VIEW_PLAN_FALLBACK id={decision.ViewId} kind={decision.ViewKind} reason={decision.Reason} origin=({fallbackView.Origin.X:F1},{fallbackView.Origin.Y:F1})");
            }
        }
    }

    private FreeViewRepositionPlan BuildFreeViewRepositionPlan(
        DrawingLayoutWorkspace workspace,
        List<View> views,
        List<ArrangedView> arranged,
        double usableMinX,
        double usableMaxX,
        double usableMinY,
        double usableMaxY,
        double gap,
        IReadOnlyList<ReservedRect> reserved)
    {
        var arrangedById = arranged.ToDictionary(static view => view.Id);
        var freeViews = new List<(View View, ReservedRect Rect, double Area, bool AnchorDriven)>();
        var decisions = new List<FreeViewRepositionDecision>();
        foreach (var view in views)
        {
            var id = view.GetIdentifier().ID;
            var anchorDriven = IsAnchorDrivenFreeSection(workspace, id);
            if (!IsFreePlacementKind(workspace.GetSemanticKind(id)) && !anchorDriven)
                continue;

            // Virtual: use workspace only, no live Tekla fallback
            if (!TryGetVirtualLayoutRect(workspace, arrangedById, view, out var rect))
            {
                var kind = workspace.GetSemanticKind(id).ToString();
                decisions.Add(new FreeViewRepositionDecision
                {
                    ViewId = id, ViewKind = kind, AnchorDriven = anchorDriven,
                    PlannedOriginX = TryGetPlannedOriginX(arrangedById, id),
                    PlannedOriginY = TryGetPlannedOriginY(arrangedById, id),
                    PlannedRect = default, Reason = "skip reason=no-size-or-frame"
                });
                continue;
            }

            freeViews.Add((view, rect, GetArea(rect), anchorDriven));
        }

        freeViews = freeViews
            .OrderBy(item => item.AnchorDriven)
            .ThenByDescending(item => item.Area)
            .ToList();
        if (freeViews.Count == 0)
            return new FreeViewRepositionPlan { Decisions = decisions };

        var blockersById = new Dictionary<int, ReservedRect>();
        foreach (var view in views)
        {
            var id = view.GetIdentifier().ID;
            if (IsFreePlacementKind(workspace.GetSemanticKind(id)) || IsAnchorDrivenFreeSection(workspace, id))
                continue;

            if (TryGetVirtualLayoutRect(workspace, arrangedById, view, out var rect))
                blockersById[id] = rect;
        }

        foreach (var item in freeViews)
        {
            var view = item.View;
            var id = view.GetIdentifier().ID;
            var currentRect = item.Rect;
            var kind = workspace.GetSemanticKind(id).ToString();

            var width = currentRect.MaxX - currentRect.MinX;
            var height = currentRect.MaxY - currentRect.MinY;
            if (width <= 0 || height <= 0)
            {
                decisions.Add(new FreeViewRepositionDecision
                {
                    ViewId = id, ViewKind = kind, AnchorDriven = item.AnchorDriven,
                    PlannedOriginX = TryGetPlannedOriginX(arrangedById, id),
                    PlannedOriginY = TryGetPlannedOriginY(arrangedById, id),
                    PlannedRect = currentRect, Reason = "skip reason=no-size-or-frame"
                });
                continue;
            }

            // Phase 7.2: placement via ViewPlacementService (single flip/gap owner).
            // gap-model here is "blockers expanded by gap, item raw" — exactly the
            // service contract, so this is behavior-preserving.
            var placementFrame = new PlacementFrame(usableMinX, usableMinY, usableMaxX, usableMaxY);
            var placementBlocked = new List<ReservedRect>(reserved);
            placementBlocked.AddRange(blockersById.Values);

            var targetX = (usableMinX + usableMaxX) * 0.5;
            var targetY = (usableMinY + usableMaxY) * 0.5;
            var isAnchorDriven = false;
            if (item.AnchorDriven
                && TryGetAdjustedParentAnchor(workspace, arrangedById, blockersById, id, out var anchorX, out var anchorY))
            {
                targetX = anchorX;
                targetY = anchorY;
                isAnchorDriven = true;
            }

            var placed = isAnchorDriven
                ? _viewPlacementService.TryPlaceNearAnchor(
                    placementFrame, width, height, targetX, targetY, placementBlocked, gap, out var candidateRect)
                : _viewPlacementService.TryPlaceNearPoint(
                    placementFrame, width, height, targetX, targetY, placementBlocked, gap, out candidateRect);

            if (placed)
            {
                var validation = ViewPlacementValidator.Validate(
                    candidateRect, usableMinX, usableMaxX, usableMinY, usableMaxY, reserved, blockersById);
                if (!validation.Fits)
                {
                    DrawingProjectionAlignmentService.Log(
                        $"FREE_VIEW_REJECT id={id} kind={kind} candidate=[{candidateRect.MinX:F1},{candidateRect.MinY:F1},{candidateRect.MaxX:F1},{candidateRect.MaxY:F1}] reason={validation.Reason} blockers={FormatFreeViewBlockers(blockersById)}");
                    blockersById[id] = currentRect;
                    decisions.Add(new FreeViewRepositionDecision
                    {
                        ViewId = id, ViewKind = kind, AnchorDriven = isAnchorDriven,
                        PlannedOriginX = TryGetPlannedOriginX(arrangedById, id),
                        PlannedOriginY = TryGetPlannedOriginY(arrangedById, id),
                        PlannedRect = currentRect, Reason = $"reject reason={validation.Reason}"
                    });
                    continue;
                }
            }
            else
            {
                blockersById[id] = currentRect;
                decisions.Add(new FreeViewRepositionDecision
                {
                    ViewId = id, ViewKind = kind, AnchorDriven = isAnchorDriven,
                    PlannedOriginX = TryGetPlannedOriginX(arrangedById, id),
                    PlannedOriginY = TryGetPlannedOriginY(arrangedById, id),
                    PlannedRect = currentRect, Reason = "reject reason=no-space"
                });
                continue;
            }

            if (!arrangedById.ContainsKey(id)
                && !workspace.ActualViewRectsById.ContainsKey(id))
            {
                blockersById[id] = currentRect;
                decisions.Add(new FreeViewRepositionDecision
                {
                    ViewId = id, ViewKind = kind, AnchorDriven = isAnchorDriven,
                    PlannedRect = currentRect, Reason = "skip reason=no-arranged"
                });
                continue;
            }
            var (frameOffsetX, frameOffsetY) = ViewPlacementGeometryService.GetFrameOffsetSheet(workspace, view);
            var plannedOrigin = ViewPlacementGeometryService.ResolveOriginFromFrameCenter(
                CenterX(candidateRect),
                CenterY(candidateRect),
                frameOffsetX,
                frameOffsetY);

            blockersById[id] = candidateRect;
            if (arrangedById.TryGetValue(id, out var existing))
            {
                arrangedById[id] = new ArrangedView
                {
                    Id = existing.Id,
                    ViewType = existing.ViewType,
                    OriginX = plannedOrigin.X,
                    OriginY = plannedOrigin.Y,
                    PreferredPlacementSide = existing.PreferredPlacementSide,
                    ActualPlacementSide = existing.ActualPlacementSide,
                    PlacementFallbackUsed = existing.PlacementFallbackUsed,
                    LayoutMargin = existing.LayoutMargin,
                    LayoutGap = existing.LayoutGap,
                    IsSnapshotFallback = false
                };
            }
            else
            {
                arrangedById[id] = new ArrangedView
                {
                    Id = id,
                    ViewType = view.ViewType.ToString(),
                    OriginX = plannedOrigin.X,
                    OriginY = plannedOrigin.Y,
                    LayoutMargin = usableMinX,
                    LayoutGap = gap
                };
            }
            decisions.Add(new FreeViewRepositionDecision
            {
                ViewId = id, ViewKind = kind, AnchorDriven = isAnchorDriven,
                PlannedOriginX = plannedOrigin.X,
                PlannedOriginY = plannedOrigin.Y,
                PlannedRect = candidateRect,
                Reason = "ok"
            });
        }

        return new FreeViewRepositionPlan { Decisions = decisions };
    }

    private static string FormatFreeViewBlockers(IReadOnlyDictionary<int, ReservedRect> blockersById)
    {
        if (blockersById.Count == 0)
            return "none";

        return string.Join(
            ";",
            blockersById
                .OrderBy(static item => item.Key)
                .Select(static item => $"{item.Key}=[{item.Value.MinX:F1},{item.Value.MinY:F1},{item.Value.MaxX:F1},{item.Value.MaxY:F1}]"));
    }

    private static double? TryGetPlannedOriginX(
        IReadOnlyDictionary<int, ArrangedView> arrangedById,
        int id)
    {
        return arrangedById.TryGetValue(id, out var arranged) ? arranged.OriginX : null;
    }

    private static double? TryGetPlannedOriginY(
        IReadOnlyDictionary<int, ArrangedView> arrangedById,
        int id)
    {
        return arrangedById.TryGetValue(id, out var arranged) ? arranged.OriginY : null;
    }

    private static bool TryGetVirtualLayoutRect(
        DrawingLayoutWorkspace workspace,
        IReadOnlyDictionary<int, ArrangedView> arrangedById,
        View view,
        out ReservedRect rect)
    {
        var id = view.GetIdentifier().ID;

        if (workspace.SelectedFrameSizesById.TryGetValue(id, out var size) && size.Width > 0 && size.Height > 0)
        {
            if (arrangedById.TryGetValue(id, out var arranged))
            {
                rect = ViewPlacementGeometryService.CreateRectFromOrigin(
                    workspace, view, arranged.OriginX, arranged.OriginY, size.Width, size.Height);
                return true;
            }
        }

        // Fallback: use actual rect snapshot (captured before layout pass, not a live Tekla call)
        if (workspace.ActualViewRectsById.TryGetValue(id, out var actualRect))
        {
            var w = actualRect.MaxX - actualRect.MinX;
            var h = actualRect.MaxY - actualRect.MinY;
            if (w > 0 && h > 0)
            {
                if (arrangedById.TryGetValue(id, out var arranged2))
                {
                    // Recompute rect from planned origin using snapshot size
                    rect = ViewPlacementGeometryService.CreateRectFromOrigin(
                        workspace, view, arranged2.OriginX, arranged2.OriginY, w, h);
                }
                else
                {
                    rect = actualRect;
                }
                return true;
            }
        }

        rect = null!;
        return false;
    }

    private static bool IsFreePlacementKind(ViewSemanticKind kind)
        => kind == ViewSemanticKind.Other || kind == ViewSemanticKind.Model3D;

    private static bool IsModel3DView(DrawingLayoutWorkspace workspace, View view)
    {
        var id = view.GetIdentifier().ID;
        return workspace.GetSemanticKind(id) == ViewSemanticKind.Model3D
               || workspace.GetLayoutViewKind(id) == LayoutViewKind.Model3D;
    }

    private static void TraceFreeViewPackerSpace(
        int viewId,
        double requiredWidth,
        double requiredHeight,
        double usableMinX,
        double usableMaxY,
        ReservedRect currentRect,
        IReadOnlyList<PackedRectangle> freeRectangles)
    {
        var candidates = freeRectangles
            .OrderBy(static rect => rect.Y)
            .ThenByDescending(static rect => rect.Width * rect.Height)
            .Take(8)
            .Select(rect =>
            {
                var minX = usableMinX + rect.X;
                var maxY = usableMaxY - rect.Y;
                var maxX = minX + rect.Width;
                var minY = maxY - rect.Height;
                var fitsWidth = rect.Width + 0.01 >= requiredWidth;
                var fitsHeight = rect.Height + 0.01 >= requiredHeight;
                var reason = fitsWidth && fitsHeight
                    ? "fits"
                    : !fitsWidth && !fitsHeight
                        ? "too-narrow+too-short"
                        : !fitsWidth
                            ? "too-narrow"
                            : "too-short";
                return $"[{minX:F1},{minY:F1},{maxX:F1},{maxY:F1}] size=({rect.Width:F1},{rect.Height:F1}) result={reason}";
            });

        DrawingProjectionAlignmentService.Log(
            $"FREE_VIEW_REPOSITION model3d-space id={viewId} required=({requiredWidth:F1},{requiredHeight:F1}) current=[{currentRect.MinX:F1},{currentRect.MinY:F1},{currentRect.MaxX:F1},{currentRect.MaxY:F1}] freeCount={freeRectangles.Count} topCandidates={string.Join(";", candidates)}");
    }

    private static bool IsAnchorDrivenFreeSection(
        DrawingLayoutWorkspace workspace,
        int viewId)
    {
        var kind = workspace.GetLayoutViewKind(viewId);
        if (kind == LayoutViewKind.AnchorDetailSection)
            return true;

        // Detail views join the same anchor pipeline when their detail-mark
        // anchor is known (set by DrawingLayoutWorkspace.SetParentViewRelations).
        if (kind == LayoutViewKind.Detail)
        {
            var view = workspace.TryGetView(viewId);
            return view?.ParentAnchorX is not null && view.ParentAnchorY is not null;
        }

        return false;
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
                LayoutGap = arranged[i].LayoutGap,
                IsSnapshotFallback = arranged[i].IsSnapshotFallback
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
        DrawingLayoutWorkspace workspace,
        List<View> views,
        List<ArrangedView> arranged,
        double usableMinX, double usableMaxX,
        double usableMinY, double usableMaxY,
        IReadOnlyList<ReservedRect> reserved,
        bool applyChanges)
    {
        if (views.Count == 0)
            return arranged;

        var arrangedById = arranged.ToDictionary(static item => item.Id);
        var rects = GetViewRects(workspace, arrangedById, views);
        if (rects.Count != views.Count)
        {
            DrawingProjectionAlignmentService.Log(
                $"CENTER_GROUP_SKIP reason=rect-count-mismatch expected={views.Count} actual={rects.Count}");
            return arranged;
        }

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

        PerfTrace.Write("api-view", "center_group_plan", 0,
            $"applied=0 dx={dx:F1} dy={dy:F1} usableX={usableMinX:F1}-{usableMaxX:F1} usableY={usableMinY:F1}-{usableMaxY:F1}");

        var centeredIds = views.Select(v => v.GetIdentifier().ID).ToHashSet();
        return arranged.Select(a =>
        {
            var shifted = centeredIds.Contains(a.Id);
            return new ArrangedView
            {
                Id       = a.Id,
                ViewType = a.ViewType,
                OriginX  = shifted ? a.OriginX + dx : a.OriginX,
                OriginY  = shifted ? a.OriginY + dy : a.OriginY,
                PreferredPlacementSide = a.PreferredPlacementSide,
                ActualPlacementSide = a.ActualPlacementSide,
                PlacementFallbackUsed = a.PlacementFallbackUsed,
                LayoutMargin = a.LayoutMargin,
                LayoutGap = a.LayoutGap,
                IsSnapshotFallback = a.IsSnapshotFallback
            };
        }).ToList();
    }

    private static double CenterX(ReservedRect rect) => (rect.MinX + rect.MaxX) / 2.0;

    private static double CenterY(ReservedRect rect) => (rect.MinY + rect.MaxY) / 2.0;
}
