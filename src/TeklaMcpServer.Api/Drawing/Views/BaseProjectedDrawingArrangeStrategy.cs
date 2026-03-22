using System.Collections.Generic;
using System.Linq;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;
using TeklaMcpServer.Api.Algorithms.Packing;
using TeklaMcpServer.Api.Diagnostics;

namespace TeklaMcpServer.Api.Drawing;

public sealed class BaseProjectedDrawingArrangeStrategy : IDrawingViewArrangeStrategy, IDrawingViewArrangeDiagnosticsStrategy
{
    internal const double RelaxedLayoutScaleCutoff = 50.0;

    internal readonly struct ZoneBudgets
    {
        public ZoneBudgets(double topHeight, double bottomHeight, double leftWidth, double rightWidth)
        {
            TopHeight = topHeight;
            BottomHeight = bottomHeight;
            LeftWidth = leftWidth;
            RightWidth = rightWidth;
        }

        public double TopHeight { get; }
        public double BottomHeight { get; }
        public double LeftWidth { get; }
        public double RightWidth { get; }
    }

    private readonly GaDrawingMaxRectsArrangeStrategy _maxRectsFallback = new();
    private readonly ShelfPackingDrawingArrangeStrategy _fallback = new();
    private readonly SectionPlacementSideResolver _sectionPlacementSideResolver = new(new Model());

    private sealed class PlannedPlacement
    {
        public PlannedPlacement(
            View view,
            double x,
            double y,
            SectionPlacementSide? preferredPlacementSide = null,
            SectionPlacementSide? actualPlacementSide = null)
        {
            View = view;
            X = x;
            Y = y;
            PreferredPlacementSide = preferredPlacementSide;
            ActualPlacementSide = actualPlacementSide;
        }

        public View View { get; }
        public double X { get; }
        public double Y { get; }
        public SectionPlacementSide? PreferredPlacementSide { get; }
        public SectionPlacementSide? ActualPlacementSide { get; }
    }

    internal static bool TryProjectViewLocalPointToSheet(View view, Point? localPoint, out double sheetX, out double sheetY)
    {
        sheetX = 0;
        sheetY = 0;

        if (view.Origin == null || localPoint == null)
            return false;

        var scale = view.Attributes.Scale > 0 ? view.Attributes.Scale : 1.0;
        sheetX = view.Origin.X + (localPoint.X / scale);
        sheetY = view.Origin.Y + (localPoint.Y / scale);
        return true;
    }

    public bool CanArrange(DrawingArrangeContext context)
    {
        var baseView = BaseViewSelection.Select(context.Views).View;
        return baseView != null;
    }

    public bool EstimateFit(DrawingArrangeContext context, IReadOnlyList<(double w, double h)> frames)
    {
        var planningContext = CreatePlanningContext(context, frames);

        if (TryCreatePlan(planningContext, out var planned))
        {
            // Plan succeeded, but it may leave some views unplanned.
            // EstimateFit must return true only when ALL views can be placed.
            var plannedIds = new System.Collections.Generic.HashSet<int>(
                planned.Select(p => p.View.GetIdentifier().ID));
            var unplannedViews = planningContext.Views
                .Where(v => !plannedIds.Contains(v.GetIdentifier().ID))
                .ToList();

            if (unplannedViews.Count == 0)
                return true;

            // Check if unplanned views can fit in the space left around anchors.
            var anchorRects = planned.Select(p =>
            {
                var w = DrawingArrangeContextSizing.GetWidth(planningContext, p.View);
                var h = DrawingArrangeContextSizing.GetHeight(planningContext, p.View);
                DrawingViewFrameGeometry.TryGetBoundingRectAtOrigin(p.View, p.X, p.Y, w, h, out var rect);
                return rect;
            }).ToList();
            var extendedReserved = new System.Collections.Generic.List<ReservedRect>(planningContext.ReservedAreas);
            extendedReserved.AddRange(anchorRects);
            var unplannedCtx = new DrawingArrangeContext(
                planningContext.Drawing, unplannedViews,
                planningContext.SheetWidth, planningContext.SheetHeight,
                planningContext.Margin, planningContext.Gap,
                extendedReserved, planningContext.EffectiveFrameSizes);
            var unplannedFrames = unplannedViews
                .Select(v => (DrawingArrangeContextSizing.GetWidth(unplannedCtx, v), DrawingArrangeContextSizing.GetHeight(unplannedCtx, v)))
                .ToList();
            return _maxRectsFallback.EstimateFit(unplannedCtx, unplannedFrames);
        }

        var maxr  = _maxRectsFallback.EstimateFit(planningContext, frames);
        var shelf = !maxr && planningContext.ReservedAreas.Count == 0 && _fallback.EstimateFit(planningContext, frames);
        return maxr || shelf;
    }

    public List<ArrangedView> Arrange(DrawingArrangeContext context)
    {
        if (TryCreatePlan(context, out var planned))
        {
            var result = ApplyPlan(planned);

            // If the plan only placed anchor views (FrontView, TopView, primary section etc.),
            // run MaxRects for any remaining unplanned views, treating the anchor placements
            // as additional blocked areas.  This keeps FrontView/TopView at their correct
            // projection-aware positions while still placing secondary sections on the sheet.
            var plannedIds = new System.Collections.Generic.HashSet<int>(
                planned.Select(p => p.View.GetIdentifier().ID));
            var unplanned = context.Views
                .Where(v => !plannedIds.Contains(v.GetIdentifier().ID))
                .ToList();

            if (unplanned.Count > 0)
            {
                var anchorRects = planned.Select(p =>
                {
                    var w = DrawingArrangeContextSizing.GetWidth(context, p.View);
                    var h = DrawingArrangeContextSizing.GetHeight(context, p.View);
                    DrawingViewFrameGeometry.TryGetBoundingRectAtOrigin(p.View, p.X, p.Y, w, h, out var rect);
                    return rect;
                }).ToList();

                var extendedReserved = new System.Collections.Generic.List<ReservedRect>(context.ReservedAreas);
                extendedReserved.AddRange(anchorRects);

                var unplannedCtx = new DrawingArrangeContext(
                    context.Drawing, unplanned,
                    context.SheetWidth, context.SheetHeight,
                    context.Margin, context.Gap,
                    extendedReserved, context.EffectiveFrameSizes);

                PerfTrace.Write("api-view", "front_arrange_plan", 0,
                    $"mode=anchor-then-maxrects anchors={planned.Count} remaining={unplanned.Count}");
                var unplannedFrames = unplanned
                    .Select(v => (DrawingArrangeContextSizing.GetWidth(unplannedCtx, v), DrawingArrangeContextSizing.GetHeight(unplannedCtx, v)))
                    .ToList();
                if (_maxRectsFallback.EstimateFit(unplannedCtx, unplannedFrames))
                {
                    var unplannedArranged = _maxRectsFallback.Arrange(unplannedCtx);
                    return result.Concat(unplannedArranged).ToList();
                }
                // Sections don't fit around anchors — keep anchors only, leave sections in place.
                PerfTrace.Write("api-view", "front_arrange_plan", 0, "mode=anchor-only sections-unfit");
                return result;
            }

            PerfTrace.Write("api-view", "front_arrange_plan", 0, $"mode=custom views={planned.Count}");
            return result;
        }

        var frames = context.Views.Select(v => (DrawingArrangeContextSizing.GetWidth(context, v), DrawingArrangeContextSizing.GetHeight(context, v))).ToList();
        if (_maxRectsFallback.EstimateFit(context, frames))
        {
            PerfTrace.Write("api-view", "front_arrange_plan", 0, "mode=maxrects-fallback");
            return _maxRectsFallback.Arrange(context);
        }

        PerfTrace.Write("api-view", "front_arrange_plan", 0, "mode=shelf-fallback");
        return _fallback.Arrange(context);
    }

    public List<DrawingFitConflict> DiagnoseFitConflicts(DrawingArrangeContext context, IReadOnlyList<(double w, double h)> frames)
    {
        var conflicts = new List<DrawingFitConflict>();
        var planningContext = CreatePlanningContext(context, frames);

        var baseViewSelection = BaseViewSelection.Select(planningContext.Views);
        var baseView = baseViewSelection.View;
        if (baseView == null)
            return conflicts;

        if (TryCreatePlan(planningContext, out var planned))
        {
            var plannedIds = new System.Collections.Generic.HashSet<int>(
                planned.Select(p => p.View.GetIdentifier().ID));
            var unplannedViews = planningContext.Views
                .Where(v => !plannedIds.Contains(v.GetIdentifier().ID))
                .ToList();

            if (unplannedViews.Count == 0)
                return conflicts;

            var anchorRects = planned.Select(p =>
            {
                var w = DrawingArrangeContextSizing.GetWidth(planningContext, p.View);
                var h = DrawingArrangeContextSizing.GetHeight(planningContext, p.View);
                return new ReservedRect(p.X - w / 2.0, p.Y - h / 2.0, p.X + w / 2.0, p.Y + h / 2.0);
            }).ToList();
            var extendedReserved = new System.Collections.Generic.List<ReservedRect>(planningContext.ReservedAreas);
            extendedReserved.AddRange(anchorRects);
            var unplannedCtx = new DrawingArrangeContext(
                planningContext.Drawing, unplannedViews,
                planningContext.SheetWidth, planningContext.SheetHeight,
                planningContext.Margin, planningContext.Gap,
                extendedReserved, planningContext.EffectiveFrameSizes);
            var unplannedFrames = unplannedViews
                .Select(v => (DrawingArrangeContextSizing.GetWidth(unplannedCtx, v), DrawingArrangeContextSizing.GetHeight(unplannedCtx, v)))
                .ToList();

            if (!_maxRectsFallback.EstimateFit(unplannedCtx, unplannedFrames))
            {
                foreach (var view in unplannedViews)
                    AddResidualConflicts(conflicts, view, unplannedCtx, extendedReserved);
            }

            if (conflicts.Count > 0)
                return conflicts;
        }

        var semanticViews = SemanticViewSet.Build(planningContext.Views);

        var neighbors = StandardNeighborResolver.Build(planningContext.Views, semanticViews, baseViewSelection);
        var sectionGroups = SectionGroupSet.Build(
            semanticViews.Sections,
            planningContext.Drawing,
            baseView,
            _sectionPlacementSideResolver);

        var leftSections = sectionGroups.Left;
        var rightSections = sectionGroups.Right;
        var topSections = sectionGroups.Top;
        var bottomSections = sectionGroups.Bottom;

        DiagnoseRelaxedLayoutConflicts(planningContext, neighbors, leftSections, rightSections, topSections, bottomSections, conflicts);
        return conflicts;
    }

    private static DrawingArrangeContext CreatePlanningContext(
        DrawingArrangeContext context,
        IReadOnlyList<(double w, double h)> frames)
    {
        if (frames.Count != context.Views.Count)
            return context;

        var effectiveFrameSizes = context.EffectiveFrameSizes.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value);
        for (var i = 0; i < context.Views.Count; i++)
        {
            var view = context.Views[i];
            var frame = frames[i];
            effectiveFrameSizes[view.GetIdentifier().ID] = (frame.w, frame.h);
        }

