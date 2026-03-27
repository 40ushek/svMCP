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

    internal sealed class PlannedPlacement
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

    private readonly struct MainSkeletonRect
    {
        public MainSkeletonRect(string role, ReservedRect rect)
        {
            Role = role;
            Rect = rect;
        }

        public string Role { get; }
        public ReservedRect Rect { get; }
    }

    private sealed class MainSkeletonPlacementState
    {
        public ReservedRect? TopRect { get; private set; }
        public ReservedRect? BottomRect { get; private set; }
        public ReservedRect? LeftRect { get; private set; }
        public ReservedRect? RightRect { get; private set; }

        public void SetPlaced(string role, ReservedRect rect)
        {
            switch (role)
            {
                case "top":
                    TopRect = rect;
                    break;
                case "bottom":
                    BottomRect = rect;
                    break;
                case "left":
                    LeftRect = rect;
                    break;
                case "right":
                    RightRect = rect;
                    break;
            }
        }

        public void Clear(string role)
        {
            switch (role)
            {
                case "top":
                    TopRect = null;
                    break;
                case "bottom":
                    BottomRect = null;
                    break;
                case "left":
                    LeftRect = null;
                    break;
                case "right":
                    RightRect = null;
                    break;
            }
        }

        public ReservedRect? GetPlacedRect(string role)
            => role switch
            {
                "top" => TopRect,
                "bottom" => BottomRect,
                "left" => LeftRect,
                "right" => RightRect,
                _ => null
            };

        public bool TryGetPlacedRect(string role, out ReservedRect rect)
        {
            var placedRect = GetPlacedRect(role);
            if (placedRect != null)
            {
                rect = placedRect;
                return true;
            }

            rect = new ReservedRect(0, 0, 0, 0);
            return false;
        }

        public ReservedRect GetAnchorOrBase(string role, ReservedRect baseRect)
            => GetPlacedRect(role) ?? baseRect;
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
            if (!_maxRectsFallback.EstimateFit(unplannedCtx, unplannedFrames))
                return false;

            var fallbackStandardSectionIds = CollectUnplannedStandardSectionIds(planningContext, plannedIds);
            return fallbackStandardSectionIds.Count == 0
                || ValidateResidualFallbackLayout(unplannedCtx, fallbackStandardSectionIds);
        }

        var maxr  = _maxRectsFallback.EstimateFit(planningContext, frames);
        var shelf = !maxr && planningContext.ReservedAreas.Count == 0 && _fallback.EstimateFit(planningContext, frames);
        return maxr || shelf;
    }

    private HashSet<int> CollectUnplannedStandardSectionIds(
        DrawingArrangeContext context,
        HashSet<int> plannedIds)
    {
        var baseViewSelection = BaseViewSelection.Select(context.Views);
        var baseView = baseViewSelection.View;
        if (baseView == null)
            return new HashSet<int>();

        var semanticViews = SemanticViewSet.Build(context.Views);
        var sectionGroups = SectionGroupSet.Build(
            semanticViews.Sections,
            context.Drawing,
            baseView,
            _sectionPlacementSideResolver);

        return sectionGroups.Left
            .Concat(sectionGroups.Right)
            .Concat(sectionGroups.Top)
            .Concat(sectionGroups.Bottom)
            .Select(view => view.GetIdentifier().ID)
            .Where(id => !plannedIds.Contains(id))
            .ToHashSet();
    }

    private static bool ValidateResidualFallbackLayout(
        DrawingArrangeContext context,
        HashSet<int> fallbackSectionIds)
    {
        if (fallbackSectionIds.Count == 0)
            return true;

        if (!TryEstimateResidualPlacements(context, out var residualRectsById))
            return false;

        foreach (var entry in residualRectsById)
        {
            if (!fallbackSectionIds.Contains(entry.Key))
                continue;

            var rect = entry.Value;
            if (!IsWithinArea(
                    rect,
                    context.Margin,
                    context.SheetWidth - context.Margin,
                    context.Margin,
                    context.SheetHeight - context.Margin))
            {
                PerfTrace.Write("api-view", "fallback_layout_reject", 0,
                    $"view={entry.Key} reason=out-of-sheet rect=[{rect.MinX:F2},{rect.MinY:F2},{rect.MaxX:F2},{rect.MaxY:F2}]");
                return false;
            }

            if (IntersectsAny(rect, context.ReservedAreas))
            {
                PerfTrace.Write("api-view", "fallback_layout_reject", 0,
                    $"view={entry.Key} reason=reserved-overlap rect=[{rect.MinX:F2},{rect.MinY:F2},{rect.MaxX:F2},{rect.MaxY:F2}]");
                return false;
            }

            foreach (var other in residualRectsById)
            {
                if (other.Key == entry.Key)
                    continue;

                if (!Intersects(rect, other.Value))
                    continue;

                PerfTrace.Write("api-view", "fallback_layout_reject", 0,
                    $"view={entry.Key} reason=view-overlap blocker={other.Key} rect=[{rect.MinX:F2},{rect.MinY:F2},{rect.MaxX:F2},{rect.MaxY:F2}] blockerRect=[{other.Value.MinX:F2},{other.Value.MinY:F2},{other.Value.MaxX:F2},{other.Value.MaxY:F2}]");
                return false;
            }
        }

        return true;
    }

    private static bool TryEstimateResidualPlacements(
        DrawingArrangeContext context,
        out Dictionary<int, ReservedRect> residualRectsById)
    {
        residualRectsById = new Dictionary<int, ReservedRect>();

        var availableW = context.SheetWidth - (2 * context.Margin);
        var availableH = context.SheetHeight - (2 * context.Margin);
        if (availableW <= 0 || availableH <= 0)
            return false;

        var packer = new MaxRectsBinPacker(
            availableW + context.Gap,
            availableH + context.Gap,
            allowRotation: false,
            blockedRectangles: ToMaxRectsBlockedRectangles(context));

        var orderedViews = context.Views
            .OrderByDescending(view => view.Width * view.Height)
            .ToList();

        foreach (var view in orderedViews)
        {
            if (!packer.TryInsert(view.Width + context.Gap, view.Height + context.Gap, MaxRectsHeuristic.BestAreaFit, out var placement))
                return false;

            var originX = context.Margin + placement.X + (view.Width / 2.0);
            var originY = context.SheetHeight - context.Margin - placement.Y - (view.Height / 2.0);
            var width = DrawingArrangeContextSizing.GetWidth(context, view);
            var height = DrawingArrangeContextSizing.GetHeight(context, view);
            if (!DrawingViewFrameGeometry.TryGetBoundingRectAtOrigin(view, originX, originY, width, height, out var rect))
                rect = new ReservedRect(
                    originX - (width / 2.0),
                    originY - (height / 2.0),
                    originX + (width / 2.0),
                    originY + (height / 2.0));

            residualRectsById[view.GetIdentifier().ID] = rect;
        }

        return true;
    }

    private static IEnumerable<PackedRectangle> ToMaxRectsBlockedRectangles(DrawingArrangeContext context)
    {
        foreach (var area in context.ReservedAreas)
        {
            var minX = System.Math.Max(context.Margin, area.MinX - context.Gap);
            var maxX = System.Math.Min(context.SheetWidth - context.Margin, area.MaxX + context.Gap);
            var minY = System.Math.Max(context.Margin, area.MinY - context.Gap);
            var maxY = System.Math.Min(context.SheetHeight - context.Margin, area.MaxY + context.Gap);

            if (maxX <= minX || maxY <= minY)
                continue;

            yield return new PackedRectangle(
                minX - context.Margin,
                (context.SheetHeight - context.Margin) - maxY,
                maxX - minX,
                maxY - minY);
        }
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
                    context.Gap,
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
        var mainSkeleton = new MainSkeletonPlacementState();

        TryPlaceOptionalDiagnosticMainSkeletonNeighbor(
            conflicts,
            context,
            "top",
            top,
            baseRect,
            freeMinX,
            freeMaxX,
            freeMinY,
            freeMaxY,
            occupied,
            RelativePlacement.Top,
            allowSheetTopFallback: true,
            mainSkeleton);

        TryPlaceOptionalDiagnosticMainSkeletonNeighbor(
            conflicts,
            context,
            "bottom",
            bottom,
            baseRect,
            freeMinX,
            freeMaxX,
            freeMinY,
            freeMaxY,
            occupied,
            RelativePlacement.Bottom,
            allowSheetTopFallback: false,
            mainSkeleton);

        TryPlaceOptionalDiagnosticMainSkeletonNeighbor(
            conflicts,
            context,
            "left",
            leftNeighbor,
            baseRect,
            freeMinX,
            freeMaxX,
            freeMinY,
            freeMaxY,
            occupied,
            RelativePlacement.Left,
            allowSheetTopFallback: false,
            mainSkeleton);

        TryPlaceOptionalDiagnosticMainSkeletonNeighbor(
            conflicts,
            context,
            "right",
            rightNeighbor,
            baseRect,
            freeMinX,
            freeMaxX,
            freeMinY,
            freeMaxY,
            occupied,
            RelativePlacement.Right,
            allowSheetTopFallback: false,
            mainSkeleton);

        if (!TryValidateMainSkeletonSpacing(
                baseRect,
                mainSkeleton.TopRect,
                mainSkeleton.BottomRect,
                mainSkeleton.LeftRect,
                mainSkeleton.RightRect,
                context.SheetWidth,
                context.SheetHeight,
                context.Margin,
                context.Gap,
                context.ReservedAreas,
                out var mainSkeletonReason,
                out var mainSkeletonRole,
                out var mainSkeletonRect))
        {
            var conflictedView = ResolveMainSkeletonView(neighbors, mainSkeletonRole);
            var attemptedZone = ToAttemptedZone(mainSkeletonRole);
            AddConflict(conflicts, conflictedView, attemptedZone, mainSkeletonReason);
            EnsureBoundingRect(conflicts, conflictedView, attemptedZone, mainSkeletonRect);
            return;
        }

        DiagnoseStackPlacementFailureWithFallback(
            conflicts,
            context,
            leftSections,
            baseRect,
            mainSkeleton.GetAnchorOrBase("left", baseRect),
            mainSkeleton.GetAnchorOrBase("right", baseRect),
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
            mainSkeleton.GetAnchorOrBase("right", baseRect),
            mainSkeleton.GetAnchorOrBase("left", baseRect),
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
            mainSkeleton.GetAnchorOrBase("top", baseRect),
            mainSkeleton.GetAnchorOrBase("bottom", baseRect),
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
            mainSkeleton.GetAnchorOrBase("bottom", baseRect),
            mainSkeleton.GetAnchorOrBase("top", baseRect),
            RelativePlacement.Bottom,
            freeMinX,
            freeMaxX,
            freeMinY,
            freeMaxY,
            context.Gap,
            occupied);
    }

    internal static bool TryValidateMainSkeletonSpacing(
        ReservedRect baseRect,
        ReservedRect? topRect,
        ReservedRect? bottomRect,
        ReservedRect? leftRect,
        ReservedRect? rightRect,
        double sheetWidth,
        double sheetHeight,
        double margin,
        double gap,
        IReadOnlyList<ReservedRect> reservedAreas,
        out string reason,
        out string role,
        out ReservedRect failingRect)
    {
        reason = string.Empty;
        role = string.Empty;
        failingRect = baseRect;

        var placements = new List<MainSkeletonRect>
        {
            new("base", baseRect)
        };

        if (topRect != null)
            placements.Add(new MainSkeletonRect("top", topRect));
        if (bottomRect != null)
            placements.Add(new MainSkeletonRect("bottom", bottomRect));
        if (leftRect != null)
            placements.Add(new MainSkeletonRect("left", leftRect));
        if (rightRect != null)
            placements.Add(new MainSkeletonRect("right", rightRect));

        foreach (var placement in placements)
        {
            if (!IsWithinArea(placement.Rect, margin, sheetWidth - margin, margin, sheetHeight - margin))
            {
                reason = $"main-skeleton-out-of-sheet-{placement.Role}";
                role = placement.Role;
                failingRect = placement.Rect;
                return false;
            }

            if (IntersectsAny(placement.Rect, reservedAreas))
            {
                reason = $"main-skeleton-reserved-overlap-{placement.Role}";
                role = placement.Role;
                failingRect = placement.Rect;
                return false;
            }
        }

        for (var i = 0; i < placements.Count; i++)
        {
            for (var j = i + 1; j < placements.Count; j++)
            {
                if (!Intersects(placements[i].Rect, placements[j].Rect))
                    continue;

                reason = $"main-skeleton-overlap-{placements[j].Role}";
                role = placements[j].Role;
                failingRect = placements[j].Rect;
                return false;
            }
        }

        if (topRect != null && topRect.MinY - baseRect.MaxY < gap)
        {
            reason = "main-skeleton-gap-top";
            role = "top";
            failingRect = topRect;
            return false;
        }

        if (bottomRect != null && baseRect.MinY - bottomRect.MaxY < gap)
        {
            reason = "main-skeleton-gap-bottom";
            role = "bottom";
            failingRect = bottomRect;
            return false;
        }

        if (leftRect != null && baseRect.MinX - leftRect.MaxX < gap)
        {
            reason = "main-skeleton-gap-left";
            role = "left";
            failingRect = leftRect;
            return false;
        }

        if (rightRect != null && rightRect.MinX - baseRect.MaxX < gap)
        {
            reason = "main-skeleton-gap-right";
            role = "right";
            failingRect = rightRect;
            return false;
        }

        return true;
    }

    private static View ResolveMainSkeletonView(NeighborSet neighbors, string role)
        => role switch
        {
            "top" => neighbors.TopNeighbor ?? neighbors.BaseView,
            "bottom" => neighbors.BottomNeighbor ?? neighbors.BaseView,
            "left" => neighbors.SideNeighborLeft ?? neighbors.BaseView,
            "right" => neighbors.SideNeighborRight ?? neighbors.BaseView,
            _ => neighbors.BaseView
        };

    private static string ToAttemptedZone(string role)
        => role switch
        {
            "top" => RelativePlacement.Top.ToString(),
            "bottom" => RelativePlacement.Bottom.ToString(),
            "left" => RelativePlacement.Left.ToString(),
            "right" => RelativePlacement.Right.ToString(),
            _ => "Center"
        };

    internal static bool TryDeferMainSkeletonNeighbor(
        string role,
        string reason,
        View? top,
        View? bottom,
        View? leftNeighbor,
        View? rightNeighbor,
        ref bool topPlaced,
        ref bool bottomPlaced,
        ref bool leftPlaced,
        ref bool rightPlaced,
        ref ReservedRect topRect,
        ref ReservedRect bottomRect,
        ref ReservedRect leftRect,
        ref ReservedRect rightRect,
        List<ReservedRect> occupied,
        List<PlannedPlacement> planned)
    {
        var placements = new MainSkeletonPlacementState();
        if (topPlaced)
            placements.SetPlaced("top", topRect);
        if (bottomPlaced)
            placements.SetPlaced("bottom", bottomRect);
        if (leftPlaced)
            placements.SetPlaced("left", leftRect);
        if (rightPlaced)
            placements.SetPlaced("right", rightRect);

        var deferred = TryDeferMainSkeletonNeighborCore(
            role,
            reason,
            top,
            bottom,
            leftNeighbor,
            rightNeighbor,
            placements,
            occupied,
            planned);

        topPlaced = placements.TopRect != null;
        bottomPlaced = placements.BottomRect != null;
        leftPlaced = placements.LeftRect != null;
        rightPlaced = placements.RightRect != null;
        topRect = placements.TopRect ?? new ReservedRect(0, 0, 0, 0);
        bottomRect = placements.BottomRect ?? new ReservedRect(0, 0, 0, 0);
        leftRect = placements.LeftRect ?? new ReservedRect(0, 0, 0, 0);
        rightRect = placements.RightRect ?? new ReservedRect(0, 0, 0, 0);
        return deferred;
    }

    private static bool TryDeferMainSkeletonNeighborCore(
        string role,
        string reason,
        View? top,
        View? bottom,
        View? leftNeighbor,
        View? rightNeighbor,
        MainSkeletonPlacementState placements,
        List<ReservedRect> occupied,
        List<PlannedPlacement> planned)
    {
        void RemovePlacement(View view, ReservedRect rect)
        {
            planned.RemoveAll(item => ReferenceEquals(item.View, view));
            occupied.Remove(rect);
        }

        switch (role)
        {
            case "top" when top != null && placements.TryGetPlacedRect("top", out var topRect):
                RemovePlacement(top, topRect);
                placements.Clear("top");
                PerfTrace.Write("api-view", "main_skeleton_defer", 0, $"role=top reason={reason}");
                return true;

            case "bottom" when bottom != null && placements.TryGetPlacedRect("bottom", out var bottomRect):
                RemovePlacement(bottom, bottomRect);
                placements.Clear("bottom");
                PerfTrace.Write("api-view", "main_skeleton_defer", 0, $"role=bottom reason={reason}");
                return true;

            case "left" when leftNeighbor != null && placements.TryGetPlacedRect("left", out var leftRect):
                RemovePlacement(leftNeighbor, leftRect);
                placements.Clear("left");
                PerfTrace.Write("api-view", "main_skeleton_defer", 0, $"role=left reason={reason}");
                return true;

            case "right" when rightNeighbor != null && placements.TryGetPlacedRect("right", out var rightRect):
                RemovePlacement(rightNeighbor, rightRect);
                placements.Clear("right");
                PerfTrace.Write("api-view", "main_skeleton_defer", 0, $"role=right reason={reason}");
                return true;

            default:
                return false;
        }
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

        var preferredPlacementSide = ToPlacementSide(preferredZone);
        if (TryProbeSectionStackWithFallback(
                preferredZone,
                zone => TryPlaceVerticalSectionStack(
                    context,
                    sectionViews,
                    frontRect,
                    zone == preferredZone ? preferredAnchorRect : fallbackAnchorRect,
                    zone,
                    freeMinX,
                    freeMaxX,
                    freeMinY,
                    freeMaxY,
                    gap,
                    occupied.ToList(),
                    new List<PlannedPlacement>(),
                    preferredPlacementSide,
                    zone == preferredZone ? preferredPlacementSide : GetFallbackPlacementSide(preferredPlacementSide)),
                out _,
                out _))
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

        var preferredPlacementSide = ToPlacementSide(preferredZone);
        if (TryProbeSectionStackWithFallback(
                preferredZone,
                zone => TryPlaceHorizontalSectionStack(
                    context,
                    sectionViews,
                    frontRect,
                    zone == preferredZone ? preferredAnchorRect : fallbackAnchorRect,
                    zone,
                    freeMinX,
                    freeMaxX,
                    freeMinY,
                    freeMaxY,
                    gap,
                    occupied.ToList(),
                    new List<PlannedPlacement>(),
                    preferredPlacementSide,
                    zone == preferredZone ? preferredPlacementSide : GetFallbackPlacementSide(preferredPlacementSide)),
                out _,
                out _))
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
        foreach (var section in OrderSectionViewsForStack(context, sectionViews))
        {
            var width = DrawingArrangeContextSizing.GetWidth(context, section);
            var height = DrawingArrangeContextSizing.GetHeight(context, section);
            if (!TryCreateVerticalSectionRect(frontRect, currentAnchor, zone, width, height, gap, freeMinY, freeMaxY, out var rect))
            {
                AddConflict(conflicts, section, zone.ToString(), "outside_zone_bounds");
                return;
            }

            if (!TryValidateSectionCandidateRect(rect, freeMinX, freeMaxX, freeMinY, freeMaxY, occupied, proposed, out var reason))
            {
                AddSectionCandidateConflict(conflicts, section, zone, rect, reason, occupied);
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
        foreach (var section in OrderSectionViewsForStack(context, sectionViews))
        {
            var width = DrawingArrangeContextSizing.GetWidth(context, section);
            var height = DrawingArrangeContextSizing.GetHeight(context, section);
            if (!TryGetHorizontalSectionPlacementInputs(
                    frontRect,
                    currentAnchor,
                    zone,
                    width,
                    height,
                    gap,
                    freeMinX,
                    freeMaxX,
                    out var preferredMinX,
                    out var minY,
                    out var maxY))
            {
                AddConflict(conflicts, section, zone.ToString(), "outside_zone_bounds");
                return;
            }

            var rect = new ReservedRect(preferredMinX, minY, preferredMinX + width, maxY);

            if (!TryValidateSectionCandidateRect(rect, freeMinX, freeMaxX, freeMinY, freeMaxY, occupied, proposed, out var reason))
            {
                AddSectionCandidateConflict(conflicts, section, zone, rect, reason, occupied);
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
                gap,
                out var baseRect))
        {
            TracePlanReject("strict", "base", context, planned, null);
            return false;
        }

        AddPlannedRect(planned, baseView, baseRect);

        var occupied = new List<ReservedRect>(blocked) { baseRect };
        var mainSkeleton = new MainSkeletonPlacementState();

        if (!TryPlaceOptionalStrictMainSkeletonNeighbor(
                context,
                "top",
                top,
                RelativePlacement.Top,
                baseRect,
                topWidth,
                topHeight,
                gap,
                freeArea.minX,
                freeArea.maxX,
                freeArea.minY,
                freeArea.maxY,
                occupied,
                planned,
                mainSkeleton))
            return false;

        if (!TryPlaceOptionalStrictMainSkeletonNeighbor(
                context,
                "bottom",
                bottom,
                RelativePlacement.Bottom,
                baseRect,
                bottomWidth,
                bottomHeight,
                gap,
                freeArea.minX,
                freeArea.maxX,
                freeArea.minY,
                freeArea.maxY,
                occupied,
                planned,
                mainSkeleton))
            return false;

        if (!TryPlaceOptionalStrictMainSkeletonNeighbor(
                context,
                "left",
                leftNeighbor,
                RelativePlacement.Left,
                baseRect,
                leftNeighborWidth,
                leftNeighborHeight,
                gap,
                freeArea.minX,
                freeArea.maxX,
                freeArea.minY,
                freeArea.maxY,
                occupied,
                planned,
                mainSkeleton))
            return false;

        if (!TryPlaceOptionalStrictMainSkeletonNeighbor(
                context,
                "right",
                rightNeighbor,
                RelativePlacement.Right,
                baseRect,
                rightNeighborWidth,
                rightNeighborHeight,
                gap,
                freeArea.minX,
                freeArea.maxX,
                freeArea.minY,
                freeArea.maxY,
                occupied,
                planned,
                mainSkeleton))
            return false;

        if (!TryValidateMainSkeletonSpacing(
                baseRect,
                mainSkeleton.TopRect,
                mainSkeleton.BottomRect,
                mainSkeleton.LeftRect,
                mainSkeleton.RightRect,
                context.SheetWidth,
                context.SheetHeight,
                context.Margin,
                context.Gap,
                context.ReservedAreas,
                out var mainSkeletonReason,
                out _,
                out var mainSkeletonRect))
        {
            TracePlanReject("strict", mainSkeletonReason, context, planned, mainSkeletonRect);
            return false;
        }

        var leftAnchor = mainSkeleton.GetAnchorOrBase("left", baseRect);
        var rightAnchor = mainSkeleton.GetAnchorOrBase("right", baseRect);
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
            mainSkeleton.GetAnchorOrBase("top", baseRect),
            mainSkeleton.GetAnchorOrBase("bottom", baseRect),
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
            mainSkeleton.GetAnchorOrBase("bottom", baseRect),
            mainSkeleton.GetAnchorOrBase("top", baseRect),
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
                context.Gap,
                includeRelaxedCandidates: true,
                out var baseRect))
        {
            TracePlanReject("relaxed", "base", context, planned, null);
            return false;
        }

        AddPlannedRect(planned, baseView, baseRect);
        var occupied = new List<ReservedRect>(blocked) { baseRect };

        var deferred = new List<View>(secondaryViews);
        var mainSkeleton = new MainSkeletonPlacementState();
        var deferredMainSkeletonRoles = new List<string>();

        TryPlaceOptionalRelaxedMainSkeletonNeighbor(
            context,
            "top",
            top,
            baseRect,
            freeMinX,
            freeMaxX,
            freeMinY,
            freeMaxY,
            occupied,
            planned,
            deferred,
            RelativePlacement.Top,
            allowSheetTopFallback: true,
            mainSkeleton);

        TryPlaceOptionalRelaxedMainSkeletonNeighbor(
            context,
            "bottom",
            bottom,
            baseRect,
            freeMinX,
            freeMaxX,
            freeMinY,
            freeMaxY,
            occupied,
            planned,
            deferred,
            RelativePlacement.Bottom,
            allowSheetTopFallback: false,
            mainSkeleton);

        TryPlaceOptionalRelaxedMainSkeletonNeighbor(
            context,
            "left",
            leftNeighbor,
            baseRect,
            freeMinX,
            freeMaxX,
            freeMinY,
            freeMaxY,
            occupied,
            planned,
            deferred,
            RelativePlacement.Left,
            allowSheetTopFallback: false,
            mainSkeleton);

        TryPlaceOptionalRelaxedMainSkeletonNeighbor(
            context,
            "right",
            rightNeighbor,
            baseRect,
            freeMinX,
            freeMaxX,
            freeMinY,
            freeMaxY,
            occupied,
            planned,
            deferred,
            RelativePlacement.Right,
            allowSheetTopFallback: false,
            mainSkeleton);

        while (!TryValidateMainSkeletonSpacing(
                   baseRect,
                   mainSkeleton.TopRect,
                   mainSkeleton.BottomRect,
                   mainSkeleton.LeftRect,
                   mainSkeleton.RightRect,
                   context.SheetWidth,
                   context.SheetHeight,
                   context.Margin,
                   context.Gap,
                   context.ReservedAreas,
                   out var mainSkeletonReason,
                   out var mainSkeletonRole,
                   out var mainSkeletonRect))
        {
            if (!TryDeferMainSkeletonNeighborCore(
                    mainSkeletonRole,
                    mainSkeletonReason,
                    top,
                    bottom,
                    leftNeighbor,
                    rightNeighbor,
                    mainSkeleton,
                    occupied,
                    planned))
            {
                TracePlanReject("relaxed", mainSkeletonReason, context, planned, mainSkeletonRect);
                return false;
            }

            deferredMainSkeletonRoles.Add(mainSkeletonRole);
        }

        if (deferredMainSkeletonRoles.Count > 0)
        {
            PerfTrace.Write(
                "api-view",
                "main_skeleton_relaxed_resolved",
                0,
                $"deferrals={deferredMainSkeletonRoles.Count} roles=[{string.Join(",", deferredMainSkeletonRoles)}]");
        }

        var leftPlacedAnchor = mainSkeleton.GetAnchorOrBase("left", baseRect);
        var rightPlacedAnchor = mainSkeleton.GetAnchorOrBase("right", baseRect);
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
            mainSkeleton.GetAnchorOrBase("top", baseRect),
            mainSkeleton.GetAnchorOrBase("bottom", baseRect),
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
            mainSkeleton.GetAnchorOrBase("bottom", baseRect),
            mainSkeleton.GetAnchorOrBase("top", baseRect),
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

            AddPlannedAndOccupiedRect(planned, occupied, relation.DetailView, candidateRect);
            blockedRects.Add(candidateRect);
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
        double gap,
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
                    gap,
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
        double gap,
        out ReservedRect baseRect)
    {
        var inset = gap > 0 ? gap : 0.0;
        var searchWindow = new ReservedRect(
            window.MinX + inset,
            window.MinY + inset,
            window.MaxX - inset,
            window.MaxY - inset);

        if (searchWindow.MaxX - searchWindow.MinX < baseWidth || searchWindow.MaxY - searchWindow.MinY < baseHeight)
        {
            baseRect = new ReservedRect(0, 0, 0, 0);
            return false;
        }

        var blockedRectangles = new List<PackedRectangle>();
        foreach (var rect in blocked)
        {
            if (!TryClipToWindow(rect, searchWindow, out var clipped))
                continue;

            blockedRectangles.Add(ToBlockedRectangle(searchWindow.MinX, searchWindow.MaxY, clipped));
        }

        var packer = new MaxRectsBinPacker(
            searchWindow.MaxX - searchWindow.MinX,
            searchWindow.MaxY - searchWindow.MinY,
            allowRotation: false,
            blockedRectangles);

        var targetCenterX = (searchWindow.MaxX - searchWindow.MinX) / 2.0;
        var targetCenterY = (searchWindow.MaxY - searchWindow.MinY) / 2.0;
        if (!packer.TryInsertClosestToPoint(baseWidth, baseHeight, targetCenterX, targetCenterY, out var placement))
        {
            baseRect = new ReservedRect(0, 0, 0, 0);
            return false;
        }

        baseRect = FromPackedRectangle(searchWindow.MinX, searchWindow.MaxY, placement);
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

        var orderedSections = OrderSectionViewsForStack(context, sectionViews);

        var proposed = new List<(View View, ReservedRect Rect)>(orderedSections.Count);
        var currentAnchor = anchorRect;
        foreach (var section in orderedSections)
        {
            var width = DrawingArrangeContextSizing.GetWidth(context, section);
            var height = DrawingArrangeContextSizing.GetHeight(context, section);
            if (!TryCreateVerticalSectionRect(frontRect, currentAnchor, zone, width, height, gap, freeMinY, freeMaxY, out var rect))
            {
                PerfTrace.Write(
                    "api-view",
                    "section_stack_reject",
                    0,
                    $"axis=vertical zone={zone} section={section.GetIdentifier().ID} reason=out-of-bounds-y height={height:F2} freeY={freeMinY:F2}..{freeMaxY:F2}");
                return false;
            }

            if (!TryValidateSectionCandidateRect(
                    rect,
                    freeMinX,
                    freeMaxX,
                    freeMinY,
                    freeMaxY,
                    occupied,
                    proposed.Select(item => item.Rect),
                    out var reason))
            {
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

        CommitSectionPlacements(proposed, planned, occupied, preferredPlacementSide, actualPlacementSide);

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

        var orderedSections = OrderSectionViewsForStack(context, horizontalSections);

        var proposed = new List<(View View, ReservedRect Rect)>(orderedSections.Count);
        var currentAnchor = anchorRect;
        foreach (var section in orderedSections)
        {
            var width = DrawingArrangeContextSizing.GetWidth(context, section);
            var height = DrawingArrangeContextSizing.GetHeight(context, section);
            if (!TryGetHorizontalSectionPlacementInputs(
                    frontRect,
                    currentAnchor,
                    zone,
                    width,
                    height,
                    gap,
                    freeMinX,
                    freeMaxX,
                    out var preferredMinX,
                    out var minY,
                    out var maxY))
            {
                PerfTrace.Write(
                    "api-view",
                    "section_stack_reject",
                    0,
                    $"axis=horizontal zone={zone} section={section.GetIdentifier().ID} reason=out-of-bounds-x size={width:F2}x{height:F2} freeX={freeMinX:F2}..{freeMaxX:F2}");
                return false;
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
                if (TryValidateSectionCandidateRect(
                        rect,
                        freeMinX,
                        freeMaxX,
                        freeMinY,
                        freeMaxY,
                        occupied,
                        proposed.Select(item => item.Rect),
                        out _))
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

        CommitSectionPlacements(proposed, planned, occupied, preferredPlacementSide, actualPlacementSide);

        return true;
    }

    private static List<View> OrderSectionViewsForStack(
        DrawingArrangeContext context,
        IReadOnlyList<View> sectionViews)
    {
        return sectionViews
            .OrderByDescending(view => DrawingArrangeContextSizing.GetWidth(context, view) * DrawingArrangeContextSizing.GetHeight(context, view))
            .ThenBy(view => view.GetIdentifier().ID)
            .ToList();
    }

    private static void CommitSectionPlacements(
        IReadOnlyList<(View View, ReservedRect Rect)> proposed,
        List<PlannedPlacement> planned,
        List<ReservedRect> occupied,
        SectionPlacementSide preferredPlacementSide,
        SectionPlacementSide actualPlacementSide)
    {
        foreach (var item in proposed)
        {
            AddPlannedAndOccupiedRect(planned, occupied, item.View, item.Rect, preferredPlacementSide, actualPlacementSide);
        }
    }

    private static void AddPlannedRect(
        List<PlannedPlacement> planned,
        View view,
        ReservedRect rect,
        SectionPlacementSide? preferredPlacementSide = null,
        SectionPlacementSide? actualPlacementSide = null)
    {
        planned.Add(new PlannedPlacement(view, CenterX(rect), CenterY(rect), preferredPlacementSide, actualPlacementSide));
    }

    private static void AddPlannedAndOccupiedRect(
        List<PlannedPlacement> planned,
        List<ReservedRect> occupied,
        View view,
        ReservedRect rect,
        SectionPlacementSide? preferredPlacementSide = null,
        SectionPlacementSide? actualPlacementSide = null)
    {
        AddPlannedRect(planned, view, rect, preferredPlacementSide, actualPlacementSide);
        occupied.Add(rect);
    }

    private static void CommitMainSkeletonPlacement(
        MainSkeletonPlacementState placements,
        string role,
        List<ReservedRect> occupied,
        ReservedRect rect)
    {
        placements.SetPlaced(role, rect);
        occupied.Add(rect);
    }

    private static void CommitPlannedMainSkeletonPlacement(
        MainSkeletonPlacementState placements,
        string role,
        List<PlannedPlacement> planned,
        List<ReservedRect> occupied,
        View view,
        ReservedRect rect)
    {
        AddPlannedAndOccupiedRect(planned, occupied, view, rect);
        placements.SetPlaced(role, rect);
    }

    private static string FormatSectionIds(IReadOnlyList<View> sectionViews)
        => string.Join(",", sectionViews.Select(v => v.GetIdentifier().ID));

    private static bool TryValidateSectionCandidateRect(
        ReservedRect rect,
        double freeMinX,
        double freeMaxX,
        double freeMinY,
        double freeMaxY,
        IReadOnlyList<ReservedRect> occupied,
        IEnumerable<ReservedRect> proposed,
        out string reason)
    {
        if (!IsWithinArea(rect, freeMinX, freeMaxX, freeMinY, freeMaxY))
        {
            reason = "out-of-bounds";
            return false;
        }

        if (IntersectsAny(rect, occupied))
        {
            reason = "occupied-intersection";
            return false;
        }

        if (proposed.Any(item => Intersects(item, rect)))
        {
            reason = "proposed-intersection";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static void AddSectionCandidateConflict(
        List<DrawingFitConflict> conflicts,
        View section,
        RelativePlacement zone,
        ReservedRect rect,
        string reason,
        IReadOnlyList<ReservedRect> occupied)
    {
        if (reason == "occupied-intersection")
        {
            AddIntersectionConflicts(conflicts, section, zone.ToString(), rect, occupied);
            return;
        }

        AddConflict(
            conflicts,
            section,
            zone.ToString(),
            reason == "out-of-bounds" ? "outside_zone_bounds" : "intersects_view");
    }

    private static void TraceSectionStackAttempt(string axis, RelativePlacement preferredZone, IReadOnlyList<View> sectionViews)
    {
        PerfTrace.Write(
            "api-view",
            "section_stack_attempt",
            0,
            $"axis={axis} preferred={preferredZone} sections=[{FormatSectionIds(sectionViews)}]");
    }

    private static void TraceSectionStackResult(
        string axis,
        RelativePlacement preferredZone,
        RelativePlacement? actualZone,
        bool fallbackUsed,
        IReadOnlyList<View> sectionViews)
    {
        PerfTrace.Write(
            "api-view",
            "section_stack_result",
            0,
            $"axis={axis} preferred={preferredZone} actual={(actualZone?.ToString() ?? "none")} fallbackUsed={(fallbackUsed ? 1 : 0)} sections=[{FormatSectionIds(sectionViews)}]");
    }

    private static bool TryProbeSectionStackWithFallback(
        RelativePlacement preferredZone,
        System.Func<RelativePlacement, bool> tryPlace,
        out RelativePlacement? actualZone,
        out bool fallbackUsed)
    {
        if (tryPlace(preferredZone))
        {
            actualZone = preferredZone;
            fallbackUsed = false;
            return true;
        }

        var fallbackZone = GetFallbackZone(preferredZone);
        if (tryPlace(fallbackZone))
        {
            actualZone = fallbackZone;
            fallbackUsed = true;
            return true;
        }

        actualZone = null;
        fallbackUsed = false;
        return false;
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

        TraceSectionStackAttempt("horizontal", preferredZone, sectionViews);
        var preferredPlacementSide = ToPlacementSide(preferredZone);

        if (TryProbeSectionStackWithFallback(
                preferredZone,
                zone => TryPlaceHorizontalSectionStack(
                    context,
                    sectionViews,
                    frontRect,
                    zone == preferredZone ? preferredAnchorRect : fallbackAnchorRect,
                    zone,
                    freeMinX,
                    freeMaxX,
                    freeMinY,
                    freeMaxY,
                    gap,
                    occupied,
                    planned,
                    preferredPlacementSide,
                    zone == preferredZone ? preferredPlacementSide : GetFallbackPlacementSide(preferredPlacementSide)),
                out var actualZone,
                out var fallbackUsed))
        {
            TraceSectionStackResult("horizontal", preferredZone, actualZone, fallbackUsed, sectionViews);
            return true;
        }

        TraceSectionStackResult("horizontal", preferredZone, actualZone: null, fallbackUsed: false, sectionViews);

        return false;
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

        TraceSectionStackAttempt("vertical", preferredZone, sectionViews);
        var preferredPlacementSide = ToPlacementSide(preferredZone);

        if (TryProbeSectionStackWithFallback(
                preferredZone,
                zone => TryPlaceVerticalSectionStack(
                    context,
                    sectionViews,
                    frontRect,
                    zone == preferredZone ? preferredAnchorRect : fallbackAnchorRect,
                    zone,
                    freeMinX,
                    freeMaxX,
                    freeMinY,
                    freeMaxY,
                    gap,
                    occupied,
                    planned,
                    preferredPlacementSide,
                    zone == preferredZone ? preferredPlacementSide : GetFallbackPlacementSide(preferredPlacementSide)),
                out var actualZone,
                out var fallbackUsed))
        {
            TraceSectionStackResult("vertical", preferredZone, actualZone, fallbackUsed, sectionViews);
            return true;
        }

        TraceSectionStackResult("vertical", preferredZone, actualZone: null, fallbackUsed: false, sectionViews);

        return false;
    }

    private static RelativePlacement GetFallbackZone(RelativePlacement preferredZone)
        => preferredZone switch
        {
            RelativePlacement.Top => RelativePlacement.Bottom,
            RelativePlacement.Bottom => RelativePlacement.Top,
            RelativePlacement.Left => RelativePlacement.Right,
            RelativePlacement.Right => RelativePlacement.Left,
            _ => preferredZone
        };

    private static bool TryGetHorizontalSectionPlacementInputs(
        ReservedRect frontRect,
        ReservedRect anchorRect,
        RelativePlacement zone,
        double width,
        double height,
        double gap,
        double freeMinX,
        double freeMaxX,
        out double preferredMinX,
        out double minY,
        out double maxY)
    {
        preferredMinX = CenterX(frontRect) - width / 2.0;
        if (preferredMinX < freeMinX || preferredMinX + width > freeMaxX)
        {
            minY = 0;
            maxY = 0;
            return false;
        }

        if (zone == RelativePlacement.Top)
        {
            minY = anchorRect.MaxY + gap;
            maxY = minY + height;
            return true;
        }

        if (zone == RelativePlacement.Bottom)
        {
            maxY = anchorRect.MinY - gap;
            minY = maxY - height;
            return true;
        }

        minY = 0;
        maxY = 0;
        return false;
    }

    private static bool TryCreateCenteredRelativeRect(
        ReservedRect anchorRect,
        RelativePlacement placement,
        double width,
        double height,
        double gap,
        out ReservedRect rect)
    {
        rect = placement switch
        {
            RelativePlacement.Top => new ReservedRect(
                CenterX(anchorRect) - width / 2.0,
                anchorRect.MaxY + gap,
                CenterX(anchorRect) + width / 2.0,
                anchorRect.MaxY + gap + height),
            RelativePlacement.Bottom => new ReservedRect(
                CenterX(anchorRect) - width / 2.0,
                anchorRect.MinY - gap - height,
                CenterX(anchorRect) + width / 2.0,
                anchorRect.MinY - gap),
            RelativePlacement.Left => new ReservedRect(
                anchorRect.MinX - gap - width,
                CenterY(anchorRect) - height / 2.0,
                anchorRect.MinX - gap,
                CenterY(anchorRect) + height / 2.0),
            RelativePlacement.Right => new ReservedRect(
                anchorRect.MaxX + gap,
                CenterY(anchorRect) - height / 2.0,
                anchorRect.MaxX + gap + width,
                CenterY(anchorRect) + height / 2.0),
            _ => new ReservedRect(0, 0, 0, 0)
        };

        return placement is RelativePlacement.Top
            or RelativePlacement.Bottom
            or RelativePlacement.Left
            or RelativePlacement.Right;
    }

    private static bool TryFindStrictMainSkeletonNeighborRect(
        RelativePlacement placement,
        ReservedRect baseRect,
        double width,
        double height,
        double gap,
        double freeMinX,
        double freeMaxX,
        double freeMinY,
        double freeMaxY,
        IReadOnlyList<ReservedRect> occupied,
        out ReservedRect rect)
    {
        if (!TryCreateCenteredRelativeRect(baseRect, placement, width, height, gap, out rect))
            return false;

        if (!IsWithinArea(rect, freeMinX, freeMaxX, freeMinY, freeMaxY) || IntersectsAny(rect, occupied))
            return false;

        return true;
    }

    private static bool TryPlaceOptionalStrictMainSkeletonNeighbor(
        DrawingArrangeContext context,
        string role,
        View? view,
        RelativePlacement placement,
        ReservedRect baseRect,
        double width,
        double height,
        double gap,
        double freeMinX,
        double freeMaxX,
        double freeMinY,
        double freeMaxY,
        List<ReservedRect> occupied,
        List<PlannedPlacement> planned,
        MainSkeletonPlacementState placements)
    {
        if (view == null)
        {
            placements.Clear(role);
            return true;
        }

        if (TryFindStrictMainSkeletonNeighborRect(
                placement,
                baseRect,
                width,
                height,
                gap,
                freeMinX,
                freeMaxX,
                freeMinY,
                freeMaxY,
                occupied,
                out var rect))
        {
            CommitPlannedMainSkeletonPlacement(placements, role, planned, occupied, view, rect);
            return true;
        }

        placements.Clear(role);
        TracePlanReject("strict", role, context, planned, rect.Width > 0 || rect.Height > 0 ? rect : null);
        return false;
    }

    private static void TryPlaceOptionalRelaxedMainSkeletonNeighbor(
        DrawingArrangeContext context,
        string role,
        View? view,
        ReservedRect baseRect,
        double freeMinX,
        double freeMaxX,
        double freeMinY,
        double freeMaxY,
        List<ReservedRect> occupied,
        List<PlannedPlacement> planned,
        List<View> deferred,
        RelativePlacement placement,
        bool allowSheetTopFallback,
        MainSkeletonPlacementState placements)
    {
        if (view != null
            && TryFindRelaxedMainSkeletonNeighborRect(
                context,
                view,
                baseRect,
                freeMinX,
                freeMaxX,
                freeMinY,
                freeMaxY,
                occupied,
                placement,
                allowSheetTopFallback,
                out var rect))
        {
            CommitPlannedMainSkeletonPlacement(placements, role, planned, occupied, view, rect);
            return;
        }

        placements.Clear(role);
        if (view == null)
            return;

        TracePlanReject("relaxed", role, context, planned, null);
        deferred.Add(view);
    }

    private static bool TryFindRelaxedMainSkeletonNeighborRect(
        DrawingArrangeContext context,
        View view,
        ReservedRect baseRect,
        double freeMinX,
        double freeMaxX,
        double freeMinY,
        double freeMaxY,
        IReadOnlyList<ReservedRect> occupied,
        RelativePlacement placement,
        bool allowSheetTopFallback,
        out ReservedRect rect)
    {
        if (TryPlaceRelative(
                context,
                view,
                baseRect,
                freeMinX,
                freeMaxX,
                freeMinY,
                freeMaxY,
                context.Gap,
                occupied,
                placement,
                out rect))
        {
            return true;
        }

        if (allowSheetTopFallback
            && placement == RelativePlacement.Top
            && TryFindTopViewAtSheetTop(context, view, freeMinX, freeMaxX, freeMinY, freeMaxY, occupied, out rect))
        {
            return true;
        }

        rect = new ReservedRect(0, 0, 0, 0);
        return false;
    }

    private static void TryPlaceOptionalDiagnosticMainSkeletonNeighbor(
        List<DrawingFitConflict> conflicts,
        DrawingArrangeContext context,
        string role,
        View? view,
        ReservedRect baseRect,
        double freeMinX,
        double freeMaxX,
        double freeMinY,
        double freeMaxY,
        List<ReservedRect> occupied,
        RelativePlacement placement,
        bool allowSheetTopFallback,
        MainSkeletonPlacementState placements)
    {
        if (view != null
            && TryFindRelaxedMainSkeletonNeighborRect(
                context,
                view,
                baseRect,
                freeMinX,
                freeMaxX,
                freeMinY,
                freeMaxY,
                occupied,
                placement,
                allowSheetTopFallback,
                out var rect))
        {
            CommitMainSkeletonPlacement(placements, role, occupied, rect);
            return;
        }

        placements.Clear(role);
        if (view == null)
            return;

        DiagnoseRelativePlacementFailure(conflicts, context, view, baseRect, freeMinX, freeMaxX, freeMinY, freeMaxY, context.Gap, occupied, placement);
    }

    private static bool TryCreateVerticalSectionRect(
        ReservedRect frontRect,
        ReservedRect anchorRect,
        RelativePlacement zone,
        double width,
        double height,
        double gap,
        double freeMinY,
        double freeMaxY,
        out ReservedRect rect)
    {
        var minY = CenterY(frontRect) - height / 2.0;
        if (minY < freeMinY || minY + height > freeMaxY)
        {
            rect = new ReservedRect(0, 0, 0, 0);
            return false;
        }

        if (zone == RelativePlacement.Right)
        {
            var minX = anchorRect.MaxX + gap;
            rect = new ReservedRect(minX, minY, minX + width, minY + height);
            return true;
        }

        if (zone == RelativePlacement.Left)
        {
            var maxX = anchorRect.MinX - gap;
            rect = new ReservedRect(maxX - width, minY, maxX, minY + height);
            return true;
        }

        rect = new ReservedRect(0, 0, 0, 0);
        return false;
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

    internal static SectionPlacementSide GetFallbackPlacementSide(SectionPlacementSide preferredPlacementSide)
        => preferredPlacementSide switch
        {
            SectionPlacementSide.Top => SectionPlacementSide.Bottom,
            SectionPlacementSide.Bottom => SectionPlacementSide.Top,
            SectionPlacementSide.Left => SectionPlacementSide.Right,
            SectionPlacementSide.Right => SectionPlacementSide.Left,
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
        foreach (var candidate in EnumerateAvailableRelativeCandidates(
                     context,
                     view,
                     anchor,
                     freeMinX,
                     freeMaxX,
                     freeMinY,
                     freeMaxY,
                     gap,
                     occupied,
                     preferred))
        {
            placement = candidate;
            return true;
        }

        placement = new ReservedRect(0, 0, 0, 0);
        return false;
    }

    private static IEnumerable<ReservedRect> EnumerateAvailableRelativeCandidates(
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
        foreach (var candidate in EnumerateRelativeCandidates(context, view, anchor, freeMinX, freeMaxX, freeMinY, freeMaxY, gap, preferred))
        {
            if (IntersectsAny(candidate, occupied))
                continue;

            yield return candidate;
        }
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
