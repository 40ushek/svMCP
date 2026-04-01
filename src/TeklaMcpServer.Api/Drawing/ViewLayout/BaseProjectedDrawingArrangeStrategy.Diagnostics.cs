using System.Collections.Generic;
using System.Linq;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using TeklaMcpServer.Api.Diagnostics;

namespace TeklaMcpServer.Api.Drawing;

public sealed partial class BaseProjectedDrawingArrangeStrategy
{
    private static void DiagnoseRelativePlacementFailure(
        List<DrawingFitConflict> conflicts,
        DrawingArrangeContext context,
        View view,
        ReservedRect anchor,
        ViewPlacementSearchArea searchArea,
        double gap,
        IReadOnlyList<ReservedRect> occupied,
        RelativePlacement preferred)
    {
        var candidates = EnumerateRelativeCandidates(context, view, anchor, searchArea, gap, preferred).ToList();
        if (candidates.Count == 0)
        {
            AddConflict(conflicts, view, preferred.ToString(), "outside_zone_bounds");
            return;
        }

        foreach (var candidate in candidates)
        {
            if (IntersectsAny(candidate, occupied))
            {
                AddIntersectionConflicts(conflicts, view, preferred.ToString(), candidate, occupied);
                return;
            }
        }

        AddConflict(conflicts, view, preferred.ToString(), "outside_zone_bounds");
    }

    private static void DiagnoseRelativePlacementFailure(
        List<DrawingFitConflict> conflicts,
        DrawingArrangeContext context,
        View view,
        ReservedRect anchor,
        double freeMinX,
        double freeMaxX,
        double freeMinY,
        double freeMaxY,
        double gap,
        IReadOnlyList<ReservedRect> occupied,
        RelativePlacement preferred)
        => DiagnoseRelativePlacementFailure(
            conflicts,
            context,
            view,
            anchor,
            new ViewPlacementSearchArea(anchor, freeMinX, freeMaxX, freeMinY, freeMaxY),
            gap,
            occupied,
            preferred);

    private static void AddIntersectionConflicts(
        List<DrawingFitConflict> conflicts,
        View view,
        string attemptedZone,
        ReservedRect rect,
        IReadOnlyList<ReservedRect> occupied)
    {
        var added = false;
        foreach (var other in occupied.Where(other => Intersects(rect, other)))
        {
            AddConflict(conflicts, view, attemptedZone, "intersects_reserved_area", target: $"{other.MinX:F1},{other.MinY:F1},{other.MaxX:F1},{other.MaxY:F1}");
            added = true;
        }

        if (!added)
            AddConflict(conflicts, view, attemptedZone, "intersects_view");
    }

    private static void AddResidualConflicts(
        List<DrawingFitConflict> conflicts,
        View view,
        DrawingArrangeContext context,
        IReadOnlyList<ReservedRect> occupied)
    {
        if (!TryGetViewBoundingRect(view, out var rect))
        {
            AddConflict(conflicts, view, "Residual", "no_residual_space", target: "maxrects_fallback");
            return;
        }

        EnsureBoundingRect(conflicts, view, "Residual", rect);

        var usableMinX = context.Margin;
        var usableMaxX = context.SheetWidth - context.Margin;
        var usableMinY = context.Margin;
        var usableMaxY = context.SheetHeight - context.Margin;

        if (!IsWithinArea(rect, usableMinX, usableMaxX, usableMinY, usableMaxY))
            AddConflict(conflicts, view, "Residual", "outside_sheet_bounds");

        AddIntersectionConflicts(conflicts, view, "Residual", rect, occupied);

        var residualConflict = conflicts.FirstOrDefault(item =>
            item.ViewId == (view.GetIdentifier()?.ID ?? 0) &&
            item.AttemptedZone == "Residual");

        if (residualConflict == null || residualConflict.Conflicts.Count == 0)
            AddConflict(conflicts, view, "Residual", "no_residual_space", target: "maxrects_fallback");
    }