        return new DrawingArrangeContext(
            context.Drawing,
            context.Views,
            context.SheetWidth,
            context.SheetHeight,
            context.Margin,
            context.Gap,
            context.ReservedAreas,
            effectiveFrameSizes);
    }

    internal static bool ShouldPreferRelaxedLayout(double scale)
        => scale >= RelaxedLayoutScaleCutoff;

    internal static (double minX, double maxX, double minY, double maxY) ComputeFreeArea(DrawingArrangeContext context)
        => ComputeFreeArea(context.SheetWidth, context.SheetHeight, context.Margin, context.Gap, context.ReservedAreas);

    internal static (double minX, double maxX, double minY, double maxY) ComputeFreeArea(
        double sheetWidth,
        double sheetHeight,
        double margin,
        double gap,
        IReadOnlyList<ReservedRect> reservedAreas)
    {
        double minX = margin;
        double maxX = sheetWidth - margin;
        double minY = margin;
        double maxY = sheetHeight - margin;

        const double edgeTol = 5.0;
        const double edgeCoverageRatio = 0.75;
        var usableWidth = System.Math.Max(sheetWidth - 2 * margin, 1);
        var usableHeight = System.Math.Max(sheetHeight - 2 * margin, 1);
        foreach (var area in reservedAreas)
        {
            var widthRatio = area.Width / usableWidth;
            var heightRatio = area.Height / usableHeight;

            if (area.MinX <= margin + edgeTol && heightRatio >= edgeCoverageRatio)
                minX = System.Math.Max(minX, area.MaxX + gap);
            if (area.MaxX >= sheetWidth - margin - edgeTol && heightRatio >= edgeCoverageRatio)
                maxX = System.Math.Min(maxX, area.MinX - gap);
            if (area.MinY <= margin + edgeTol && widthRatio >= edgeCoverageRatio)
                minY = System.Math.Max(minY, area.MaxY + gap);
            if (area.MaxY >= sheetHeight - margin - edgeTol && widthRatio >= edgeCoverageRatio)
                maxY = System.Math.Min(maxY, area.MinY - gap);
        }

        if (maxX <= minX) { minX = margin; maxX = sheetWidth - margin; }
        if (maxY <= minY) { minY = margin; maxY = sheetHeight - margin; }

        return (minX, maxX, minY, maxY);
    }

    internal static bool TryPackSupplementalViews(
        IReadOnlyList<(double width, double height)> viewSizes,
        double freeMinX,
        double freeMaxX,
        double freeMinY,
        double freeMaxY,
        double gap,
        ReservedRect blockedRect,
        out List<(double centerX, double centerY)> placements)
    {
        placements = new List<(double centerX, double centerY)>(viewSizes.Count);
        if (viewSizes.Count == 0)
            return true;

        var availableW = freeMaxX - freeMinX;
        var availableH = freeMaxY - freeMinY;
        if (availableW <= 0 || availableH <= 0)
            return false;

        var blocked = new List<PackedRectangle>();
        var blockedMinX = System.Math.Max(freeMinX, blockedRect.MinX - gap);
        var blockedMaxX = System.Math.Min(freeMaxX, blockedRect.MaxX + gap);
        var blockedMinY = System.Math.Max(freeMinY, blockedRect.MinY - gap);
        var blockedMaxY = System.Math.Min(freeMaxY, blockedRect.MaxY + gap);
        if (blockedMaxX > blockedMinX && blockedMaxY > blockedMinY)
        {
            blocked.Add(new PackedRectangle(
                blockedMinX - freeMinX,
                freeMaxY - blockedMaxY,
                blockedMaxX - blockedMinX,
                blockedMaxY - blockedMinY));
        }

        var packer = new MaxRectsBinPacker(availableW + gap, availableH + gap, allowRotation: false, blockedRectangles: blocked);
        var ordered = viewSizes
            .Select((size, index) => (size.width, size.height, index))
            .OrderByDescending(x => x.width * x.height)
            .ToList();
        var resolved = new (double centerX, double centerY)[viewSizes.Count];

        foreach (var item in ordered)
        {
            if (!packer.TryInsert(item.width + gap, item.height + gap, MaxRectsHeuristic.BestAreaFit, out var placement))
                return false;

            resolved[item.index] = (
                freeMinX + placement.X + item.width / 2.0,
                freeMaxY - placement.Y - item.height / 2.0);
        }

        placements.AddRange(resolved);
        return true;
    }

    private static void DiagnoseRelaxedLayoutConflicts(
        DrawingArrangeContext context,
        NeighborSet neighbors,
        IReadOnlyList<View> leftSections,
        IReadOnlyList<View> rightSections,
        IReadOnlyList<View> topSections,
        IReadOnlyList<View> bottomSections,
        List<DrawingFitConflict> conflicts)
    {
        var baseView = neighbors.BaseView;
        var top = neighbors.TopNeighbor;
        var bottom = neighbors.BottomNeighbor;
        var leftNeighbor = neighbors.SideNeighborLeft;
        var rightNeighbor = neighbors.SideNeighborRight;

        var blocked = NormalizeReservedAreas(context);
        var (freeMinX, freeMaxX, freeMinY, freeMaxY) = ComputeFreeArea(context);
        var baseWidth = DrawingArrangeContextSizing.GetWidth(context, baseView);
        var baseHeight = DrawingArrangeContextSizing.GetHeight(context, baseView);

        var budgets = ComputeZoneBudgets(context, neighbors, leftSections, rightSections, topSections, bottomSections);

        var baseRect = new ReservedRect(0, 0, 0, 0);
        var placed = false;
        foreach (var (x1, x2, y1, y2) in EnumerateBaseViewWindows(
                     freeMinX,
                     freeMaxX,
                     freeMinY,
                     freeMaxY,
                     budgets,
                     includeRelaxedCandidates: true))
        {
            if (x2 - x1 >= baseWidth && y2 - y1 >= baseHeight
                && TryFindBaseViewRectInWindow(
                    blocked,
                    new ReservedRect(x1, y1, x2, y2),
                    baseWidth,
                    baseHeight,
                    out baseRect))
            {
                placed = true;
                break;
            }
        }

        if (!placed)
        {
            AddConflict(conflicts, baseView, "Center", "outside_zone_bounds");
            return;
        }

        var occupied = new List<ReservedRect>(blocked) { baseRect };
        var topRect = new ReservedRect(0, 0, 0, 0);
        var bottomRect = new ReservedRect(0, 0, 0, 0);
        var leftRect = new ReservedRect(0, 0, 0, 0);
        var rightRect = new ReservedRect(0, 0, 0, 0);
        var topPlaced = false;
        var bottomPlaced = false;
        var leftPlaced = false;
        var rightPlaced = false;

        if (top != null)
        {
            if (TryPlaceRelative(context, top, baseRect, freeMinX, freeMaxX, freeMinY, freeMaxY, context.Gap, occupied, RelativePlacement.Top, out var rect)
                || TryFindTopViewAtSheetTop(context, top, freeMinX, freeMaxX, freeMinY, freeMaxY, occupied, out rect))
            {
                topRect = rect;
                topPlaced = true;
                occupied.Add(rect);
            }
            else
            {
                DiagnoseRelativePlacementFailure(conflicts, context, top, baseRect, freeMinX, freeMaxX, freeMinY, freeMaxY, context.Gap, occupied, RelativePlacement.Top);
            }
        }

        if (bottom != null)
        {
            if (TryPlaceRelative(context, bottom, baseRect, freeMinX, freeMaxX, freeMinY, freeMaxY, context.Gap, occupied, RelativePlacement.Bottom, out var rect))
            {
                bottomRect = rect;
                bottomPlaced = true;
                occupied.Add(rect);
            }
            else
            {
                DiagnoseRelativePlacementFailure(conflicts, context, bottom, baseRect, freeMinX, freeMaxX, freeMinY, freeMaxY, context.Gap, occupied, RelativePlacement.Bottom);
            }
        }

        if (leftNeighbor != null)
        {
            if (TryPlaceRelative(context, leftNeighbor, baseRect, freeMinX, freeMaxX, freeMinY, freeMaxY, context.Gap, occupied, RelativePlacement.Left, out var rect))
            {
                leftRect = rect;
                leftPlaced = true;
                occupied.Add(rect);
            }
            else
            {
                DiagnoseRelativePlacementFailure(conflicts, context, leftNeighbor, baseRect, freeMinX, freeMaxX, freeMinY, freeMaxY, context.Gap, occupied, RelativePlacement.Left);
            }
        }

        if (rightNeighbor != null)
        {
            if (TryPlaceRelative(context, rightNeighbor, baseRect, freeMinX, freeMaxX, freeMinY, freeMaxY, context.Gap, occupied, RelativePlacement.Right, out var rect))
            {
                rightRect = rect;
                rightPlaced = true;
                occupied.Add(rect);
            }
            else
            {
                DiagnoseRelativePlacementFailure(conflicts, context, rightNeighbor, baseRect, freeMinX, freeMaxX, freeMinY, freeMaxY, context.Gap, occupied, RelativePlacement.Right);
            }
        }

        DiagnoseStackPlacementFailureWithFallback(
            conflicts,
            context,
            leftSections,
            baseRect,
            leftPlaced ? leftRect : baseRect,
            rightPlaced ? rightRect : baseRect,
            RelativePlacement.Left,
            freeMinX,
            freeMaxX,
            freeMinY,
            freeMaxY,
            context.Gap,
            occupied);

        DiagnoseStackPlacementFailureWithFallback(
            conflicts,
            context,
            rightSections,
            baseRect,
            rightPlaced ? rightRect : baseRect,
            leftPlaced ? leftRect : baseRect,
            RelativePlacement.Right,
            freeMinX,
            freeMaxX,
            freeMinY,
            freeMaxY,
            context.Gap,
            occupied);

        DiagnoseHorizontalStackPlacementFailureWithFallback(
            conflicts,
            context,
            topSections,
            baseRect,
            topPlaced ? topRect : baseRect,
            bottomPlaced ? bottomRect : baseRect,
            RelativePlacement.Top,
            freeMinX,
            freeMaxX,
            freeMinY,
            freeMaxY,
            context.Gap,
            occupied);

        DiagnoseHorizontalStackPlacementFailureWithFallback(
            conflicts,
            context,
            bottomSections,
            baseRect,
            bottomPlaced ? bottomRect : baseRect,
            topPlaced ? topRect : baseRect,
            RelativePlacement.Bottom,
            freeMinX,
            freeMaxX,
            freeMinY,
            freeMaxY,
            context.Gap,
            occupied);
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
    {
        var candidates = EnumerateRelativeCandidates(context, view, anchor, freeMinX, freeMaxX, freeMinY, freeMaxY, gap, preferred).ToList();
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

    private static void DiagnoseStackPlacementFailureWithFallback(
        List<DrawingFitConflict> conflicts,
        DrawingArrangeContext context,
        IReadOnlyList<View> sectionViews,
        ReservedRect frontRect,
        ReservedRect preferredAnchorRect,
        ReservedRect fallbackAnchorRect,
        RelativePlacement preferredZone,
        double freeMinX,
        double freeMaxX,
        double freeMinY,
        double freeMaxY,
        double gap,
        IReadOnlyList<ReservedRect> occupied)
    {
        if (sectionViews.Count == 0)
            return;

        if (TryPlaceVerticalSectionStack(context, sectionViews, frontRect, preferredAnchorRect, preferredZone, freeMinX, freeMaxX, freeMinY, freeMaxY, gap, occupied.ToList(), new List<PlannedPlacement>(), ToPlacementSide(preferredZone), ToPlacementSide(preferredZone)))
            return;

        var fallbackZone = preferredZone == RelativePlacement.Left ? RelativePlacement.Right : RelativePlacement.Left;
        if (TryPlaceVerticalSectionStack(context, sectionViews, frontRect, fallbackAnchorRect, fallbackZone, freeMinX, freeMaxX, freeMinY, freeMaxY, gap, occupied.ToList(), new List<PlannedPlacement>(), ToPlacementSide(preferredZone), ToPlacementSide(fallbackZone)))
            return;

        DiagnoseVerticalStackPlacementFailure(conflicts, context, sectionViews, frontRect, preferredAnchorRect, preferredZone, freeMinX, freeMaxX, freeMinY, freeMaxY, gap, occupied);
    }

    private static void DiagnoseHorizontalStackPlacementFailureWithFallback(
        List<DrawingFitConflict> conflicts,
        DrawingArrangeContext context,
        IReadOnlyList<View> sectionViews,
        ReservedRect frontRect,
        ReservedRect preferredAnchorRect,
        ReservedRect fallbackAnchorRect,
        RelativePlacement preferredZone,
        double freeMinX,
        double freeMaxX,
        double freeMinY,
        double freeMaxY,
        double gap,
        IReadOnlyList<ReservedRect> occupied)
    {
        if (sectionViews.Count == 0)
            return;

        if (TryPlaceHorizontalSectionStack(context, sectionViews, frontRect, preferredAnchorRect, preferredZone, freeMinX, freeMaxX, freeMinY, freeMaxY, gap, occupied.ToList(), new List<PlannedPlacement>(), ToPlacementSide(preferredZone), ToPlacementSide(preferredZone)))
            return;

        var fallbackZone = preferredZone == RelativePlacement.Top ? RelativePlacement.Bottom : RelativePlacement.Top;
        if (TryPlaceHorizontalSectionStack(context, sectionViews, frontRect, fallbackAnchorRect, fallbackZone, freeMinX, freeMaxX, freeMinY, freeMaxY, gap, occupied.ToList(), new List<PlannedPlacement>(), ToPlacementSide(preferredZone), ToPlacementSide(fallbackZone)))
            return;

        DiagnoseHorizontalStackPlacementFailure(conflicts, context, sectionViews, frontRect, preferredAnchorRect, preferredZone, freeMinX, freeMaxX, freeMinY, freeMaxY, gap, occupied);
    }

    private static void DiagnoseVerticalStackPlacementFailure(
        List<DrawingFitConflict> conflicts,
        DrawingArrangeContext context,
        IReadOnlyList<View> sectionViews,
        ReservedRect frontRect,
        ReservedRect anchorRect,
        RelativePlacement zone,
        double freeMinX,
        double freeMaxX,
        double freeMinY,
        double freeMaxY,
        double gap,
        IReadOnlyList<ReservedRect> occupied)
    {
        var proposed = new List<ReservedRect>();
        var currentAnchor = anchorRect;
        foreach (var section in sectionViews.OrderByDescending(view => DrawingArrangeContextSizing.GetWidth(context, view) * DrawingArrangeContextSizing.GetHeight(context, view)).ThenBy(view => view.GetIdentifier().ID))
        {
            var width = DrawingArrangeContextSizing.GetWidth(context, section);
            var height = DrawingArrangeContextSizing.GetHeight(context, section);
            var minY = CenterY(frontRect) - height / 2.0;
            if (minY < freeMinY || minY + height > freeMaxY)
            {
                AddConflict(conflicts, section, zone.ToString(), "outside_zone_bounds");
                return;
            }

            ReservedRect rect;
            if (zone == RelativePlacement.Right)
            {
                var minX = currentAnchor.MaxX + gap;
                rect = new ReservedRect(minX, minY, minX + width, minY + height);
            }
            else
            {
                var maxX = currentAnchor.MinX - gap;
                rect = new ReservedRect(maxX - width, minY, maxX, minY + height);
            }

            if (!IsWithinArea(rect, freeMinX, freeMaxX, freeMinY, freeMaxY))
            {
                AddConflict(conflicts, section, zone.ToString(), "outside_zone_bounds");
                return;
            }

            if (IntersectsAny(rect, occupied))
            {
                AddIntersectionConflicts(conflicts, section, zone.ToString(), rect, occupied);
                return;
            }

            var proposedHit = proposed.FirstOrDefault(item => Intersects(item, rect));
            if (proposedHit.Width > 0 || proposedHit.Height > 0)
            {
                AddConflict(conflicts, section, zone.ToString(), "intersects_view");
                return;
            }

            proposed.Add(rect);
            currentAnchor = rect;
        }
    }

    private static void DiagnoseHorizontalStackPlacementFailure(
        List<DrawingFitConflict> conflicts,
        DrawingArrangeContext context,
        IReadOnlyList<View> sectionViews,
        ReservedRect frontRect,
        ReservedRect anchorRect,
        RelativePlacement zone,
        double freeMinX,
        double freeMaxX,
        double freeMinY,
        double freeMaxY,
        double gap,
        IReadOnlyList<ReservedRect> occupied)
    {
        var proposed = new List<ReservedRect>();
        var currentAnchor = anchorRect;
        foreach (var section in sectionViews.OrderByDescending(view => DrawingArrangeContextSizing.GetWidth(context, view) * DrawingArrangeContextSizing.GetHeight(context, view)).ThenBy(view => view.GetIdentifier().ID))
        {
            var width = DrawingArrangeContextSizing.GetWidth(context, section);
            var height = DrawingArrangeContextSizing.GetHeight(context, section);
            var minX = CenterX(frontRect) - width / 2.0;
            if (minX < freeMinX || minX + width > freeMaxX)
            {
                AddConflict(conflicts, section, zone.ToString(), "outside_zone_bounds");
                return;
            }

            ReservedRect rect;
            if (zone == RelativePlacement.Top)
            {
                var minY = currentAnchor.MaxY + gap;
                rect = new ReservedRect(minX, minY, minX + width, minY + height);
            }
            else
            {
                var maxY = currentAnchor.MinY - gap;
                rect = new ReservedRect(minX, maxY - height, minX + width, maxY);
            }

            if (!IsWithinArea(rect, freeMinX, freeMaxX, freeMinY, freeMaxY))
            {
                AddConflict(conflicts, section, zone.ToString(), "outside_zone_bounds");
                return;
            }

            if (IntersectsAny(rect, occupied))
            {
                AddIntersectionConflicts(conflicts, section, zone.ToString(), rect, occupied);
                return;
            }

            var proposedHit = proposed.FirstOrDefault(item => Intersects(item, rect));
            if (proposedHit.Width > 0 || proposedHit.Height > 0)
            {
                AddConflict(conflicts, section, zone.ToString(), "intersects_view");
                return;
            }

            proposed.Add(rect);
            currentAnchor = rect;
        }
    }

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
            item.ViewId == view.GetIdentifier().ID &&
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
        var viewId = view.GetIdentifier().ID;
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
        var viewId = view.GetIdentifier().ID;
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

    private static bool TryGetViewBoundingRect(View view, out ReservedRect rect)
        => DrawingViewFrameGeometry.TryGetBoundingRect(view, out rect);

    private static List<ArrangedView> ApplyPlan(List<PlannedPlacement> planned)
    {
        var arranged = new List<ArrangedView>(planned.Count);
        foreach (var item in planned)
        {
            var origin = item.View.Origin;
            origin.X = item.X;
            origin.Y = item.Y;
            item.View.Origin = origin;
            item.View.Modify();
            arranged.Add(new ArrangedView
            {
                Id = item.View.GetIdentifier().ID,
                ViewType = item.View.ViewType.ToString(),
                OriginX = item.X,
                OriginY = item.Y,
                PreferredPlacementSide = ToPlacementSideString(item.PreferredPlacementSide),
                ActualPlacementSide = ToPlacementSideString(item.ActualPlacementSide),
                PlacementFallbackUsed = item.PreferredPlacementSide.HasValue
                    && item.ActualPlacementSide.HasValue
                    && item.PreferredPlacementSide.Value != item.ActualPlacementSide.Value
            });
        }

        return arranged;
    }

    private bool TryCreatePlan(DrawingArrangeContext context, out List<PlannedPlacement> planned)
    {
        planned = new List<PlannedPlacement>();

        var baseViewSelection = BaseViewSelection.Select(context.Views);
        var baseView = baseViewSelection.View;
        if (baseView == null)
            return false;

        var semanticViews = SemanticViewSet.Build(context.Views);

        var neighbors = StandardNeighborResolver.Build(context.Views, semanticViews, baseViewSelection);
        var sections = semanticViews.Sections;
        var detailViews = semanticViews.Details;
        var detailRelations = DetailRelationResolver.Build(context.Views, detailViews).All.ToList();
        var otherViews = semanticViews.Other;
        var sectionGroups = SectionGroupSet.Build(
            sections,
            context.Drawing,
            baseView,
            _sectionPlacementSideResolver);
        var leftSections = sectionGroups.Left;
        var rightSections = sectionGroups.Right;
        var topSections = sectionGroups.Top;
        var bottomSections = sectionGroups.Bottom;
        var unknownSections = sectionGroups.Unknown;
        var deferredSections = leftSections
            .Concat(rightSections)
            .Concat(topSections)
            .Concat(bottomSections)
            .Concat(unknownSections)
            .ToList();
        var nonDetailSecondaryViews = neighbors.ResidualProjected
            .Concat(otherViews)
            .ToList();
        var secondaryViews = nonDetailSecondaryViews
            .Concat(deferredSections)
            .ToList();

        PerfTrace.Write(
            "api-view",
            "view_semantic_summary",
            0,
            $"baseProjected={semanticViews.BaseProjected.Count} sections={sections.Count} details={detailViews.Count} other={otherViews.Count}");

        if (sections.Count > 0)
        {
            PerfTrace.Write(
                "api-view",
                "section_placement_side_summary",
                0,
                $"sections={sections.Count} left={leftSections.Count} right={rightSections.Count} top={topSections.Count} bottom={bottomSections.Count} unknown={unknownSections.Count} deferred={deferredSections.Count}");
        }

        var scale = GetCurrentScale(context);
        if (!ShouldPreferRelaxedLayout(scale))
        {
            if (TryPlanStrictLayout(context, neighbors, leftSections, rightSections, topSections, bottomSections, detailRelations, secondaryViews, out planned))
                return true;
            PerfTrace.Write("api-view", "front_arrange_try", 0, "mode=strict result=failed");
        }

        if (TryPlanRelaxedLayout(context, neighbors, leftSections, rightSections, topSections, bottomSections, detailRelations, secondaryViews, out planned))
            return true;
        PerfTrace.Write("api-view", "front_arrange_try", 0, "mode=relaxed result=failed");

        var strictRetry = TryPlanStrictLayout(context, neighbors, leftSections, rightSections, topSections, bottomSections, detailRelations, secondaryViews, out planned);
        PerfTrace.Write("api-view", "front_arrange_try", 0, $"mode=strict-retry result={(strictRetry ? "ok" : "failed")}");
        return strictRetry;
    }

    private static double GetCurrentScale(DrawingArrangeContext context)
        => context.Views.Select(v => v.Attributes.Scale).FirstOrDefault(s => s > 0);

    private bool TryPlanStrictLayout(
        DrawingArrangeContext context,
        NeighborSet neighbors,
        IReadOnlyList<View> leftSections,
        IReadOnlyList<View> rightSections,
        IReadOnlyList<View> topSections,
        IReadOnlyList<View> bottomSections,
        IReadOnlyList<DetailRelation> detailRelations,
        IReadOnlyList<View> secondaryViews,
        out List<PlannedPlacement> planned)
    {
        planned = new List<PlannedPlacement>();

        var baseView = neighbors.BaseView;
        var top = neighbors.TopNeighbor;
        var bottom = neighbors.BottomNeighbor;
        var leftNeighbor = neighbors.SideNeighborLeft;
        var rightNeighbor = neighbors.SideNeighborRight;
        var gap = context.Gap;
        var blocked = NormalizeReservedAreas(context);
        var freeArea = ComputeFreeArea(context);
        var baseWidth = DrawingArrangeContextSizing.GetWidth(context, baseView);
        var baseHeight = DrawingArrangeContextSizing.GetHeight(context, baseView);
        var topWidth = top != null ? DrawingArrangeContextSizing.GetWidth(context, top) : 0;
        var topHeight = top != null ? DrawingArrangeContextSizing.GetHeight(context, top) : 0;
        var bottomWidth = bottom != null ? DrawingArrangeContextSizing.GetWidth(context, bottom) : 0;
        var bottomHeight = bottom != null ? DrawingArrangeContextSizing.GetHeight(context, bottom) : 0;
        var leftNeighborWidth = leftNeighbor != null ? DrawingArrangeContextSizing.GetWidth(context, leftNeighbor) : 0;
        var leftNeighborHeight = leftNeighbor != null ? DrawingArrangeContextSizing.GetHeight(context, leftNeighbor) : 0;
        var rightNeighborWidth = rightNeighbor != null ? DrawingArrangeContextSizing.GetWidth(context, rightNeighbor) : 0;
        var rightNeighborHeight = rightNeighbor != null ? DrawingArrangeContextSizing.GetHeight(context, rightNeighbor) : 0;
        var leftSectionStackWidth = ComputeHorizontalStackWidth(context, leftSections, gap);
        var rightSectionStackWidth = ComputeHorizontalStackWidth(context, rightSections, gap);
        var topSectionStackHeight = ComputeVerticalStackHeight(context, topSections, gap);
        var bottomSectionStackHeight = ComputeVerticalStackHeight(context, bottomSections, gap);
        PerfTrace.Write(
            "api-view",
            "front_arrange_strict_input",
            0,
            $"free=({freeArea.minX:F2},{freeArea.maxX:F2},{freeArea.minY:F2},{freeArea.maxY:F2}) base=({baseWidth:F2},{baseHeight:F2}) top=({topWidth:F2},{topHeight:F2}) leftStackW=({leftSectionStackWidth:F2}) rightStackW=({rightSectionStackWidth:F2}) leftNeighbor=({leftNeighborWidth:F2},{leftNeighborHeight:F2}) rightNeighbor=({rightNeighborWidth:F2},{rightNeighborHeight:F2}) bottom=({bottomWidth:F2},{bottomHeight:F2}) budgets=(top:{(top != null ? topHeight + gap : 0) + topSectionStackHeight:F2},bottom:{(bottom != null ? bottomHeight + gap : 0) + bottomSectionStackHeight:F2},left:{(leftNeighbor != null ? leftNeighborWidth + gap : 0) + leftSectionStackWidth:F2},right:{(rightNeighbor != null ? rightNeighborWidth + gap : 0) + rightSectionStackWidth:F2})");

        var budgets = new ZoneBudgets(
            topHeight: (top != null ? topHeight + gap : 0) + topSectionStackHeight,
            bottomHeight: (bottom != null ? bottomHeight + gap : 0) + bottomSectionStackHeight,
            leftWidth: (leftNeighbor != null ? leftNeighborWidth + gap : 0) + leftSectionStackWidth,
            rightWidth: (rightNeighbor != null ? rightNeighborWidth + gap : 0) + rightSectionStackWidth);

        if (!TryFindBaseViewWindow(
                freeArea.minX,
                freeArea.maxX,
                freeArea.minY,
                freeArea.maxY,
                baseWidth,
                baseHeight,
                budgets,
                out var baseWindow))
        {
            TracePlanReject("strict", "base", context, planned, null);
            return false;
        }

        if (!TryFindBaseViewRectInWindow(
                blocked,
                baseWindow,
                baseWidth,
                baseHeight,
                out var baseRect))
        {
            TracePlanReject("strict", "base", context, planned, null);
            return false;
        }

        planned.Add(new PlannedPlacement(baseView, CenterX(baseRect), CenterY(baseRect)));

        var occupied = new List<ReservedRect>(blocked) { baseRect };
        var topRect = new ReservedRect(0, 0, 0, 0);
        var bottomRect = new ReservedRect(0, 0, 0, 0);
        var leftRect = new ReservedRect(0, 0, 0, 0);
        var rightRect = new ReservedRect(0, 0, 0, 0);

        if (top != null)
        {
            topRect = new ReservedRect(
                CenterX(baseRect) - topWidth / 2.0,
                baseRect.MaxY + gap,
                CenterX(baseRect) + topWidth / 2.0,
                baseRect.MaxY + gap + topHeight);
            if (!IsWithinArea(topRect, freeArea.minX, freeArea.maxX, freeArea.minY, freeArea.maxY) || IntersectsAny(topRect, occupied))
            {
                TracePlanReject("strict", "top", context, planned, topRect);
                return false;
            }

            planned.Add(new PlannedPlacement(top, CenterX(topRect), CenterY(topRect)));
            occupied.Add(topRect);
        }

        if (bottom != null)
        {
            bottomRect = new ReservedRect(
                CenterX(baseRect) - bottomWidth / 2.0,
                baseRect.MinY - gap - bottomHeight,
                CenterX(baseRect) + bottomWidth / 2.0,
                baseRect.MinY - gap);
            if (!IsWithinArea(bottomRect, freeArea.minX, freeArea.maxX, freeArea.minY, freeArea.maxY) || IntersectsAny(bottomRect, occupied))
            {
                TracePlanReject("strict", "bottom", context, planned, bottomRect);
                return false;
            }

            planned.Add(new PlannedPlacement(bottom, CenterX(bottomRect), CenterY(bottomRect)));
            occupied.Add(bottomRect);
        }

        if (leftNeighbor != null)
        {
            leftRect = new ReservedRect(
                baseRect.MinX - gap - leftNeighborWidth,
                CenterY(baseRect) - leftNeighborHeight / 2.0,
                baseRect.MinX - gap,
                CenterY(baseRect) + leftNeighborHeight / 2.0);
            if (!IsWithinArea(leftRect, freeArea.minX, freeArea.maxX, freeArea.minY, freeArea.maxY) || IntersectsAny(leftRect, occupied))
            {
                TracePlanReject("strict", "left", context, planned, leftRect);
                return false;
            }

            planned.Add(new PlannedPlacement(leftNeighbor, CenterX(leftRect), CenterY(leftRect)));
            occupied.Add(leftRect);
        }

        if (rightNeighbor != null)
        {
            rightRect = new ReservedRect(
                baseRect.MaxX + gap,
                CenterY(baseRect) - rightNeighborHeight / 2.0,
                baseRect.MaxX + gap + rightNeighborWidth,
                CenterY(baseRect) + rightNeighborHeight / 2.0);
            if (!IsWithinArea(rightRect, freeArea.minX, freeArea.maxX, freeArea.minY, freeArea.maxY) || IntersectsAny(rightRect, occupied))
            {
                TracePlanReject("strict", "right", context, planned, rightRect);
                return false;
            }

            planned.Add(new PlannedPlacement(rightNeighbor, CenterX(rightRect), CenterY(rightRect)));
            occupied.Add(rightRect);
        }

        var leftAnchor = leftNeighbor != null ? leftRect : baseRect;
        var rightAnchor = rightNeighbor != null ? rightRect : baseRect;
        TryPlaceVerticalSectionStackWithFallback(
            context,
            leftSections,
            baseRect,
            leftAnchor,
            rightAnchor,
            RelativePlacement.Left,
            freeArea.minX,
            freeArea.maxX,
            freeArea.minY,
            freeArea.maxY,
            gap,
            occupied,
            planned);
        TryPlaceVerticalSectionStackWithFallback(
            context,
            rightSections,
            baseRect,
            rightAnchor,
            leftAnchor,
            RelativePlacement.Right,
            freeArea.minX,
            freeArea.maxX,
            freeArea.minY,
            freeArea.maxY,
            gap,
            occupied,
            planned);

        TryPlaceHorizontalSectionStackWithFallback(
            context,
            topSections,
            baseRect,
            top != null ? topRect : baseRect,
            bottom != null ? bottomRect : baseRect,
            RelativePlacement.Top,
            freeArea.minX,
            freeArea.maxX,
            freeArea.minY,
            freeArea.maxY,
            gap,
            occupied,
            planned);
        TryPlaceHorizontalSectionStackWithFallback(
            context,
            bottomSections,
            baseRect,
            bottom != null ? bottomRect : baseRect,
            top != null ? topRect : baseRect,
            RelativePlacement.Bottom,
            freeArea.minX,
            freeArea.maxX,
            freeArea.minY,
            freeArea.maxY,
            gap,
            occupied,
            planned);

        TryPlaceDetailViews(
            context,
            detailRelations,
            freeArea.minX,
            freeArea.maxX,
            freeArea.minY,
            freeArea.maxY,
            gap,
            occupied,
            planned);

        return true;
    }

    private bool TryPlanRelaxedLayout(
        DrawingArrangeContext context,
        NeighborSet neighbors,
        IReadOnlyList<View> leftSections,
        IReadOnlyList<View> rightSections,
        IReadOnlyList<View> topSections,
        IReadOnlyList<View> bottomSections,
        IReadOnlyList<DetailRelation> detailRelations,
        IReadOnlyList<View> secondaryViews,
        out List<PlannedPlacement> planned)
    {
        planned = new List<PlannedPlacement>();

        var baseView = neighbors.BaseView;
        var top = neighbors.TopNeighbor;
        var bottom = neighbors.BottomNeighbor;
        var leftNeighbor = neighbors.SideNeighborLeft;
        var rightNeighbor = neighbors.SideNeighborRight;
        var blocked = NormalizeReservedAreas(context);
        var (freeMinX, freeMaxX, freeMinY, freeMaxY) = ComputeFreeArea(context);
        var baseWidth = DrawingArrangeContextSizing.GetWidth(context, baseView);
        var baseHeight = DrawingArrangeContextSizing.GetHeight(context, baseView);

        var budgets = ComputeZoneBudgets(context, neighbors, leftSections, rightSections, topSections, bottomSections);

        if (!TryPlaceBaseViewWithBudgets(
                blocked,
                freeMinX,
                freeMaxX,
                freeMinY,
                freeMaxY,
                baseWidth,
                baseHeight,
                budgets,
                includeRelaxedCandidates: true,
                out var baseRect))
        {
            TracePlanReject("relaxed", "base", context, planned, null);
            return false;
        }

        planned.Add(new PlannedPlacement(baseView, CenterX(baseRect), CenterY(baseRect)));
        var occupied = new List<ReservedRect>(blocked) { baseRect };

        var deferred = new List<View>(secondaryViews);
        var topRect = new ReservedRect(0, 0, 0, 0);
        var bottomRect = new ReservedRect(0, 0, 0, 0);
        var leftRect = new ReservedRect(0, 0, 0, 0);
        var rightRect = new ReservedRect(0, 0, 0, 0);
        var topPlaced = false;
        var bottomPlaced = false;
        var leftPlaced = false;
        var rightPlaced = false;

        if (top != null)
        {
            if (TryPlaceRelative(context, top, baseRect, freeMinX, freeMaxX, freeMinY, freeMaxY, context.Gap, occupied, RelativePlacement.Top, out var rect)
                || TryFindTopViewAtSheetTop(context, top, freeMinX, freeMaxX, freeMinY, freeMaxY, occupied, out rect))
            {
                planned.Add(new PlannedPlacement(top, CenterX(rect), CenterY(rect)));
                occupied.Add(rect);
                topRect = rect;
                topPlaced = true;
            }
            else
            {
                TracePlanReject("relaxed", "top", context, planned, null);
                deferred.Add(top);
            }
        }

        if (bottom != null)
        {
            if (TryPlaceRelative(context, bottom, baseRect, freeMinX, freeMaxX, freeMinY, freeMaxY, context.Gap, occupied, RelativePlacement.Bottom, out var rect))
            {
                planned.Add(new PlannedPlacement(bottom, CenterX(rect), CenterY(rect)));
                occupied.Add(rect);
                bottomRect = rect;
                bottomPlaced = true;
            }
            else
            {
                TracePlanReject("relaxed", "bottom", context, planned, null);
                deferred.Add(bottom);
            }
        }

        if (leftNeighbor != null)
        {
            if (TryPlaceRelative(context, leftNeighbor, baseRect, freeMinX, freeMaxX, freeMinY, freeMaxY, context.Gap, occupied, RelativePlacement.Left, out var rect))
            {
                planned.Add(new PlannedPlacement(leftNeighbor, CenterX(rect), CenterY(rect)));
                occupied.Add(rect);
                leftRect = rect;
                leftPlaced = true;
            }
            else
            {
                TracePlanReject("relaxed", "left", context, planned, null);
                deferred.Add(leftNeighbor);
            }
        }

        if (rightNeighbor != null)
        {
            if (TryPlaceRelative(context, rightNeighbor, baseRect, freeMinX, freeMaxX, freeMinY, freeMaxY, context.Gap, occupied, RelativePlacement.Right, out var rect))
            {
                planned.Add(new PlannedPlacement(rightNeighbor, CenterX(rect), CenterY(rect)));
                occupied.Add(rect);
                rightRect = rect;
                rightPlaced = true;
            }
            else
            {
                TracePlanReject("relaxed", "right", context, planned, null);
                deferred.Add(rightNeighbor);
            }
        }

        var leftPlacedAnchor = leftPlaced ? leftRect : baseRect;
        var rightPlacedAnchor = rightPlaced ? rightRect : baseRect;
        TryPlaceVerticalSectionStackWithFallback(
            context,
            leftSections,
            baseRect,
            leftPlacedAnchor,
            rightPlacedAnchor,
            RelativePlacement.Left,
            freeMinX,
            freeMaxX,
            freeMinY,
            freeMaxY,
            context.Gap,
            occupied,
            planned);
        TryPlaceVerticalSectionStackWithFallback(
            context,
            rightSections,
            baseRect,
            rightPlacedAnchor,
            leftPlacedAnchor,
            RelativePlacement.Right,
            freeMinX,
            freeMaxX,
            freeMinY,
            freeMaxY,
            context.Gap,
            occupied,
            planned);

        TryPlaceHorizontalSectionStackWithFallback(
            context,
            topSections,
            baseRect,
            topPlaced ? topRect : baseRect,
            bottomPlaced ? bottomRect : baseRect,
            RelativePlacement.Top,
            freeMinX,
            freeMaxX,
            freeMinY,
            freeMaxY,
            context.Gap,
            occupied,
            planned);
        TryPlaceHorizontalSectionStackWithFallback(
            context,
            bottomSections,
            baseRect,
            bottomPlaced ? bottomRect : baseRect,
            topPlaced ? topRect : baseRect,
            RelativePlacement.Bottom,
            freeMinX,
            freeMaxX,
            freeMinY,
            freeMaxY,
            context.Gap,
            occupied,
            planned);

        TryPlaceDetailViews(
            context,
            detailRelations,
            freeMinX,
            freeMaxX,
            freeMinY,
            freeMaxY,
            context.Gap,
            occupied,
            planned);

        return true;
    }

    private static void TryPlaceDetailViews(
        DrawingArrangeContext context,
        IReadOnlyList<DetailRelation> detailRelations,
        double freeMinX,
        double freeMaxX,
        double freeMinY,
        double freeMaxY,
        double gap,
        List<ReservedRect> occupied,
        List<PlannedPlacement> planned)
    {
        if (detailRelations.Count == 0)
            return;

        var reservedCount = System.Math.Max(0, occupied.Count - planned.Count);
        var plannedById = planned.ToDictionary(
            item => item.View.GetIdentifier().ID,
            item =>
            {
                var width = DrawingArrangeContextSizing.GetWidth(context, item.View);
                var height = DrawingArrangeContextSizing.GetHeight(context, item.View);
                DrawingViewFrameGeometry.TryGetBoundingRectAtOrigin(item.View, item.X, item.Y, width, height, out var rect);
                return rect;
            });
        var blockedRects = new List<ReservedRect>(occupied.Take(reservedCount));
        foreach (var item in planned)
        {
            if (plannedById.TryGetValue(item.View.GetIdentifier().ID, out var plannedRect))
                blockedRects.Add(plannedRect);
        }

        foreach (var relation in detailRelations)
        {
            if (!plannedById.TryGetValue(relation.OwnerView.GetIdentifier().ID, out var ownerRect))
                continue;

            var detailWidth = DrawingArrangeContextSizing.GetWidth(context, relation.DetailView);
            var detailHeight = DrawingArrangeContextSizing.GetHeight(context, relation.DetailView);
            var offset = gap * 2.0;
            if (!TryFindDetailRect(
                    ownerRect,
                    detailWidth,
                    detailHeight,
                    offset,
                    freeMinX,
                    freeMaxX,
                    freeMinY,
                    freeMaxY,
                    blockedRects,
                    relation.AnchorX,
                    relation.AnchorY,
                    out var candidateRect))
                continue;

            planned.Add(new PlannedPlacement(relation.DetailView, CenterX(candidateRect), CenterY(candidateRect)));
            blockedRects.Add(candidateRect);
            occupied.Add(candidateRect);
            plannedById[relation.DetailView.GetIdentifier().ID] = candidateRect;
        }
    }

    internal static bool TryFindDetailRect(
        ReservedRect ownerRect,
        double detailWidth,
        double detailHeight,
        double offset,
        double freeMinX,
        double freeMaxX,
        double freeMinY,
        double freeMaxY,
        IReadOnlyList<ReservedRect> occupied,
        double? anchorX,
        double? anchorY,
        out ReservedRect bestRect)
    {
        var ownerCenterX = CenterX(ownerRect);
        var ownerCenterY = CenterY(ownerRect);
        var effectiveAnchorX = anchorX ?? ownerCenterX;
        var effectiveAnchorY = anchorY ?? ownerCenterY;
        var preferRight = effectiveAnchorX >= ownerCenterX;
        var preferTop = effectiveAnchorY >= ownerCenterY;
        var preferredCenterX = preferRight
            ? ownerRect.MaxX + offset + detailWidth * 0.5
            : ownerRect.MinX - offset - detailWidth * 0.5;
        var preferredCenterY = preferTop
            ? ownerRect.MaxY + offset + detailHeight * 0.5
            : ownerRect.MinY - offset - detailHeight * 0.5;

        var xCandidates = new HashSet<double>
        {
            freeMinX,
            ownerRect.MinX - offset - detailWidth,
            ownerRect.MaxX + offset,
            ownerCenterX - detailWidth * 0.5
        };
        var yCandidates = new HashSet<double>
        {
            freeMinY,
            ownerRect.MinY - offset - detailHeight,
            ownerRect.MaxY + offset,
            ownerCenterY - detailHeight * 0.5
        };

        // Anchor-driven candidates: ensure positions centred on the anchor are
        // explicitly considered, not just ranked among accidentally nearby ones.
        if (anchorX.HasValue)
        {
            xCandidates.Add(anchorX.Value - detailWidth * 0.5);
            xCandidates.Add(anchorX.Value);
            xCandidates.Add(anchorX.Value - detailWidth);
        }
        if (anchorY.HasValue)
        {
            yCandidates.Add(anchorY.Value - detailHeight * 0.5);
            yCandidates.Add(anchorY.Value);
            yCandidates.Add(anchorY.Value - detailHeight);
        }

        foreach (var rect in occupied)
        {
            xCandidates.Add(rect.MinX - offset - detailWidth);
            xCandidates.Add(rect.MaxX + offset);
            yCandidates.Add(rect.MinY - offset - detailHeight);
            yCandidates.Add(rect.MaxY + offset);
        }

        var bestScore = double.MaxValue;
        bestRect = default;
        foreach (var minX in xCandidates)
        {
            foreach (var minY in yCandidates)
            {
                var rect = new ReservedRect(minX, minY, minX + detailWidth, minY + detailHeight);
                if (!IsWithinArea(rect, freeMinX, freeMaxX, freeMinY, freeMaxY))
                    continue;

                if (IntersectsAny(rect, occupied))
                    continue;

                var centerX = CenterX(rect);
                var centerY = CenterY(rect);
                var anchorDistance = System.Math.Abs(centerX - effectiveAnchorX) + System.Math.Abs(centerY - effectiveAnchorY);
                var preferredDistance = System.Math.Abs(centerX - preferredCenterX) + System.Math.Abs(centerY - preferredCenterY);
                var score = (anchorDistance * 10.0) + preferredDistance;
                if (score >= bestScore)
                    continue;

                bestScore = score;
                bestRect = rect;
            }
        }

        return bestScore < double.MaxValue;
    }

    private static double ComputeVerticalStackHeight(
        DrawingArrangeContext context,
        IReadOnlyList<View> views,
        double gap)
    {
        if (views.Count == 0)
            return 0;

        return views.Sum(view => DrawingArrangeContextSizing.GetHeight(context, view)) + (views.Count * gap);
    }

    private static double ComputeHorizontalStackWidth(
        DrawingArrangeContext context,
        IReadOnlyList<View> views,
        double gap)
    {
        if (views.Count == 0)
            return 0;

        return views.Sum(view => DrawingArrangeContextSizing.GetWidth(context, view)) + (views.Count * gap);
    }

    private static ZoneBudgets ComputeZoneBudgets(
        DrawingArrangeContext context,
        NeighborSet neighbors,
        IReadOnlyList<View> leftSections,
        IReadOnlyList<View> rightSections,
        IReadOnlyList<View> topSections,
        IReadOnlyList<View> bottomSections)
    {
        var gap = context.Gap;
        var top = neighbors.TopNeighbor;
        var bottom = neighbors.BottomNeighbor;
        var leftNeighbor = neighbors.SideNeighborLeft;
        var rightNeighbor = neighbors.SideNeighborRight;

        return new ZoneBudgets(
            topHeight: (top != null ? DrawingArrangeContextSizing.GetHeight(context, top) + gap : 0)
                + ComputeVerticalStackHeight(context, topSections, gap),
            bottomHeight: (bottom != null ? DrawingArrangeContextSizing.GetHeight(context, bottom) + gap : 0)
                + ComputeVerticalStackHeight(context, bottomSections, gap),
            leftWidth: (leftNeighbor != null ? DrawingArrangeContextSizing.GetWidth(context, leftNeighbor) + gap : 0)
                + ComputeHorizontalStackWidth(context, leftSections, gap),
            rightWidth: (rightNeighbor != null ? DrawingArrangeContextSizing.GetWidth(context, rightNeighbor) + gap : 0)
                + ComputeHorizontalStackWidth(context, rightSections, gap));
    }

    internal static bool TryFindBaseViewWindow(
        double freeMinX,
        double freeMaxX,
        double freeMinY,
        double freeMaxY,
        double baseWidth,
        double baseHeight,
        ZoneBudgets budgets,
        out ReservedRect window)
    {
        var minX = freeMinX + budgets.LeftWidth;
        var maxX = freeMaxX - budgets.RightWidth;
        var minY = freeMinY + budgets.BottomHeight;
        var maxY = freeMaxY - budgets.TopHeight;

        if (maxX - minX < baseWidth || maxY - minY < baseHeight)
        {
            window = new ReservedRect(0, 0, 0, 0);
            return false;
        }

        window = new ReservedRect(minX, minY, maxX, maxY);
        return true;
    }

    private static IEnumerable<(double minX, double maxX, double minY, double maxY)> EnumerateBaseViewWindows(
        double freeMinX,
        double freeMaxX,
        double freeMinY,
        double freeMaxY,
        ZoneBudgets budgets,
        bool includeRelaxedCandidates)
    {
        yield return (
            freeMinX + budgets.LeftWidth,
            freeMaxX - budgets.RightWidth,
            freeMinY + budgets.BottomHeight,
            freeMaxY - budgets.TopHeight);

        if (!includeRelaxedCandidates)
            yield break;

        yield return (
            freeMinX + budgets.LeftWidth,
            freeMaxX - budgets.RightWidth,
            freeMinY,
            freeMaxY);
        yield return (
            freeMinX,
            freeMaxX,
            freeMinY + budgets.BottomHeight,
            freeMaxY - budgets.TopHeight);
        yield return (
            freeMinX,
            freeMaxX,
            freeMinY,
            freeMaxY);
    }

    private static bool TryPlaceBaseViewWithBudgets(
        IReadOnlyList<ReservedRect> blocked,
        double freeMinX,
        double freeMaxX,
        double freeMinY,
        double freeMaxY,
        double baseWidth,
        double baseHeight,
        ZoneBudgets budgets,
        bool includeRelaxedCandidates,
        out ReservedRect baseRect)
    {
        foreach (var (minX, maxX, minY, maxY) in EnumerateBaseViewWindows(
                     freeMinX,
                     freeMaxX,
                     freeMinY,
                     freeMaxY,
                     budgets,
                     includeRelaxedCandidates))
        {
            if (maxX - minX < baseWidth || maxY - minY < baseHeight)
                continue;

            if (TryFindBaseViewRectInWindow(
                    blocked,
                    new ReservedRect(minX, minY, maxX, maxY),
                    baseWidth,
                    baseHeight,
                    out baseRect))
                return true;
        }

        baseRect = new ReservedRect(0, 0, 0, 0);
        return false;
    }

    private static bool TryFindBaseViewRectInWindow(
        IReadOnlyList<ReservedRect> blocked,
        ReservedRect window,
        double baseWidth,
        double baseHeight,
        out ReservedRect baseRect)
    {
        if (window.MaxX - window.MinX < baseWidth || window.MaxY - window.MinY < baseHeight)
        {
            baseRect = new ReservedRect(0, 0, 0, 0);
            return false;
        }

        var blockedRectangles = new List<PackedRectangle>();
        foreach (var rect in blocked)
        {
            if (!TryClipToWindow(rect, window, out var clipped))
                continue;

            blockedRectangles.Add(ToBlockedRectangle(window.MinX, window.MaxY, clipped));
        }

        var packer = new MaxRectsBinPacker(
            window.MaxX - window.MinX,
            window.MaxY - window.MinY,
            allowRotation: false,
            blockedRectangles);

        var targetCenterX = (window.MaxX - window.MinX) / 2.0;
        var targetCenterY = (window.MaxY - window.MinY) / 2.0;
        if (!packer.TryInsertClosestToPoint(baseWidth, baseHeight, targetCenterX, targetCenterY, out var placement))
        {
            baseRect = new ReservedRect(0, 0, 0, 0);
            return false;
        }

        baseRect = FromPackedRectangle(window.MinX, window.MaxY, placement);
        return true;
    }

    private static bool TryPlaceVerticalSectionStack(
        DrawingArrangeContext context,
        IReadOnlyList<View> sectionViews,
        ReservedRect frontRect,
        ReservedRect anchorRect,
        RelativePlacement zone,
        double freeMinX,
        double freeMaxX,
        double freeMinY,
        double freeMaxY,
        double gap,
        List<ReservedRect> occupied,
        List<PlannedPlacement> planned,
        SectionPlacementSide preferredPlacementSide,
        SectionPlacementSide actualPlacementSide)
    {
        if (sectionViews.Count == 0)
            return true;

        var orderedSections = sectionViews
            .OrderByDescending(view => DrawingArrangeContextSizing.GetWidth(context, view) * DrawingArrangeContextSizing.GetHeight(context, view))
            .ThenBy(view => view.GetIdentifier().ID)
            .ToList();

        var proposed = new List<(View View, ReservedRect Rect)>(orderedSections.Count);
        var currentAnchor = anchorRect;
        foreach (var section in orderedSections)
        {
            var width = DrawingArrangeContextSizing.GetWidth(context, section);
            var height = DrawingArrangeContextSizing.GetHeight(context, section);
            var minY = CenterY(frontRect) - height / 2.0;
            if (minY < freeMinY || minY + height > freeMaxY)
            {
                PerfTrace.Write(
                    "api-view",
                    "section_stack_reject",
                    0,
                    $"axis=vertical zone={zone} section={section.GetIdentifier().ID} reason=out-of-bounds-y rectY={minY:F2}..{(minY + height):F2} freeY={freeMinY:F2}..{freeMaxY:F2}");
                return false;
            }

            ReservedRect rect;
            if (zone == RelativePlacement.Right)
            {
                var minX = currentAnchor.MaxX + gap;
                rect = new ReservedRect(minX, minY, minX + width, minY + height);
            }
            else
            {
                var maxX = currentAnchor.MinX - gap;
                rect = new ReservedRect(maxX - width, minY, maxX, minY + height);
            }

            if (!IsWithinArea(rect, freeMinX, freeMaxX, freeMinY, freeMaxY) || IntersectsAny(rect, occupied) || proposed.Any(item => Intersects(item.Rect, rect)))
            {
                var reason = !IsWithinArea(rect, freeMinX, freeMaxX, freeMinY, freeMaxY)
                    ? "out-of-bounds"
                    : IntersectsAny(rect, occupied)
                        ? "occupied-intersection"
                        : "proposed-intersection";
                PerfTrace.Write(
                    "api-view",
                    "section_stack_reject",
                    0,
                    $"axis=vertical zone={zone} section={section.GetIdentifier().ID} reason={reason} rect=({rect.MinX:F2},{rect.MinY:F2},{rect.MaxX:F2},{rect.MaxY:F2}) free=({freeMinX:F2},{freeMinY:F2},{freeMaxX:F2},{freeMaxY:F2})");
                return false;
            }

            proposed.Add((section, rect));
            currentAnchor = rect;
        }

        foreach (var item in proposed)
        {
            planned.Add(new PlannedPlacement(item.View, CenterX(item.Rect), CenterY(item.Rect), preferredPlacementSide, actualPlacementSide));
            occupied.Add(item.Rect);
        }

        return true;
    }

    private static bool TryPlaceHorizontalSectionStack(
        DrawingArrangeContext context,
        IReadOnlyList<View> horizontalSections,
        ReservedRect frontRect,
        ReservedRect anchorRect,
        RelativePlacement zone,
        double freeMinX,
        double freeMaxX,
        double freeMinY,
        double freeMaxY,
        double gap,
        List<ReservedRect> occupied,
        List<PlannedPlacement> planned,
        SectionPlacementSide preferredPlacementSide,
        SectionPlacementSide actualPlacementSide)
    {
        if (horizontalSections.Count == 0)
            return true;

        var orderedSections = horizontalSections
            .OrderByDescending(view => DrawingArrangeContextSizing.GetWidth(context, view) * DrawingArrangeContextSizing.GetHeight(context, view))
            .ThenBy(view => view.GetIdentifier().ID)
            .ToList();

        var proposed = new List<(View View, ReservedRect Rect)>(orderedSections.Count);
        var currentAnchor = anchorRect;
        foreach (var section in orderedSections)
        {
            var width = DrawingArrangeContextSizing.GetWidth(context, section);
            var height = DrawingArrangeContextSizing.GetHeight(context, section);
            var preferredMinX = CenterX(frontRect) - width / 2.0;

            // Compute Y bounds first (zone-dependent), then find a valid X position.
            double minY, maxY;
            if (zone == RelativePlacement.Top)
            {
                minY = currentAnchor.MaxY + gap;
                maxY = minY + height;
            }
            else
            {
                maxY = currentAnchor.MinY - gap;
                minY = maxY - height;
            }

            // Try preferred (centered) X, then shift right, then shift left to clear obstacles.
            ReservedRect rect;
            double minX = preferredMinX;
            bool found = false;
            foreach (var candidateMinX in new[] { preferredMinX, freeMinX, freeMaxX - width })
            {
                minX = candidateMinX;

                // Push right past any blocking occupied areas whose X overlaps would obstruct.
                foreach (var blocked in occupied)
                {
                    if (blocked.MinY < maxY && blocked.MaxY > minY  // Y overlap
                        && blocked.MaxX > minX && blocked.MinX < minX + width) // X overlap
                    {
                        if (blocked.MaxX > minX && blocked.MaxX < minX + width)
                            minX = System.Math.Max(minX, blocked.MaxX); // push right past blocker
                    }
                }

                if (minX < freeMinX || minX + width > freeMaxX)
                    continue;

                rect = new ReservedRect(minX, minY, minX + width, maxY);
                if (IsWithinArea(rect, freeMinX, freeMaxX, freeMinY, freeMaxY)
                    && !IntersectsAny(rect, occupied)
                    && !proposed.Any(item => Intersects(item.Rect, rect)))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                var preferredRect = new ReservedRect(preferredMinX, minY, preferredMinX + width, maxY);
                var hasActualRect = DrawingViewFrameGeometry.TryGetBoundingRect(section, out var actualRect);
                var blockers = occupied
                    .Where(blocked =>
                        blocked.MinY < maxY &&
                        blocked.MaxY > minY &&
                        blocked.MaxX > preferredMinX &&
                        blocked.MinX < preferredMinX + width)
                    .Select(blocked =>
                        $"[{blocked.MinX:F2},{blocked.MinY:F2},{blocked.MaxX:F2},{blocked.MaxY:F2}]")
                    .ToList();
                PerfTrace.Write(
                    "api-view",
                    "section_stack_reject",
                    0,
                    $"axis=horizontal zone={zone} section={section.GetIdentifier().ID} reason=no-valid-x y={minY:F2}..{maxY:F2} preferredRect=[{preferredRect.MinX:F2},{preferredRect.MinY:F2},{preferredRect.MaxX:F2},{preferredRect.MaxY:F2}] actualRect={(hasActualRect ? $"[{actualRect.MinX:F2},{actualRect.MinY:F2},{actualRect.MaxX:F2},{actualRect.MaxY:F2}]" : "n/a")} size={width:F2}x{height:F2} free=({freeMinX:F2},{freeMinY:F2},{freeMaxX:F2},{freeMaxY:F2}) occupied={occupied.Count} blockers={string.Join(";", blockers)}");
                PerfTrace.Write(
                    "api-view",
                    "section_stack_snapshot",
                    0,
                    $"axis=horizontal zone={zone} section={section.GetIdentifier().ID} candidate=[{preferredRect.MinX:F2},{preferredRect.MinY:F2},{preferredRect.MaxX:F2},{preferredRect.MaxY:F2}] planned=[{FormatPlannedRects(context, planned)}]");

                if (ShouldDebugStopOnSectionReject())
                {
                    throw new System.InvalidOperationException(
                        $"Debug stop: horizontal section reject section={section.GetIdentifier().ID} zone={zone} preferredRect=[{preferredRect.MinX:F2},{preferredRect.MinY:F2},{preferredRect.MaxX:F2},{preferredRect.MaxY:F2}] actualRect={(hasActualRect ? $"[{actualRect.MinX:F2},{actualRect.MinY:F2},{actualRect.MaxX:F2},{actualRect.MaxY:F2}]" : "n/a")}");
                }

                return false;
            }

            rect = new ReservedRect(minX, minY, minX + width, maxY);

            proposed.Add((section, rect));
            currentAnchor = rect;
        }

        foreach (var item in proposed)
        {
            planned.Add(new PlannedPlacement(item.View, CenterX(item.Rect), CenterY(item.Rect), preferredPlacementSide, actualPlacementSide));
            occupied.Add(item.Rect);
        }

        return true;
    }

    private static bool ShouldDebugStopOnSectionReject()
    {
        var raw = System.Environment.GetEnvironmentVariable("SVMCP_FIT_DEBUG_STOP_ON_SECTION_REJECT");
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        return raw.Equals("1", System.StringComparison.OrdinalIgnoreCase)
            || raw.Equals("true", System.StringComparison.OrdinalIgnoreCase)
            || raw.Equals("on", System.StringComparison.OrdinalIgnoreCase)
            || raw.Equals("yes", System.StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatPlannedRects(DrawingArrangeContext context, IReadOnlyList<PlannedPlacement> planned)
    {
        return string.Join(";",
            planned.Select(item =>
            {
                var width = DrawingArrangeContextSizing.GetWidth(context, item.View);
                var height = DrawingArrangeContextSizing.GetHeight(context, item.View);
                DrawingViewFrameGeometry.TryGetBoundingRectAtOrigin(item.View, item.X, item.Y, width, height, out var rect);
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

    private static bool TryPlaceHorizontalSectionStackWithFallback(
        DrawingArrangeContext context,
        IReadOnlyList<View> sectionViews,
        ReservedRect frontRect,
        ReservedRect preferredAnchorRect,
        ReservedRect fallbackAnchorRect,
        RelativePlacement preferredZone,
        double freeMinX,
        double freeMaxX,
        double freeMinY,
        double freeMaxY,
        double gap,
        List<ReservedRect> occupied,
        List<PlannedPlacement> planned)
    {
        if (sectionViews.Count == 0)
            return true;

        PerfTrace.Write(
            "api-view",
            "section_stack_attempt",
            0,
            $"axis=horizontal preferred={preferredZone} sections=[{string.Join(",", sectionViews.Select(v => v.GetIdentifier().ID))}]");

        if (TryPlaceHorizontalSectionStack(
                context,
                sectionViews,
                frontRect,
                preferredAnchorRect,
                preferredZone,
                freeMinX,
                freeMaxX,
                freeMinY,
                freeMaxY,
                gap,
                occupied,
                planned,
                ToPlacementSide(preferredZone),
                ToPlacementSide(preferredZone)))
        {
            PerfTrace.Write(
                "api-view",
                "section_stack_result",
                0,
                $"axis=horizontal preferred={preferredZone} actual={preferredZone} fallbackUsed=0 sections=[{string.Join(",", sectionViews.Select(v => v.GetIdentifier().ID))}]");
            return true;
        }

        var fallbackZone = preferredZone == RelativePlacement.Top
            ? RelativePlacement.Bottom
            : RelativePlacement.Top;

        var fallbackPlaced = TryPlaceHorizontalSectionStack(
            context,
            sectionViews,
            frontRect,
            fallbackAnchorRect,
            fallbackZone,
            freeMinX,
            freeMaxX,
            freeMinY,
            freeMaxY,
            gap,
            occupied,
            planned,
            ToPlacementSide(preferredZone),
            ToPlacementSide(fallbackZone));

        PerfTrace.Write(
            "api-view",
            "section_stack_result",
            0,
            $"axis=horizontal preferred={preferredZone} actual={(fallbackPlaced ? fallbackZone.ToString() : "none")} fallbackUsed={(fallbackPlaced ? 1 : 0)} sections=[{string.Join(",", sectionViews.Select(v => v.GetIdentifier().ID))}]");

        return fallbackPlaced;
    }

    private static bool TryPlaceVerticalSectionStackWithFallback(
        DrawingArrangeContext context,
        IReadOnlyList<View> sectionViews,
        ReservedRect frontRect,
        ReservedRect preferredAnchorRect,
        ReservedRect fallbackAnchorRect,
        RelativePlacement preferredZone,
        double freeMinX,
        double freeMaxX,
        double freeMinY,
        double freeMaxY,
        double gap,
        List<ReservedRect> occupied,
        List<PlannedPlacement> planned)
    {
        if (sectionViews.Count == 0)
            return true;

        PerfTrace.Write(
            "api-view",
            "section_stack_attempt",
            0,
            $"axis=vertical preferred={preferredZone} sections=[{string.Join(",", sectionViews.Select(v => v.GetIdentifier().ID))}]");

        if (TryPlaceVerticalSectionStack(
                context,
                sectionViews,
                frontRect,
                preferredAnchorRect,
                preferredZone,
                freeMinX,
                freeMaxX,
                freeMinY,
                freeMaxY,
                gap,
                occupied,
                planned,
                ToPlacementSide(preferredZone),
                ToPlacementSide(preferredZone)))
        {
            PerfTrace.Write(
                "api-view",
                "section_stack_result",
                0,
                $"axis=vertical preferred={preferredZone} actual={preferredZone} fallbackUsed=0 sections=[{string.Join(",", sectionViews.Select(v => v.GetIdentifier().ID))}]");
            return true;
        }

        var fallbackZone = preferredZone == RelativePlacement.Left
            ? RelativePlacement.Right
            : RelativePlacement.Left;

        var fallbackPlaced = TryPlaceVerticalSectionStack(
            context,
            sectionViews,
            frontRect,
            fallbackAnchorRect,
            fallbackZone,
            freeMinX,
            freeMaxX,
            freeMinY,
            freeMaxY,
            gap,
            occupied,
            planned,
            ToPlacementSide(preferredZone),
            ToPlacementSide(fallbackZone));

        PerfTrace.Write(
            "api-view",
            "section_stack_result",
            0,
            $"axis=vertical preferred={preferredZone} actual={(fallbackPlaced ? fallbackZone.ToString() : "none")} fallbackUsed={(fallbackPlaced ? 1 : 0)} sections=[{string.Join(",", sectionViews.Select(v => v.GetIdentifier().ID))}]");

        return fallbackPlaced;
    }

    private static SectionPlacementSide ToPlacementSide(RelativePlacement placement)
        => placement switch
        {
            RelativePlacement.Left => SectionPlacementSide.Left,
            RelativePlacement.Right => SectionPlacementSide.Right,
            RelativePlacement.Top => SectionPlacementSide.Top,
            RelativePlacement.Bottom => SectionPlacementSide.Bottom,
            _ => SectionPlacementSide.Unknown
        };

    private static string ToPlacementSideString(SectionPlacementSide? side)
        => side?.ToString() ?? string.Empty;

    /// <summary>
    /// Tries to place the TopView at the very top of the free area when the standard
    /// above-FrontView slot is unavailable (e.g. FrontView sits high on a tall sheet).
    /// Tries center, left, and right X positions at freeMaxY - height.
    /// </summary>
    private static bool TryFindTopViewAtSheetTop(
        DrawingArrangeContext context,
        View top,
        double freeMinX,
        double freeMaxX,
        double freeMinY,
        double freeMaxY,
        IReadOnlyList<ReservedRect> occupied,
        out ReservedRect placement)
    {
        var width  = DrawingArrangeContextSizing.GetWidth(context, top);
        var height = DrawingArrangeContextSizing.GetHeight(context, top);
        var y = freeMaxY - height;
        if (y < freeMinY || freeMaxX - freeMinX < width)
        {
            placement = new ReservedRect(0, 0, 0, 0);
            return false;
        }

        var maxX = freeMaxX - width;
        var cx = freeMinX + (freeMaxX - freeMinX - width) / 2.0;
        var candidates = new[]
        {
            new ReservedRect(System.Math.Min(cx,  maxX), y, System.Math.Min(cx,  maxX) + width, freeMaxY),
            new ReservedRect(freeMinX,                   y, freeMinX + width,                   freeMaxY),
            new ReservedRect(System.Math.Max(freeMinX, maxX), y, System.Math.Max(freeMinX, maxX) + width, freeMaxY),
        };

        foreach (var c in candidates)
        {
            if (!IntersectsAny(c, occupied))
            {
                placement = c;
                return true;
            }
        }

        placement = new ReservedRect(0, 0, 0, 0);
        return false;
    }

    private static void PackSecondaryViewsPartial(
        DrawingArrangeContext context,
        IReadOnlyList<View> views,
        IReadOnlyList<ReservedRect> occupied,
        List<(View View, double X, double Y)> planned)
    {
        if (views.Count == 0)
            return;

        var (freeMinX, freeMaxX, freeMinY, freeMaxY) = ComputeFreeArea(context);
        var availableW = freeMaxX - freeMinX;
        var availableH = freeMaxY - freeMinY;
        if (availableW <= 0 || availableH <= 0)
            return;

        var blocked = occupied
            .Select(rect => ToBlockedRectangle(freeMinX, freeMaxY, rect))
            .ToList();

        var packer = new MaxRectsBinPacker(
            availableW + context.Gap,
            availableH + context.Gap,
            allowRotation: false,
            blockedRectangles: blocked);

        foreach (var view in views.OrderByDescending(v => DrawingArrangeContextSizing.GetWidth(context, v) * DrawingArrangeContextSizing.GetHeight(context, v)))
        {
            var width = DrawingArrangeContextSizing.GetWidth(context, view);
            var height = DrawingArrangeContextSizing.GetHeight(context, view);
            if (!packer.TryInsert(width + context.Gap, height + context.Gap, MaxRectsHeuristic.BestAreaFit, out var placement))
            {
                // Can't place this view — leave it at its current position.
                PerfTrace.Write("api-view", "front_arrange_secondary_skip", 0,
                    $"view={view.GetIdentifier().ID} w={width:F1} h={height:F1}");
                continue;
            }

            planned.Add((
                view,
                freeMinX + placement.X + width / 2.0,
                freeMaxY - placement.Y - height / 2.0));
        }
    }

    private static bool TryPlaceRelative(
        DrawingArrangeContext context,
        View view,
        ReservedRect anchor,
        double freeMinX,
        double freeMaxX,
        double freeMinY,
        double freeMaxY,
        double gap,
        IReadOnlyList<ReservedRect> occupied,
        RelativePlacement preferred,
        out ReservedRect placement)
    {
        foreach (var candidate in EnumerateRelativeCandidates(context, view, anchor, freeMinX, freeMaxX, freeMinY, freeMaxY, gap, preferred))
        {
            if (IntersectsAny(candidate, occupied))
                continue;

            placement = candidate;
            return true;
        }

        placement = new ReservedRect(0, 0, 0, 0);
        return false;
    }

    private static IEnumerable<ReservedRect> EnumerateRelativeCandidates(
        DrawingArrangeContext context,
        View view,
        ReservedRect anchor,
        double freeMinX,
        double freeMaxX,
        double freeMinY,
        double freeMaxY,
        double gap,
        RelativePlacement placement)
    {
        var width = DrawingArrangeContextSizing.GetWidth(context, view);
        var height = DrawingArrangeContextSizing.GetHeight(context, view);
        var maxX = freeMaxX - width;
        var maxY = freeMaxY - height;

        static double Clamp(double value, double min, double max)
            => value < min ? min : (value > max ? max : value);

        IEnumerable<ReservedRect> BuildTop()
        {
            var y = anchor.MaxY + gap;
            if (y > maxY)
                yield break;
            var xs = new[]
            {
                Clamp(CenterX(anchor) - width / 2.0, freeMinX, maxX),
                Clamp(anchor.MinX, freeMinX, maxX),
                Clamp(anchor.MaxX - width, freeMinX, maxX)
            };

            foreach (var x in xs.Distinct())
                yield return new ReservedRect(x, y, x + width, y + height);
        }

        IEnumerable<ReservedRect> BuildBottom()
        {
            var y = anchor.MinY - gap - height;
            if (y < freeMinY)
                yield break;
            var xs = new[]
            {
                Clamp(CenterX(anchor) - width / 2.0, freeMinX, maxX),
                Clamp(anchor.MinX, freeMinX, maxX),
                Clamp(anchor.MaxX - width, freeMinX, maxX)
            };

            foreach (var x in xs.Distinct())
                yield return new ReservedRect(x, y, x + width, y + height);
        }

        IEnumerable<ReservedRect> BuildRight()
        {
            var x = anchor.MaxX + gap;
            if (x > maxX)
                yield break;
            var ys = new[]
            {
                Clamp(CenterY(anchor) - height / 2.0, freeMinY, maxY),
                Clamp(anchor.MinY, freeMinY, maxY),
                Clamp(anchor.MaxY - height, freeMinY, maxY)
            };

            foreach (var y in ys.Distinct())
                yield return new ReservedRect(x, y, x + width, y + height);
        }

        IEnumerable<ReservedRect> BuildLeft()
        {
            var x = anchor.MinX - gap - width;
            if (x < freeMinX)
                yield break;
            var ys = new[]
            {
                Clamp(CenterY(anchor) - height / 2.0, freeMinY, maxY),
                Clamp(anchor.MinY, freeMinY, maxY),
                Clamp(anchor.MaxY - height, freeMinY, maxY)
            };

            foreach (var y in ys.Distinct())
                yield return new ReservedRect(x, y, x + width, y + height);
        }

        return placement switch
        {
            RelativePlacement.Top => BuildTop().Concat(BuildRight()).Concat(BuildLeft()).Concat(BuildBottom()),
            RelativePlacement.Right => BuildRight().Concat(BuildTop()).Concat(BuildBottom()).Concat(BuildLeft()),
            RelativePlacement.Bottom => BuildBottom().Concat(BuildLeft()).Concat(BuildRight()).Concat(BuildTop()),
            RelativePlacement.Left => BuildLeft().Concat(BuildTop()).Concat(BuildBottom()).Concat(BuildRight()),
            _ => System.Array.Empty<ReservedRect>()
        };
    }

    /// <summary>
    /// Tries to place a view of the given size within [minX..maxX, minY..maxY] without
    /// overlapping any blocked area. Tries 9 positions (center, right, left) × (center, top, bottom)
    /// in order of visual preference, returning the first non-overlapping placement.
    /// </summary>
    internal static bool TryCreateCenteredRect(
        double width,
        double height,
        double minX,
        double maxX,
        double minY,
        double maxY,
        out ReservedRect placement)
    {
        if (width <= 0 || height <= 0)
        {
            placement = new ReservedRect(0, 0, 0, 0);
            return false;
        }

        var availableW = maxX - minX;
        var availableH = maxY - minY;
        if (availableW < width || availableH < height)
        {
            placement = new ReservedRect(0, 0, 0, 0);
            return false;
        }

        var x = minX + (availableW - width) / 2.0;
        var y = minY + (availableH - height) / 2.0;
        placement = new ReservedRect(x, y, x + width, y + height);
        return true;
    }

    private static bool IntersectsAny(ReservedRect rect, IReadOnlyList<ReservedRect> others)
        => others.Any(other => Intersects(rect, other));

    private static bool Intersects(ReservedRect a, ReservedRect b)
        => a.MinX < b.MaxX
           && a.MaxX > b.MinX
           && a.MinY < b.MaxY
           && a.MaxY > b.MinY;

    private static bool IsWithinArea(ReservedRect rect, double minX, double maxX, double minY, double maxY)
        => rect.MinX >= minX
           && rect.MaxX <= maxX
           && rect.MinY >= minY
           && rect.MaxY <= maxY;

    private static List<ReservedRect> NormalizeReservedAreas(DrawingArrangeContext context)
    {
        var normalized = new List<ReservedRect>(context.ReservedAreas.Count);
        foreach (var area in context.ReservedAreas)
        {
            var minX = System.Math.Max(context.Margin, area.MinX - context.Gap);
            var maxX = System.Math.Min(context.SheetWidth - context.Margin, area.MaxX + context.Gap);
            var minY = System.Math.Max(context.Margin, area.MinY - context.Gap);
            var maxY = System.Math.Min(context.SheetHeight - context.Margin, area.MaxY + context.Gap);

            if (maxX <= minX || maxY <= minY)
                continue;

            normalized.Add(new ReservedRect(minX, minY, maxX, maxY));
        }

        return normalized;
    }

    private static bool TryClipToWindow(ReservedRect rect, ReservedRect window, out ReservedRect clipped)
    {
        var minX = System.Math.Max(window.MinX, rect.MinX);
        var minY = System.Math.Max(window.MinY, rect.MinY);
        var maxX = System.Math.Min(window.MaxX, rect.MaxX);
        var maxY = System.Math.Min(window.MaxY, rect.MaxY);

        if (maxX <= minX || maxY <= minY)
        {
            clipped = new ReservedRect(0, 0, 0, 0);
            return false;
        }

        clipped = new ReservedRect(minX, minY, maxX, maxY);
        return true;
    }

    private static PackedRectangle ToBlockedRectangle(double freeMinX, double freeMaxY, ReservedRect rect)
        => new(
            rect.MinX - freeMinX,
            freeMaxY - rect.MaxY,
            rect.MaxX - rect.MinX,
            rect.MaxY - rect.MinY);

    private static ReservedRect FromPackedRectangle(double freeMinX, double freeMaxY, PackedRectangle rect)
        => new(
            freeMinX + rect.X,
            freeMaxY - rect.Y - rect.Height,
            freeMinX + rect.X + rect.Width,
            freeMaxY - rect.Y);

    private static double CenterX(ReservedRect rect) => (rect.MinX + rect.MaxX) / 2.0;

    private static double CenterY(ReservedRect rect) => (rect.MinY + rect.MaxY) / 2.0;

    private enum RelativePlacement
    {
        Top,
        Right,
        Bottom,
        Left
    }
}
