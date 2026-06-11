using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Geometry3d;
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