    private static void AddConflict(
        List<DrawingFitConflict> conflicts,
        View view,
        string attemptedZone,
        string type,
        int? otherViewId = null,
        string target = "")
    {
        var viewId = view.GetIdentifier()?.ID ?? 0;
        var conflict = conflicts.FirstOrDefault(item => item.ViewId == viewId && item.AttemptedZone == attemptedZone);
        if (conflict == null)
        {
            conflict = new DrawingFitConflict
            {
                ViewId = viewId,
                ViewType = view.ViewType.ToString(),
                AttemptedZone = attemptedZone
            };
            conflicts.Add(conflict);
        }

        if (conflict.Conflicts.Any(item => item.Type == type && item.OtherViewId == otherViewId && item.Target == target))
            return;

        conflict.Conflicts.Add(new DrawingFitConflictItem
        {
            Type = type,
            OtherViewId = otherViewId,
            Target = target
        });
    }

    private static void EnsureBoundingRect(
        List<DrawingFitConflict> conflicts,
        View view,
        string attemptedZone,
        ReservedRect rect)
    {
        var viewId = view.GetIdentifier()?.ID ?? 0;
        var conflict = conflicts.FirstOrDefault(item => item.ViewId == viewId && item.AttemptedZone == attemptedZone);
        if (conflict == null)
        {
            conflict = new DrawingFitConflict
            {
                ViewId = viewId,
                ViewType = view.ViewType.ToString(),
                AttemptedZone = attemptedZone
            };
            conflicts.Add(conflict);
        }

        conflict.BBoxMinX = rect.MinX;
        conflict.BBoxMinY = rect.MinY;
        conflict.BBoxMaxX = rect.MaxX;
        conflict.BBoxMaxY = rect.MaxY;
    }

    private static string FormatPlannedRects(DrawingArrangeContext context, IReadOnlyList<PlannedPlacement> planned)
    {
        return string.Join(";",
            planned.Select(item =>
            {
                var width = DrawingArrangeContextSizing.GetWidth(context, item.View);
                var height = DrawingArrangeContextSizing.GetHeight(context, item.View);
                var rect = ViewPlacementGeometryService.CreateCandidateRect(item.View, item.X, item.Y, width, height);
                return $"{item.View.GetIdentifier().ID}:{rect.MinX:F2},{rect.MinY:F2},{rect.MaxX:F2},{rect.MaxY:F2}";
            }));
    }

    private static void TracePlanReject(
        string mode,
        string stage,
        DrawingArrangeContext context,
        IReadOnlyList<PlannedPlacement> planned,
        ReservedRect? attemptedRect)
    {
        var attempted = attemptedRect == null
            ? "n/a"
            : $"[{attemptedRect.MinX:F2},{attemptedRect.MinY:F2},{attemptedRect.MaxX:F2},{attemptedRect.MaxY:F2}]";

        PerfTrace.Write(
            "api-view",
            "plan_reject_snapshot",
            0,
            $"mode={mode} stage={stage} attempted={attempted} planned=[{FormatPlannedRects(context, planned)}]");
    }

    private static void DiagnoseOptionalMainSkeletonNeighborFailure(
        List<DrawingFitConflict> conflicts,
        DrawingArrangeContext context,
        MainSkeletonNeighborSpec spec,
        ViewPlacementSearchArea searchArea,
        IReadOnlyList<ReservedRect> occupied,
        View view)
        => DiagnoseRelativePlacementFailureInSearchArea(
            conflicts,
            context,
            view,
            searchArea.BaseRect,
            searchArea,
            occupied,
            spec.Placement);

    private static void DiagnoseRelativePlacementFailureInSearchArea(
        List<DrawingFitConflict> conflicts,
        DrawingArrangeContext context,
        View view,
        ReservedRect anchorRect,
        ViewPlacementSearchArea searchArea,
        IReadOnlyList<ReservedRect> occupied,
        RelativePlacement placement)
    {
        DiagnoseRelativePlacementFailure(
            conflicts,
            context,
            view,
            anchorRect,
            searchArea,
            context.Gap,
            occupied,
            placement);
    }
}
