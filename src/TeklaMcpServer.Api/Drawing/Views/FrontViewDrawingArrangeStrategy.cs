using System.Collections.Generic;
using System.Linq;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Model;
using TeklaMcpServer.Api.Algorithms.Packing;
using TeklaMcpServer.Api.Diagnostics;

namespace TeklaMcpServer.Api.Drawing;

public sealed class FrontViewDrawingArrangeStrategy : IDrawingViewArrangeStrategy, IDrawingViewArrangeDiagnosticsStrategy
{
    internal const double RelaxedLayoutScaleCutoff = 50.0;

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

    public bool CanArrange(DrawingArrangeContext context)
    {
        var baseView = BaseViewSelection.Select(context.Views).View;
        return baseView?.ViewType == View.ViewTypes.FrontView;
    }

    public bool EstimateFit(DrawingArrangeContext context, IReadOnlyList<(double w, double h)> frames)
    {
        if (TryCreatePlan(context, out var planned))
        {
            // Plan succeeded, but it may leave some views unplanned.
            // EstimateFit must return true only when ALL views can be placed.
            var plannedIds = new System.Collections.Generic.HashSet<int>(
                planned.Select(p => p.View.GetIdentifier().ID));
            var unplannedViews = context.Views
                .Where(v => !plannedIds.Contains(v.GetIdentifier().ID))
                .ToList();

            if (unplannedViews.Count == 0)
                return true;

            // Check if unplanned views can fit in the space left around anchors.
            var anchorRects = planned.Select(p =>
            {
                var w = DrawingArrangeContextSizing.GetWidth(context, p.View);
                var h = DrawingArrangeContextSizing.GetHeight(context, p.View);
                return new ReservedRect(p.X - w / 2.0, p.Y - h / 2.0, p.X + w / 2.0, p.Y + h / 2.0);
            }).ToList();
            var extendedReserved = new System.Collections.Generic.List<ReservedRect>(context.ReservedAreas);
            extendedReserved.AddRange(anchorRects);
            var unplannedCtx = new DrawingArrangeContext(
                context.Drawing, unplannedViews,
                context.SheetWidth, context.SheetHeight,
                context.Margin, context.Gap,
                extendedReserved, context.EffectiveFrameSizes);
            var unplannedFrames = unplannedViews
                .Select(v => (DrawingArrangeContextSizing.GetWidth(unplannedCtx, v), DrawingArrangeContextSizing.GetHeight(unplannedCtx, v)))
                .ToList();
            return _maxRectsFallback.EstimateFit(unplannedCtx, unplannedFrames);
        }

        var maxr  = _maxRectsFallback.EstimateFit(context, frames);
        var shelf = !maxr && context.ReservedAreas.Count == 0 && _fallback.EstimateFit(context, frames);
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
                    return new ReservedRect(p.X - w / 2.0, p.Y - h / 2.0, p.X + w / 2.0, p.Y + h / 2.0);
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

        var baseViewSelection = BaseViewSelection.Select(context.Views);
        var baseView = baseViewSelection.View;
        if (baseView?.ViewType != View.ViewTypes.FrontView)
            return conflicts;

        if (TryCreatePlan(context, out var planned))
        {
            var plannedIds = new System.Collections.Generic.HashSet<int>(
                planned.Select(p => p.View.GetIdentifier().ID));
            var unplannedViews = context.Views
                .Where(v => !plannedIds.Contains(v.GetIdentifier().ID))
                .ToList();

            if (unplannedViews.Count == 0)
                return conflicts;

            var anchorRects = planned.Select(p =>
            {
                var w = DrawingArrangeContextSizing.GetWidth(context, p.View);
                var h = DrawingArrangeContextSizing.GetHeight(context, p.View);
                return new ReservedRect(p.X - w / 2.0, p.Y - h / 2.0, p.X + w / 2.0, p.Y + h / 2.0);
            }).ToList();
            var extendedReserved = new System.Collections.Generic.List<ReservedRect>(context.ReservedAreas);
            extendedReserved.AddRange(anchorRects);
            var unplannedCtx = new DrawingArrangeContext(
                context.Drawing, unplannedViews,
                context.SheetWidth, context.SheetHeight,
                context.Margin, context.Gap,
                extendedReserved, context.EffectiveFrameSizes);
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

        var semanticKinds = context.Views.ToDictionary(
            view => view.GetIdentifier().ID,
            view => ViewSemanticClassifier.Classify(view.ViewType));

        var top = context.Views.FirstOrDefault(v => v != baseView && v.ViewType == View.ViewTypes.TopView);
        var bottom = context.Views.FirstOrDefault(v => v != baseView && v.ViewType == View.ViewTypes.BottomView);
        var back = context.Views.FirstOrDefault(v => v != baseView && v.ViewType == View.ViewTypes.BackView);
        var sections = context.Views
            .Where(v => semanticKinds[v.GetIdentifier().ID] == ViewSemanticKind.Section)
            .ToList();
        var sectionPlacementSides = sections.ToDictionary(
            section => section.GetIdentifier().ID,
            section => _sectionPlacementSideResolver.Resolve(context.Drawing, baseView, section));

        var leftSections = sections.Where(section => sectionPlacementSides[section.GetIdentifier().ID].PlacementSide == SectionPlacementSide.Left).ToList();
        var rightSections = sections.Where(section => sectionPlacementSides[section.GetIdentifier().ID].PlacementSide == SectionPlacementSide.Right).ToList();
        var topSections = sections.Where(section => sectionPlacementSides[section.GetIdentifier().ID].PlacementSide == SectionPlacementSide.Top).ToList();
        var bottomSections = sections.Where(section => sectionPlacementSides[section.GetIdentifier().ID].PlacementSide == SectionPlacementSide.Bottom).ToList();

        DiagnoseRelaxedLayoutConflicts(context, baseView, top, bottom, back, leftSections, rightSections, topSections, bottomSections, conflicts);
        return conflicts;
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
        View front,
        View? top,
        View? bottom,
        View? back,
        IReadOnlyList<View> leftSections,
        IReadOnlyList<View> rightSections,
        IReadOnlyList<View> topSections,
        IReadOnlyList<View> bottomSections,
        List<DrawingFitConflict> conflicts)
    {
        var blocked = NormalizeReservedAreas(context);
        var (freeMinX, freeMaxX, freeMinY, freeMaxY) = ComputeFreeArea(context);
        var frontWidth = DrawingArrangeContextSizing.GetWidth(context, front);
        var frontHeight = DrawingArrangeContextSizing.GetHeight(context, front);

        var leftSlotW = (back != null ? DrawingArrangeContextSizing.GetWidth(context, back) + context.Gap : 0)
            + ComputeHorizontalStackWidth(context, leftSections, context.Gap);
        var rightSlotW = ComputeHorizontalStackWidth(context, rightSections, context.Gap);
        var topSlotH = (top != null ? DrawingArrangeContextSizing.GetHeight(context, top) + context.Gap : 0)
            + ComputeVerticalStackHeight(context, topSections, context.Gap);
        var bottomSlotH = (bottom != null ? DrawingArrangeContextSizing.GetHeight(context, bottom) + context.Gap : 0)
            + ComputeVerticalStackHeight(context, bottomSections, context.Gap);

        var frontRect = new ReservedRect(0, 0, 0, 0);
        var placed = false;
        foreach (var (x1, x2, y1, y2) in new[]
        {
            (freeMinX + leftSlotW, freeMaxX - rightSlotW, freeMinY + bottomSlotH, freeMaxY - topSlotH),
            (freeMinX + leftSlotW, freeMaxX - rightSlotW, freeMinY,               freeMaxY),
            (freeMinX,             freeMaxX,             freeMinY + bottomSlotH, freeMaxY - topSlotH),
            (freeMinX,             freeMaxX,             freeMinY,               freeMaxY),
        })
        {
            if (x2 - x1 >= frontWidth && y2 - y1 >= frontHeight
                && TryFindFrontViewRect(frontWidth, frontHeight, x1, x2, y1, y2, blocked, out frontRect))
            {
                placed = true;
                break;
            }
        }

        if (!placed)
        {
            AddConflict(conflicts, front, "Center", "outside_zone_bounds");
            return;
        }

        var occupied = new List<ReservedRect>(blocked) { frontRect };
        var topRect = new ReservedRect(0, 0, 0, 0);
        var bottomRect = new ReservedRect(0, 0, 0, 0);
        var backRect = new ReservedRect(0, 0, 0, 0);
        var topPlaced = false;
        var bottomPlaced = false;
        var backPlaced = false;

        if (top != null)
        {
            if (TryPlaceRelative(context, top, frontRect, freeMinX, freeMaxX, freeMinY, freeMaxY, context.Gap, occupied, RelativePlacement.Top, out var rect)
                || TryFindTopViewAtSheetTop(context, top, freeMinX, freeMaxX, freeMinY, freeMaxY, occupied, out rect))
            {
                topRect = rect;
                topPlaced = true;
                occupied.Add(rect);
            }
            else
            {
                DiagnoseRelativePlacementFailure(conflicts, context, top, frontRect, freeMinX, freeMaxX, freeMinY, freeMaxY, context.Gap, occupied, RelativePlacement.Top);
            }
        }

        if (bottom != null)
        {
            if (TryPlaceRelative(context, bottom, frontRect, freeMinX, freeMaxX, freeMinY, freeMaxY, context.Gap, occupied, RelativePlacement.Bottom, out var rect))
            {
                bottomRect = rect;
                bottomPlaced = true;
                occupied.Add(rect);
            }
            else
            {
                DiagnoseRelativePlacementFailure(conflicts, context, bottom, frontRect, freeMinX, freeMaxX, freeMinY, freeMaxY, context.Gap, occupied, RelativePlacement.Bottom);
            }
        }

        if (back != null)
        {
            if (TryPlaceRelative(context, back, frontRect, freeMinX, freeMaxX, freeMinY, freeMaxY, context.Gap, occupied, RelativePlacement.Left, out var rect))
            {
                backRect = rect;
                backPlaced = true;
                occupied.Add(rect);
            }
            else
            {
                DiagnoseRelativePlacementFailure(conflicts, context, back, frontRect, freeMinX, freeMaxX, freeMinY, freeMaxY, context.Gap, occupied, RelativePlacement.Left);
            }
        }

        DiagnoseStackPlacementFailureWithFallback(
            conflicts,
            context,
            leftSections,
            frontRect,
            backPlaced ? backRect : frontRect,
            frontRect,
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
            frontRect,
            frontRect,
            backPlaced ? backRect : frontRect,
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
            frontRect,
            topPlaced ? topRect : frontRect,
            bottomPlaced ? bottomRect : frontRect,
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
            frontRect,
            bottomPlaced ? bottomRect : frontRect,
            topPlaced ? topRect : frontRect,
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
    {
        if (view is IAxisAlignedBoundingBox bounded)
        {
            var box = bounded.GetAxisAlignedBoundingBox();
            if (box != null)
            {
                rect = new ReservedRect(box.MinPoint.X, box.MinPoint.Y, box.MaxPoint.X, box.MaxPoint.Y);
                return true;
            }
        }

        var origin = view.Origin;
        if (origin != null)
        {
            var halfWidth = view.Width * 0.5;
            var halfHeight = view.Height * 0.5;
            rect = new ReservedRect(
                origin.X - halfWidth,
                origin.Y - halfHeight,
                origin.X + halfWidth,
                origin.Y + halfHeight);
            return true;
        }

        rect = default;
        return false;
    }

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
        if (baseView?.ViewType != View.ViewTypes.FrontView)
            return false;

        var semanticKinds = context.Views.ToDictionary(
            view => view.GetIdentifier().ID,
            view => ViewSemanticClassifier.Classify(view.ViewType));

        // TODO: when this strategy becomes truly BaseView-centric, dependent views
        // must be selected relative to the chosen BaseView rather than by fixed
        // FrontView topology assumptions.
        var top = context.Views.FirstOrDefault(v => v != baseView && v.ViewType == View.ViewTypes.TopView);
        var bottom = context.Views.FirstOrDefault(v => v != baseView && v.ViewType == View.ViewTypes.BottomView);
        var back = context.Views.FirstOrDefault(v => v != baseView && v.ViewType == View.ViewTypes.BackView);
        var sections = context.Views
            .Where(v => semanticKinds[v.GetIdentifier().ID] == ViewSemanticKind.Section)
            .ToList();
        var detailViews = context.Views
            .Where(v => semanticKinds[v.GetIdentifier().ID] == ViewSemanticKind.Detail)
            .ToList();
        var otherViews = context.Views
            .Where(v => semanticKinds[v.GetIdentifier().ID] == ViewSemanticKind.Other)
            .ToList();
        var sectionPlacementSides = sections.ToDictionary(
            section => section.GetIdentifier().ID,
            section => _sectionPlacementSideResolver.Resolve(context.Drawing, baseView, section));
        var leftSections = sections
            .Where(section => sectionPlacementSides[section.GetIdentifier().ID].PlacementSide == SectionPlacementSide.Left)
            .ToList();
        var rightSections = sections
            .Where(section => sectionPlacementSides[section.GetIdentifier().ID].PlacementSide == SectionPlacementSide.Right)
            .ToList();
        var topSections = sections
            .Where(section => sectionPlacementSides[section.GetIdentifier().ID].PlacementSide == SectionPlacementSide.Top)
            .ToList();
        var bottomSections = sections
            .Where(section => sectionPlacementSides[section.GetIdentifier().ID].PlacementSide == SectionPlacementSide.Bottom)
            .ToList();
        var unknownSections = sections
            .Where(section => sectionPlacementSides[section.GetIdentifier().ID].PlacementSide == SectionPlacementSide.Unknown)
            .ToList();
        var deferredSections = leftSections
            .Concat(rightSections)
            .Concat(topSections)
            .Concat(bottomSections)
            .Concat(unknownSections)
            .ToList();
        var nonDetailSecondaryViews = context.Views
            .Where(v => semanticKinds[v.GetIdentifier().ID] != ViewSemanticKind.Section)
            .Where(v => semanticKinds[v.GetIdentifier().ID] != ViewSemanticKind.Detail)
            .Where(v => v != baseView && v != top && v != bottom && v != back)
            .ToList();
        var secondaryViews = nonDetailSecondaryViews
            .Concat(deferredSections)
            .Concat(detailViews)
            .ToList();

        PerfTrace.Write(
            "api-view",
            "view_semantic_summary",
            0,
            $"baseProjected={context.Views.Count(v => semanticKinds[v.GetIdentifier().ID] == ViewSemanticKind.BaseProjected)} sections={sections.Count} details={detailViews.Count} other={otherViews.Count}");

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
            if (TryPlanStrictLayout(context, baseView, top, bottom, back, leftSections, rightSections, topSections, bottomSections, secondaryViews, out planned))
                return true;
            PerfTrace.Write("api-view", "front_arrange_try", 0, "mode=strict result=failed");
        }

        if (TryPlanRelaxedLayout(context, baseView, top, bottom, back, leftSections, rightSections, topSections, bottomSections, secondaryViews, out planned))
            return true;
        PerfTrace.Write("api-view", "front_arrange_try", 0, "mode=relaxed result=failed");

        var strictRetry = TryPlanStrictLayout(context, baseView, top, bottom, back, leftSections, rightSections, topSections, bottomSections, secondaryViews, out planned);
        PerfTrace.Write("api-view", "front_arrange_try", 0, $"mode=strict-retry result={(strictRetry ? "ok" : "failed")}");
        return strictRetry;
    }

    private static double GetCurrentScale(DrawingArrangeContext context)
        => context.Views.Select(v => v.Attributes.Scale).FirstOrDefault(s => s > 0);

    private bool TryPlanStrictLayout(
        DrawingArrangeContext context,
        View front,
        View? top,
        View? bottom,
        View? back,
        IReadOnlyList<View> leftSections,
        IReadOnlyList<View> rightSections,
        IReadOnlyList<View> topSections,
        IReadOnlyList<View> bottomSections,
        IReadOnlyList<View> secondaryViews,
        out List<PlannedPlacement> planned)
    {
        planned = new List<PlannedPlacement>();

        var gap = context.Gap;
        var blocked = NormalizeReservedAreas(context);
        var freeArea = ComputeFreeArea(context);
        var frontWidth = DrawingArrangeContextSizing.GetWidth(context, front);
        var frontHeight = DrawingArrangeContextSizing.GetHeight(context, front);
        var topWidth = top != null ? DrawingArrangeContextSizing.GetWidth(context, top) : 0;
        var topHeight = top != null ? DrawingArrangeContextSizing.GetHeight(context, top) : 0;
        var bottomWidth = bottom != null ? DrawingArrangeContextSizing.GetWidth(context, bottom) : 0;
        var bottomHeight = bottom != null ? DrawingArrangeContextSizing.GetHeight(context, bottom) : 0;
        var backWidth = back != null ? DrawingArrangeContextSizing.GetWidth(context, back) : 0;
        var backHeight = back != null ? DrawingArrangeContextSizing.GetHeight(context, back) : 0;
        var leftSectionStackWidth = ComputeHorizontalStackWidth(context, leftSections, gap);
        var rightSectionStackWidth = ComputeHorizontalStackWidth(context, rightSections, gap);
        PerfTrace.Write(
            "api-view",
            "front_arrange_strict_input",
            0,
            $"free=({freeArea.minX:F2},{freeArea.maxX:F2},{freeArea.minY:F2},{freeArea.maxY:F2}) front=({frontWidth:F2},{frontHeight:F2}) top=({topWidth:F2},{topHeight:F2}) leftStackW=({leftSectionStackWidth:F2}) rightStackW=({rightSectionStackWidth:F2}) back=({backWidth:F2},{backHeight:F2}) bottom=({bottomWidth:F2},{bottomHeight:F2})");

        var leftSlotW = (back != null ? backWidth + gap : 0) + leftSectionStackWidth;
        var rightSlotW = rightSectionStackWidth;
        var topSlotH = (top != null ? topHeight + gap : 0) + ComputeVerticalStackHeight(context, topSections, gap);
        var bottomSlotH = (bottom != null ? bottomHeight + gap : 0) + ComputeVerticalStackHeight(context, bottomSections, gap);

        if (!TryFindFrontViewRect(
                frontWidth,
                frontHeight,
                freeArea.minX + leftSlotW,
                freeArea.maxX - rightSlotW,
                freeArea.minY + bottomSlotH,
                freeArea.maxY - topSlotH,
                blocked,
                out var frontRect))
            return false;

        planned.Add(new PlannedPlacement(front, CenterX(frontRect), CenterY(frontRect)));

        var occupied = new List<ReservedRect>(blocked) { frontRect };
        var topRect = new ReservedRect(0, 0, 0, 0);
        var bottomRect = new ReservedRect(0, 0, 0, 0);
        var backRect = new ReservedRect(0, 0, 0, 0);

        if (top != null)
        {
            topRect = new ReservedRect(
                CenterX(frontRect) - topWidth / 2.0,
                frontRect.MaxY + gap,
                CenterX(frontRect) + topWidth / 2.0,
                frontRect.MaxY + gap + topHeight);
            if (!IsWithinArea(topRect, freeArea.minX, freeArea.maxX, freeArea.minY, freeArea.maxY) || IntersectsAny(topRect, occupied))
                return false;

            planned.Add(new PlannedPlacement(top, CenterX(topRect), CenterY(topRect)));
            occupied.Add(topRect);
        }

        if (bottom != null)
        {
            bottomRect = new ReservedRect(
                CenterX(frontRect) - bottomWidth / 2.0,
                frontRect.MinY - gap - bottomHeight,
                CenterX(frontRect) + bottomWidth / 2.0,
                frontRect.MinY - gap);
            if (!IsWithinArea(bottomRect, freeArea.minX, freeArea.maxX, freeArea.minY, freeArea.maxY) || IntersectsAny(bottomRect, occupied))
                return false;

            planned.Add(new PlannedPlacement(bottom, CenterX(bottomRect), CenterY(bottomRect)));
            occupied.Add(bottomRect);
        }

        if (back != null)
        {
            backRect = new ReservedRect(
                frontRect.MinX - gap - backWidth,
                CenterY(frontRect) - backHeight / 2.0,
                frontRect.MinX - gap,
                CenterY(frontRect) + backHeight / 2.0);
            if (!IsWithinArea(backRect, freeArea.minX, freeArea.maxX, freeArea.minY, freeArea.maxY) || IntersectsAny(backRect, occupied))
                return false;

            planned.Add(new PlannedPlacement(back, CenterX(backRect), CenterY(backRect)));
            occupied.Add(backRect);
        }

        var leftAnchor = back != null ? backRect : frontRect;
        TryPlaceVerticalSectionStackWithFallback(
            context,
            leftSections,
            frontRect,
            leftAnchor,
            frontRect,
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
            frontRect,
            frontRect,
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
            frontRect,
            top != null ? topRect : frontRect,
            bottom != null ? bottomRect : frontRect,
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
            frontRect,
            bottom != null ? bottomRect : frontRect,
            top != null ? topRect : frontRect,
            RelativePlacement.Bottom,
            freeArea.minX,
            freeArea.maxX,
            freeArea.minY,
            freeArea.maxY,
            gap,
            occupied,
            planned);

        // Secondary views left unplanned — Arrange() handles them via MaxRects.
        return true;
    }

    private bool TryPlanRelaxedLayout(
        DrawingArrangeContext context,
        View front,
        View? top,
        View? bottom,
        View? back,
        IReadOnlyList<View> leftSections,
        IReadOnlyList<View> rightSections,
        IReadOnlyList<View> topSections,
        IReadOnlyList<View> bottomSections,
        IReadOnlyList<View> secondaryViews,
        out List<PlannedPlacement> planned)
    {
        planned = new List<PlannedPlacement>();

        var blocked = NormalizeReservedAreas(context);
        var (freeMinX, freeMaxX, freeMinY, freeMaxY) = ComputeFreeArea(context);
        var frontWidth = DrawingArrangeContextSizing.GetWidth(context, front);
        var frontHeight = DrawingArrangeContextSizing.GetHeight(context, front);

        // Try placing FrontView with progressively relaxed zone constraints.
        // Prefer positions that leave room for dependent views in all directed zones.
        var leftSlotW = (back != null ? DrawingArrangeContextSizing.GetWidth(context, back) + context.Gap : 0)
            + ComputeHorizontalStackWidth(context, leftSections, context.Gap);
        var rightSlotW = ComputeHorizontalStackWidth(context, rightSections, context.Gap);
        var topSlotH = (top != null ? DrawingArrangeContextSizing.GetHeight(context, top) + context.Gap : 0)
            + ComputeVerticalStackHeight(context, topSections, context.Gap);
        var bottomSlotH = (bottom != null ? DrawingArrangeContextSizing.GetHeight(context, bottom) + context.Gap : 0)
            + ComputeVerticalStackHeight(context, bottomSections, context.Gap);

        var frontRect = new ReservedRect(0, 0, 0, 0);
        var placed = false;
        foreach (var (x1, x2, y1, y2) in new[]
        {
            (freeMinX + leftSlotW, freeMaxX - rightSlotW, freeMinY + bottomSlotH, freeMaxY - topSlotH),
            (freeMinX + leftSlotW, freeMaxX - rightSlotW, freeMinY,               freeMaxY),
            (freeMinX,             freeMaxX,             freeMinY + bottomSlotH, freeMaxY - topSlotH),
            (freeMinX,             freeMaxX,             freeMinY,               freeMaxY),
        })
        {
            if (x2 - x1 >= frontWidth && y2 - y1 >= frontHeight
                && TryFindFrontViewRect(frontWidth, frontHeight, x1, x2, y1, y2, blocked, out frontRect))
            {
                placed = true;
                break;
            }
        }

        if (!placed)
            return false;

        planned.Add(new PlannedPlacement(front, CenterX(frontRect), CenterY(frontRect)));
        var occupied = new List<ReservedRect>(blocked) { frontRect };

        var deferred = new List<View>(secondaryViews);
        var topRect = new ReservedRect(0, 0, 0, 0);
        var bottomRect = new ReservedRect(0, 0, 0, 0);
        var backRect = new ReservedRect(0, 0, 0, 0);
        var topPlaced = false;
        var bottomPlaced = false;
        var backPlaced = false;

        if (top != null)
        {
            if (TryPlaceRelative(context, top, frontRect, freeMinX, freeMaxX, freeMinY, freeMaxY, context.Gap, occupied, RelativePlacement.Top, out var rect)
                || TryFindTopViewAtSheetTop(context, top, freeMinX, freeMaxX, freeMinY, freeMaxY, occupied, out rect))
            {
                planned.Add(new PlannedPlacement(top, CenterX(rect), CenterY(rect)));
                occupied.Add(rect);
                topRect = rect;
                topPlaced = true;
            }
            else
            {
                deferred.Add(top);
            }
        }

        if (bottom != null)
        {
            if (TryPlaceRelative(context, bottom, frontRect, freeMinX, freeMaxX, freeMinY, freeMaxY, context.Gap, occupied, RelativePlacement.Bottom, out var rect))
            {
                planned.Add(new PlannedPlacement(bottom, CenterX(rect), CenterY(rect)));
                occupied.Add(rect);
                bottomRect = rect;
                bottomPlaced = true;
            }
            else
            {
                deferred.Add(bottom);
            }
        }

        if (back != null)
        {
            if (TryPlaceRelative(context, back, frontRect, freeMinX, freeMaxX, freeMinY, freeMaxY, context.Gap, occupied, RelativePlacement.Left, out var rect))
            {
                planned.Add(new PlannedPlacement(back, CenterX(rect), CenterY(rect)));
                occupied.Add(rect);
                backRect = rect;
                backPlaced = true;
            }
            else
            {
                deferred.Add(back);
            }
        }

        var leftPlacedAnchor = backPlaced ? backRect : frontRect;
        TryPlaceVerticalSectionStackWithFallback(
            context,
            leftSections,
            frontRect,
            leftPlacedAnchor,
            frontRect,
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
            frontRect,
            frontRect,
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
            frontRect,
            topPlaced ? topRect : frontRect,
            bottomPlaced ? bottomRect : frontRect,
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
            frontRect,
            bottomPlaced ? bottomRect : frontRect,
            topPlaced ? topRect : frontRect,
            RelativePlacement.Bottom,
            freeMinX,
            freeMaxX,
            freeMinY,
            freeMaxY,
            context.Gap,
            occupied,
            planned);

        // Deferred secondary views left unplanned — Arrange() handles them via MaxRects.
        return true;
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
                return false;

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
                return false;

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
            var minX = CenterX(frontRect) - width / 2.0;
            if (minX < freeMinX || minX + width > freeMaxX)
                return false;

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

            if (!IsWithinArea(rect, freeMinX, freeMaxX, freeMinY, freeMaxY) || IntersectsAny(rect, occupied) || proposed.Any(item => Intersects(item.Rect, rect)))
                return false;

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
            return true;

        var fallbackZone = preferredZone == RelativePlacement.Top
            ? RelativePlacement.Bottom
            : RelativePlacement.Top;

        return TryPlaceHorizontalSectionStack(
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
            return true;

        var fallbackZone = preferredZone == RelativePlacement.Left
            ? RelativePlacement.Right
            : RelativePlacement.Left;

        return TryPlaceVerticalSectionStack(
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
    internal static bool TryFindFrontViewRect(
        double width,
        double height,
        double minX,
        double maxX,
        double minY,
        double maxY,
        IReadOnlyList<ReservedRect> blocked,
        out ReservedRect placement)
    {
        if (width <= 0 || height <= 0 || maxX - minX < width || maxY - minY < height)
        {
            placement = new ReservedRect(0, 0, 0, 0);
            return false;
        }

        var cx = minX + (maxX - minX - width) / 2.0;
        var cy = minY + (maxY - minY - height) / 2.0;
        var rx = maxX - width;
        var lx = minX;
        var ty = maxY - height;
        var by = minY;

        // Try center first (most balanced), then shifts to avoid reserved corners
        var candidates = new[]
        {
            new ReservedRect(cx, cy, cx + width, cy + height),
            new ReservedRect(rx, cy, rx + width, cy + height),
            new ReservedRect(lx, cy, lx + width, cy + height),
            new ReservedRect(cx, ty, cx + width, ty + height),
            new ReservedRect(rx, ty, rx + width, ty + height),
            new ReservedRect(lx, ty, lx + width, ty + height),
            new ReservedRect(cx, by, cx + width, by + height),
            new ReservedRect(rx, by, rx + width, by + height),
            new ReservedRect(lx, by, lx + width, by + height),
        };

        foreach (var candidate in candidates)
        {
            if (!IntersectsAny(candidate, blocked))
            {
                placement = candidate;
                return true;
            }
        }

        placement = new ReservedRect(0, 0, 0, 0);
        return false;
    }

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

    private static PackedRectangle ToBlockedRectangle(double freeMinX, double freeMaxY, ReservedRect rect)
        => new(
            rect.MinX - freeMinX,
            freeMaxY - rect.MaxY,
            rect.MaxX - rect.MinX,
            rect.MaxY - rect.MinY);

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
