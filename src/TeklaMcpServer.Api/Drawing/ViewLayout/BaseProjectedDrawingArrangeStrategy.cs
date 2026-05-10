using System.Collections.Generic;
using System.Linq;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;
using TeklaMcpServer.Api.Algorithms.Packing;
using TeklaMcpServer.Api.Diagnostics;
using TeklaMcpServer.Api.Drawing;
//publish
namespace TeklaMcpServer.Api.Drawing.ViewLayout;

public sealed partial class BaseProjectedDrawingArrangeStrategy : IDrawingViewArrangeStrategy, IDrawingViewArrangeDiagnosticsStrategy
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

    private readonly struct ViewPlacementSearchArea
    {
        public ViewPlacementSearchArea(
            ReservedRect baseRect,
            double freeMinX,
            double freeMaxX,
            double freeMinY,
            double freeMaxY)
        {
            BaseRect = baseRect;
            FreeMinX = freeMinX;
            FreeMaxX = freeMaxX;
            FreeMinY = freeMinY;
            FreeMaxY = freeMaxY;
        }

        public ReservedRect BaseRect { get; }
        public double FreeMinX { get; }
        public double FreeMaxX { get; }
        public double FreeMinY { get; }
        public double FreeMaxY { get; }
    }

    private static ViewPlacementSearchArea CreateSearchArea(
        double freeMinX,
        double freeMaxX,
        double freeMinY,
        double freeMaxY)
        => new(new ReservedRect(0, 0, 0, 0), freeMinX, freeMaxX, freeMinY, freeMaxY);

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

    internal static bool IsOversizedStandardSection(
        SectionPlacementSide placementSide,
        double baseWidth,
        double baseHeight,
        double sectionWidth,
        double sectionHeight,
        double gap)
        => placementSide switch
        {
            SectionPlacementSide.Top or SectionPlacementSide.Bottom => sectionWidth > baseWidth + gap,
            SectionPlacementSide.Left or SectionPlacementSide.Right => sectionHeight > baseHeight + gap,
            _ => false
        };

    public bool CanArrange(DrawingArrangeContext context)
    {
        var baseView = context.Topology.BaseView;
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
                return ViewPlacementGeometryService.CreateCandidateRect(p.View, p.X, p.Y, w, h);
            }).ToList();
            var extendedReserved = new System.Collections.Generic.List<ReservedRect>(planningContext.ReservedAreas);
            extendedReserved.AddRange(anchorRects);
            var unplannedCtx = planningContext.With(
                views: unplannedViews,
                reservedAreas: extendedReserved);
            var unplannedFrames = unplannedViews
                .Select(v => (DrawingArrangeContextSizing.GetWidth(unplannedCtx, v), DrawingArrangeContextSizing.GetHeight(unplannedCtx, v)))
                .ToList();
            if (!_maxRectsFallback.EstimateFit(unplannedCtx, unplannedFrames))
                return false;

            var unplannedSectionIds = CollectUnplannedSemanticSectionIds(planningContext, plannedIds);
            if (unplannedSectionIds.Count > 0)
                return false;

            return true;
        }

        if (CollectSemanticSectionIds(planningContext).Count > 0)
            return false;

        var maxr  = _maxRectsFallback.EstimateFit(planningContext, frames);
        var shelf = !maxr && planningContext.ReservedAreas.Count == 0 && _fallback.EstimateFit(planningContext, frames);
        return maxr || shelf;
    }

    private static HashSet<int> CollectUnplannedSemanticSectionIds(
        DrawingArrangeContext context,
        HashSet<int> plannedIds)
    {
        var semanticViews = context.Topology.SemanticViews;
        return semanticViews.Sections
            .Select(view => view.GetIdentifier().ID)
            .Where(id => !plannedIds.Contains(id))
            .ToHashSet();
    }

    private static HashSet<int> CollectSemanticSectionIds(DrawingArrangeContext context)
        => CollectUnplannedSemanticSectionIds(context, new HashSet<int>());

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
            var otherResidualRects = residualRectsById
                .Where(other => other.Key != entry.Key)
                .ToDictionary(other => other.Key, other => other.Value);
            var validation = ViewPlacementValidator.Validate(
                rect,
                context.Margin,
                context.SheetWidth - context.Margin,
                context.Margin,
                context.SheetHeight - context.Margin,
                context.ReservedAreas,
                otherResidualRects);
            if (!validation.Fits)
            {
                PerfTrace.Write("api-view", "fallback_layout_reject", 0,
                    $"view={entry.Key} reason={validation.Reason} rect=[{rect.MinX:F2},{rect.MinY:F2},{rect.MaxX:F2},{rect.MaxY:F2}] blockers={FormatValidationBlockers(validation.Blockers)}");
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
            residualRectsById[view.GetIdentifier().ID] = ViewPlacementGeometryService.CreateCandidateRect(
                view,
                originX,
                originY,
                width,
                height);
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
                var unplannedSectionIds = CollectUnplannedSemanticSectionIds(context, plannedIds);
                var residualEligible = unplanned
                    .Where(v => !unplannedSectionIds.Contains(v.GetIdentifier().ID))
                    .ToList();

                var anchorRects = planned.Select(p =>
                {
                    var w = DrawingArrangeContextSizing.GetWidth(context, p.View);
                    var h = DrawingArrangeContextSizing.GetHeight(context, p.View);
                    return ViewPlacementGeometryService.CreateCandidateRect(p.View, p.X, p.Y, w, h);
                }).ToList();

                var extendedReserved = new System.Collections.Generic.List<ReservedRect>(context.ReservedAreas);
                extendedReserved.AddRange(anchorRects);

                var unplannedCtx = context.With(
                    views: residualEligible,
                    reservedAreas: extendedReserved);

                PerfTrace.Write("api-view", "front_arrange_plan", 0,
                    $"mode=anchor-then-maxrects anchors={planned.Count} remaining={residualEligible.Count} unresolvedSections={unplannedSectionIds.Count}");

                if (residualEligible.Count == 0)
                {
                    PerfTrace.Write("api-view", "front_arrange_plan", 0, "mode=anchor-only unresolved-sections");
                    return result;
                }

                var unplannedFrames = residualEligible
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

        var baseViewSelection = planningContext.Topology.BaseSelection;
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

            var unplannedSectionIds = CollectUnplannedSemanticSectionIds(planningContext, plannedIds);
            foreach (var section in unplannedViews.Where(v => unplannedSectionIds.Contains(v.GetIdentifier().ID)))
                AddConflict(conflicts, section, "Residual", "section_residual_disallowed", target: "semantic-section");

            if (conflicts.Count > 0)
                return conflicts;

            var anchorRects = planned.Select(p =>
            {
                var w = DrawingArrangeContextSizing.GetWidth(planningContext, p.View);
                var h = DrawingArrangeContextSizing.GetHeight(planningContext, p.View);
                return new ReservedRect(p.X - w / 2.0, p.Y - h / 2.0, p.X + w / 2.0, p.Y + h / 2.0);
            }).ToList();
            var extendedReserved = new System.Collections.Generic.List<ReservedRect>(planningContext.ReservedAreas);
            extendedReserved.AddRange(anchorRects);
            var unplannedCtx = planningContext.With(
                views: unplannedViews,
                reservedAreas: extendedReserved);
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

        var topology = planningContext.Topology;
        var neighbors = topology.Neighbors;
        if (neighbors == null)
            return conflicts;
        var sectionGroups = SectionGroupSet.Build(
            topology.SemanticViews.Sections,
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

        return context.With(effectiveFrameSizes: effectiveFrameSizes);
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

        if (!TrySelectBaseRectWithBudgets(
                context,
                neighbors,
                leftSections,
                rightSections,
                topSections,
                bottomSections,
                blocked,
                includeRelaxedCandidates: true,
                requireAllStrictNeighborsFit: false,
                out var baseRect,
                out var baseDecision))
        {
            var conflictedView = baseDecision.RejectView ?? baseView;
            var attemptedZone = string.IsNullOrEmpty(baseDecision.RejectZone) ? "Center" : baseDecision.RejectZone;
            var reason = string.IsNullOrEmpty(baseDecision.RejectReason) ? "outside_zone_bounds" : baseDecision.RejectReason;
            AddConflict(conflicts, conflictedView, attemptedZone, reason);
            if (HasArea(baseDecision.RejectRect))
                EnsureBoundingRect(conflicts, conflictedView, attemptedZone, baseDecision.RejectRect);
            return;
        }

        var occupied = new List<ReservedRect>(blocked) { baseRect };
        var mainSkeleton = new MainSkeletonPlacementState();
        var leftSectionPartition = PartitionStandardSections(context, baseRect, leftSections, SectionPlacementSide.Left);
        var rightSectionPartition = PartitionStandardSections(context, baseRect, rightSections, SectionPlacementSide.Right);
        var topSectionPartition = PartitionStandardSections(context, baseRect, topSections, SectionPlacementSide.Top);
        var bottomSectionPartition = PartitionStandardSections(context, baseRect, bottomSections, SectionPlacementSide.Bottom);

        var searchArea = new ViewPlacementSearchArea(baseRect, freeMinX, freeMaxX, freeMinY, freeMaxY);

        foreach (var spec in CreateMainSkeletonNeighborSpecs(top, bottom, leftNeighbor, rightNeighbor))
        {
            TryPlaceOptionalDiagnosticMainSkeletonNeighbor(
                conflicts,
                context,
                spec,
                searchArea,
                occupied,
                mainSkeleton);
        }

        if (!TryValidateMainSkeletonSpacing(
                baseRect,
                mainSkeleton,
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
            leftSectionPartition.Normal,
            baseRect,
            mainSkeleton.GetAnchorOrBase("left", baseRect),
            mainSkeleton.GetAnchorOrBase("right", baseRect),
            RelativePlacement.Left,
            searchArea,
            context.Gap,
            occupied);
        DiagnoseOversizedStandardSections(
            conflicts,
            context,
            leftSectionPartition.Oversized,
            baseRect,
            mainSkeleton.GetAnchorOrBase("left", baseRect),
            mainSkeleton.GetAnchorOrBase("right", baseRect),
            RelativePlacement.Left,
            searchArea,
            context.Gap,
            occupied);

        DiagnoseStackPlacementFailureWithFallback(
            conflicts,
            context,
            rightSectionPartition.Normal,
            baseRect,
            mainSkeleton.GetAnchorOrBase("right", baseRect),
            mainSkeleton.GetAnchorOrBase("left", baseRect),
            RelativePlacement.Right,
            searchArea,
            context.Gap,
            occupied);
        DiagnoseOversizedStandardSections(
            conflicts,
            context,
            rightSectionPartition.Oversized,
            baseRect,
            mainSkeleton.GetAnchorOrBase("right", baseRect),
            mainSkeleton.GetAnchorOrBase("left", baseRect),
            RelativePlacement.Right,
            searchArea,
            context.Gap,
            occupied);

        DiagnoseHorizontalStackPlacementFailureWithFallback(
            conflicts,
            context,
            topSectionPartition.Normal,
            baseRect,
            mainSkeleton.GetAnchorOrBase("top", baseRect),
            mainSkeleton.GetAnchorOrBase("bottom", baseRect),
            RelativePlacement.Top,
            searchArea,
            context.Gap,
            occupied);
        DiagnoseOversizedStandardSections(
            conflicts,
            context,
            topSectionPartition.Oversized,
            baseRect,
            mainSkeleton.GetAnchorOrBase("top", baseRect),
            mainSkeleton.GetAnchorOrBase("bottom", baseRect),
            RelativePlacement.Top,
            searchArea,
            context.Gap,
            occupied);

        DiagnoseHorizontalStackPlacementFailureWithFallback(
            conflicts,
            context,
            bottomSectionPartition.Normal,
            baseRect,
            mainSkeleton.GetAnchorOrBase("bottom", baseRect),
            mainSkeleton.GetAnchorOrBase("top", baseRect),
            RelativePlacement.Bottom,
            searchArea,
            context.Gap,
            occupied);
        DiagnoseOversizedStandardSections(
            conflicts,
            context,
            bottomSectionPartition.Oversized,
            baseRect,
            mainSkeleton.GetAnchorOrBase("bottom", baseRect),
            mainSkeleton.GetAnchorOrBase("top", baseRect),
            RelativePlacement.Bottom,
            searchArea,
            context.Gap,
            occupied);
    }

    private static void DiagnoseStackPlacementFailureWithFallback(
        List<DrawingFitConflict> conflicts,
        DrawingArrangeContext context,
        IReadOnlyList<View> sectionViews,
        ReservedRect frontRect,
        ReservedRect preferredAnchorRect,
        ReservedRect fallbackAnchorRect,
        RelativePlacement preferredZone,
        ViewPlacementSearchArea searchArea,
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
                    searchArea,
                    gap,
                    occupied.ToList(),
                    new List<PlannedPlacement>(),
                    preferredPlacementSide,
                    zone == preferredZone ? preferredPlacementSide : GetFallbackPlacementSide(preferredPlacementSide)),
                out _,
                out _))
            return;

        DiagnoseVerticalStackPlacementFailure(conflicts, context, sectionViews, frontRect, preferredAnchorRect, preferredZone, searchArea, gap, occupied);
    }

    private static void DiagnoseHorizontalStackPlacementFailureWithFallback(
        List<DrawingFitConflict> conflicts,
        DrawingArrangeContext context,
        IReadOnlyList<View> sectionViews,
        ReservedRect frontRect,
        ReservedRect preferredAnchorRect,
        ReservedRect fallbackAnchorRect,
        RelativePlacement preferredZone,
        ViewPlacementSearchArea searchArea,
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
                    searchArea,
                    gap,
                    occupied.ToList(),
                    new List<PlannedPlacement>(),
                    preferredPlacementSide,
                    zone == preferredZone ? preferredPlacementSide : GetFallbackPlacementSide(preferredPlacementSide)),
                out _,
                out _))
            return;

        DiagnoseHorizontalStackPlacementFailure(conflicts, context, sectionViews, frontRect, preferredAnchorRect, preferredZone, searchArea, gap, occupied);
    }

    private static void DiagnoseOversizedStandardSections(
        List<DrawingFitConflict> conflicts,
        DrawingArrangeContext context,
        IReadOnlyList<View> sectionViews,
        ReservedRect frontRect,
        ReservedRect preferredAnchorRect,
        ReservedRect fallbackAnchorRect,
        RelativePlacement preferredZone,
        ViewPlacementSearchArea searchArea,
        double gap,
        IReadOnlyList<ReservedRect> occupied)
    {
        if (sectionViews.Count == 0)
            return;

        if (TryPlaceDegradedStandardSections(
                context,
                sectionViews,
                frontRect,
                preferredAnchorRect,
                fallbackAnchorRect,
                preferredZone,
                searchArea,
                gap,
                occupied.ToList(),
                new List<PlannedPlacement>()))
            return;

        foreach (var section in sectionViews)
            AddConflict(conflicts, section, preferredZone.ToString(), "oversized_section");
    }

    private static void DiagnoseVerticalStackPlacementFailure(
        List<DrawingFitConflict> conflicts,
        DrawingArrangeContext context,
        IReadOnlyList<View> sectionViews,
        ReservedRect frontRect,
        ReservedRect anchorRect,
        RelativePlacement zone,
        ViewPlacementSearchArea searchArea,
        double gap,
        IReadOnlyList<ReservedRect> occupied)
    {
        if (TryPlanVerticalSectionStack(
                context,
                sectionViews,
                frontRect,
                anchorRect,
                zone,
                searchArea,
                gap,
                occupied,
                out _,
                out var failure))
            return;

        DiagnoseSectionStackFailure(conflicts, zone, failure, occupied);
    }

    private static void DiagnoseHorizontalStackPlacementFailure(
        List<DrawingFitConflict> conflicts,
        DrawingArrangeContext context,
        IReadOnlyList<View> sectionViews,
        ReservedRect frontRect,
        ReservedRect anchorRect,
        RelativePlacement zone,
        ViewPlacementSearchArea searchArea,
        double gap,
        IReadOnlyList<ReservedRect> occupied)
    {
        if (TryPlanHorizontalSectionStack(
                context,
                sectionViews,
                frontRect,
                anchorRect,
                zone,
                searchArea,
                gap,
                occupied,
                out _,
                out var failure))
            return;

        DiagnoseSectionStackFailure(conflicts, zone, failure, occupied);
    }

    private static void DiagnoseSectionStackFailure(
        List<DrawingFitConflict> conflicts,
        RelativePlacement zone,
        SectionStackFailureInfo? failure,
        IReadOnlyList<ReservedRect> occupied)
    {
        if (failure == null)
            return;

        if (failure.Value.ConflictReason == "out-of-bounds")
        {
            AddConflict(conflicts, failure.Value.Section, zone.ToString(), "outside_zone_bounds");
            return;
        }

        AddSectionCandidateConflict(
            conflicts,
            failure.Value.Section,
            zone,
            failure.Value.Rect,
            failure.Value.ConflictReason,
            occupied);
    }

    private static bool TryPlanVerticalSectionStack(
        DrawingArrangeContext context,
        IReadOnlyList<View> sectionViews,
        ReservedRect frontRect,
        ReservedRect anchorRect,
        RelativePlacement zone,
        ViewPlacementSearchArea searchArea,
        double gap,
        IReadOnlyList<ReservedRect> occupied,
        out List<(View View, ReservedRect Rect)> proposed,
        out SectionStackFailureInfo? failure)
    {
        proposed = new List<(View View, ReservedRect Rect)>(sectionViews.Count);
        var currentAnchor = anchorRect;
        foreach (var section in OrderSectionViewsForStack(context, sectionViews))
        {
            var width = DrawingArrangeContextSizing.GetWidth(context, section);
            var height = DrawingArrangeContextSizing.GetHeight(context, section);
            if (!TryCreateVerticalSectionRect(frontRect, currentAnchor, zone, width, height, gap, searchArea, out var rect))
            {
                failure = new SectionStackFailureInfo(
                    section,
                    new ReservedRect(0, 0, 0, 0),
                    "out-of-bounds-y",
                    "out-of-bounds");
                return false;
            }

            if (!TryValidateSectionCandidateRect(rect, searchArea, occupied, proposed.Select(item => item.Rect), out var reason))
            {
                failure = new SectionStackFailureInfo(section, rect, reason, reason);
                return false;
            }

            proposed.Add((section, rect));
            currentAnchor = rect;
        }

        failure = null;
        return true;
    }

    private static bool TryPlanHorizontalSectionStack(
        DrawingArrangeContext context,
        IReadOnlyList<View> sectionViews,
        ReservedRect frontRect,
        ReservedRect anchorRect,
        RelativePlacement zone,
        ViewPlacementSearchArea searchArea,
        double gap,
        IReadOnlyList<ReservedRect> occupied,
        out List<(View View, ReservedRect Rect)> proposed,
        out SectionStackFailureInfo? failure)
    {
        proposed = new List<(View View, ReservedRect Rect)>(sectionViews.Count);
        var currentAnchor = anchorRect;
        foreach (var section in OrderSectionViewsForStack(context, sectionViews))
        {
            if (!TryFindHorizontalSectionRectInSearchArea(
                    context,
                    section,
                    frontRect,
                    currentAnchor,
                    zone,
                    searchArea,
                    gap,
                    occupied,
                    proposed.Select(item => item.Rect),
                    out var rect,
                    out failure))
            {
                return false;
            }

            proposed.Add((section, rect));
            currentAnchor = rect;
        }

        failure = null;
        return true;
    }

    private static bool TryFindHorizontalSectionRectInSearchArea(
        DrawingArrangeContext context,
        View section,
        ReservedRect frontRect,
        ReservedRect anchorRect,
        RelativePlacement zone,
        ViewPlacementSearchArea searchArea,
        double gap,
        IReadOnlyList<ReservedRect> occupied,
        IEnumerable<ReservedRect> proposed,
        out ReservedRect rect,
        out SectionStackFailureInfo? failure)
    {
        var width = DrawingArrangeContextSizing.GetWidth(context, section);
        var height = DrawingArrangeContextSizing.GetHeight(context, section);
        return TryFindHorizontalSectionRectInSearchArea(
            section,
            width,
            height,
            frontRect,
            anchorRect,
            zone,
            searchArea,
            gap,
            occupied,
            proposed,
            out rect,
            out failure);
    }

    private static bool TryFindHorizontalSectionRectInSearchArea(
        View section,
        double width,
        double height,
        ReservedRect frontRect,
        ReservedRect anchorRect,
        RelativePlacement zone,
        ViewPlacementSearchArea searchArea,
        double gap,
        IReadOnlyList<ReservedRect> occupied,
        IEnumerable<ReservedRect> proposed,
        out ReservedRect rect,
        out SectionStackFailureInfo? failure)
    {
        if (!TryGetHorizontalSectionPlacementInputs(
                frontRect,
                anchorRect,
                zone,
                width,
                height,
                gap,
                searchArea,
                out var preferredMinX,
                out var minY,
                out var maxY))
        {
            rect = new ReservedRect(0, 0, 0, 0);
            failure = new SectionStackFailureInfo(
                section,
                rect,
                "out-of-bounds-x",
                "out-of-bounds");
            return false;
        }

        var proposedRects = proposed.ToList();
        var blockers = occupied.Concat(proposedRects).ToList();
        var preferredRect = new ReservedRect(preferredMinX, minY, preferredMinX + width, maxY);
        TryValidateSectionCandidateRect(preferredRect, searchArea, occupied, proposedRects, out var preferredReason);

        var currentMinX = TryGetCurrentHorizontalSectionMinX(section, width);
        foreach (var candidateMinX in EnumerateHorizontalSectionCandidateMinXs(preferredMinX, currentMinX, width, minY, maxY, searchArea, blockers))
        {
            var adjustedMinX = PushHorizontalSectionCandidateRight(candidateMinX, minY, maxY, width, blockers);
            if (!IsHorizontalSectionCandidateInSearchArea(adjustedMinX, width, searchArea))
                continue;

            rect = new ReservedRect(adjustedMinX, minY, adjustedMinX + width, maxY);
            if (TryValidateSectionCandidateRect(rect, searchArea, occupied, proposedRects, out _))
            {
                failure = null;
                return true;
            }
        }

        rect = preferredRect;
        failure = new SectionStackFailureInfo(
            section,
            preferredRect,
            "no-valid-x",
            string.IsNullOrEmpty(preferredReason) ? "intersects_view" : preferredReason);
        return false;
    }

    internal static HorizontalSectionProbeResult ProbeHorizontalSectionCandidate(
        View section,
        ReservedRect frontRect,
        ReservedRect anchorRect,
        SectionPlacementSide placementSide,
        double gap,
        double freeMinX,
        double freeMaxX,
        double freeMinY,
        double freeMaxY,
        IReadOnlyList<ReservedRect>? occupied = null,
        IEnumerable<ReservedRect>? proposed = null)
    {
        occupied ??= System.Array.Empty<ReservedRect>();
        proposed ??= System.Array.Empty<ReservedRect>();
        var zone = placementSide switch
        {
            SectionPlacementSide.Top => RelativePlacement.Top,
            SectionPlacementSide.Bottom => RelativePlacement.Bottom,
            _ => throw new System.ArgumentOutOfRangeException(nameof(placementSide), "Only Top/Bottom section probing is supported.")
        };

        var ok = TryFindHorizontalSectionRectInSearchArea(
            section,
            section.Width,
            section.Height,
            frontRect,
            anchorRect,
            zone,
            CreateSearchArea(freeMinX, freeMaxX, freeMinY, freeMaxY),
            gap,
            occupied,
            proposed,
            out var rect,
            out var failure);

        if (ok || failure == null)
            return new HorizontalSectionProbeResult(ok, rect, string.Empty, string.Empty, string.Empty, string.Empty);

        var conflicts = new List<DrawingFitConflict>();
        DiagnoseSectionStackFailure(conflicts, zone, failure, occupied);
        var diagnostic = conflicts.FirstOrDefault()?.Conflicts.FirstOrDefault();

        return new HorizontalSectionProbeResult(
            success: false,
            rect,
            failure.Value.RejectReason,
            failure.Value.ConflictReason,
            diagnostic?.Type ?? string.Empty,
            diagnostic?.Target ?? string.Empty);
    }

    internal static VerticalSectionProbeResult ProbeVerticalSectionCandidate(
        View section,
        ReservedRect frontRect,
        ReservedRect anchorRect,
        SectionPlacementSide placementSide,
        double gap,
        double freeMinX,
        double freeMaxX,
        double freeMinY,
        double freeMaxY,
        IReadOnlyList<ReservedRect>? occupied = null,
        IEnumerable<ReservedRect>? proposed = null)
    {
        occupied ??= System.Array.Empty<ReservedRect>();
        proposed ??= System.Array.Empty<ReservedRect>();

        var zone = placementSide switch
        {
            SectionPlacementSide.Left => RelativePlacement.Left,
            SectionPlacementSide.Right => RelativePlacement.Right,
            _ => throw new System.ArgumentOutOfRangeException(nameof(placementSide), "Only Left/Right section probing is supported.")
        };

        var width = section.Width;
        var height = section.Height;
        if (!TryCreateVerticalSectionRect(
                frontRect,
                anchorRect,
                zone,
                width,
                height,
                gap,
                CreateSearchArea(freeMinX, freeMaxX, freeMinY, freeMaxY),
                out var rect))
        {
            var failure = new SectionStackFailureInfo(section, new ReservedRect(0, 0, 0, 0), "out-of-bounds-y", "out-of-bounds");
            var conflicts = new List<DrawingFitConflict>();
            DiagnoseSectionStackFailure(conflicts, zone, failure, occupied);
            var diagnostic = conflicts.FirstOrDefault()?.Conflicts.FirstOrDefault();

            return new VerticalSectionProbeResult(
                success: false,
                rect,
                failure.RejectReason,
                failure.ConflictReason,
                diagnostic?.Type ?? string.Empty,
                diagnostic?.Target ?? string.Empty);
        }

        if (TryValidateSectionCandidateRect(rect, CreateSearchArea(freeMinX, freeMaxX, freeMinY, freeMaxY), occupied, proposed, out var reason))
            return new VerticalSectionProbeResult(true, rect, string.Empty, string.Empty, string.Empty, string.Empty);

        var validationFailure = new SectionStackFailureInfo(section, rect, reason, reason);
        var validationConflicts = new List<DrawingFitConflict>();
        DiagnoseSectionStackFailure(validationConflicts, zone, validationFailure, occupied);
        var validationDiagnostic = validationConflicts.FirstOrDefault()?.Conflicts.FirstOrDefault();

        return new VerticalSectionProbeResult(
            success: false,
            rect,
            validationFailure.RejectReason,
            validationFailure.ConflictReason,
            validationDiagnostic?.Type ?? string.Empty,
            validationDiagnostic?.Target ?? string.Empty);
    }

    private static IEnumerable<double> EnumerateHorizontalSectionCandidateMinXs(
        double preferredMinX,
        double? currentMinX,
        double width,
        double minY,
        double maxY,
        ViewPlacementSearchArea searchArea,
        IReadOnlyList<ReservedRect> blockers)
    {
        var candidates = new List<double>
        {
            preferredMinX,
        };

        if (currentMinX.HasValue)
            candidates.Add(currentMinX.Value);

        candidates.Add(searchArea.FreeMinX);
        candidates.Add(searchArea.FreeMaxX - width);

        foreach (var blocker in blockers)
        {
            if (blocker.MinY >= maxY || blocker.MaxY <= minY)
                continue;

            candidates.Add(blocker.MaxX);
            candidates.Add(blocker.MinX - width);
        }

        return candidates
            .Distinct()
            .OrderBy(candidate => candidate.Equals(preferredMinX) ? double.NegativeInfinity : System.Math.Abs(candidate - preferredMinX))
            .ThenBy(candidate => currentMinX.HasValue && candidate.Equals(currentMinX.Value) ? -1 : 0)
            .ThenBy(candidate => candidate);
    }

    private static double PushHorizontalSectionCandidateRight(
        double minX,
        double minY,
        double maxY,
        double width,
        IReadOnlyList<ReservedRect> occupied)
    {
        var adjustedMinX = minX;
        foreach (var blocked in occupied)
        {
            if (blocked.MinY < maxY && blocked.MaxY > minY
                && blocked.MaxX > adjustedMinX && blocked.MinX < adjustedMinX + width
                && blocked.MaxX < adjustedMinX + width)
            {
                adjustedMinX = System.Math.Max(adjustedMinX, blocked.MaxX);
            }
        }

        return adjustedMinX;
    }

    private static bool IsHorizontalSectionCandidateInSearchArea(
        double minX,
        double width,
        ViewPlacementSearchArea searchArea)
        => minX >= searchArea.FreeMinX && minX + width <= searchArea.FreeMaxX;

    private static double? TryGetCurrentHorizontalSectionMinX(View section, double width)
    {
        if (section.Origin == null)
            return null;

        return section.Origin.X - (width / 2.0);
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

        var topology = context.Topology;
        var baseView = topology.BaseView;
        if (baseView == null)
            return false;
        var neighbors = topology.Neighbors!;
        var sections = topology.SemanticViews.Sections;
        var detailViews = topology.SemanticViews.Details;
        var detailRelations = topology.DetailRelations.All.ToList();
        var otherViews = topology.SemanticViews.Other;
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
            $"baseProjected={topology.SemanticViews.BaseProjected.Count} sections={sections.Count} details={detailViews.Count} other={otherViews.Count}");
        PerfTrace.Write(
            "api-view",
            "view_topology_summary",
            0,
            $"base={baseView.GetIdentifier().ID} residualProjected={topology.ResidualProjected.Count} detailRelations={topology.DetailRelations.Count}");

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
        TraceProjectedGroupPlannerIfFeasible(
            context,
            neighbors,
            leftSections,
            rightSections,
            topSections,
            bottomSections,
            secondaryViews);

        var strictRetry = TryPlanStrictLayout(context, neighbors, leftSections, rightSections, topSections, bottomSections, detailRelations, secondaryViews, out planned);
        PerfTrace.Write("api-view", "front_arrange_try", 0, $"mode=strict-retry result={(strictRetry ? "ok" : "failed")}");
        return strictRetry;
    }

    private static double GetCurrentScale(DrawingArrangeContext context)
        => context.Views.Select(v => v.Attributes.Scale).FirstOrDefault(s => s > 0);

    private static void TraceProjectedGroupPlannerIfFeasible(
        DrawingArrangeContext context,
        NeighborSet neighbors,
        IReadOnlyList<View> leftSections,
        IReadOnlyList<View> rightSections,
        IReadOnlyList<View> topSections,
        IReadOnlyList<View> bottomSections,
        IReadOnlyList<View> secondaryViews)
    {
        if (!PerfTrace.IsActive)
            return;

        var frames = context.Views
            .Select(view => (
                DrawingArrangeContextSizing.GetWidth(context, view),
                DrawingArrangeContextSizing.GetHeight(context, view)))
            .ToList();
        var relaxedPacking = DrawingPackingEstimator.CheckRelaxedMaxRectsFit(
            frames,
            context.SheetWidth,
            context.SheetHeight,
            context.Margin,
            context.Gap,
            context.ReservedAreas);

        PerfTrace.Write(
            "api-view",
            "projected_group_planner_gate",
            0,
            $"strict=failed relaxed=failed relaxedPackingFits={(relaxedPacking.Fits ? 1 : 0)} frames={relaxedPacking.FrameCount} reserved={relaxedPacking.ReservedAreaCount} order={relaxedPacking.Order} heuristic={relaxedPacking.Heuristic} attempts={relaxedPacking.Attempts}");

        if (!relaxedPacking.Fits)
            return;

        ProjectedGroupLayoutPlanner.Trace(
            context,
            neighbors,
            leftSections,
            rightSections,
            topSections,
            bottomSections,
            secondaryViews,
            relaxedPacking);
    }

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

        if (!TrySelectBaseRectWithBudgets(
                context,
                neighbors,
                leftSections,
                rightSections,
                topSections,
                bottomSections,
                blocked,
                includeRelaxedCandidates: false,
                requireAllStrictNeighborsFit: true,
                out var baseRect,
                out var baseDecision))
        {
            TracePlanReject(
                "strict",
                string.IsNullOrEmpty(baseDecision.RejectReason) ? "base" : baseDecision.RejectReason,
                context,
                planned,
                HasArea(baseDecision.RejectRect) ? baseDecision.RejectRect : null);
            return false;
        }

        AddPlannedRect(planned, baseView, baseRect);

        var occupied = new List<ReservedRect>(blocked) { baseRect };
        var mainSkeleton = new MainSkeletonPlacementState();

        var searchArea = new ViewPlacementSearchArea(baseRect, freeArea.minX, freeArea.maxX, freeArea.minY, freeArea.maxY);

        foreach (var spec in CreateStrictMainSkeletonNeighborSpecs(
                     top,
                     bottom,
                     leftNeighbor,
                     rightNeighbor,
                     topWidth,
                     topHeight,
                     bottomWidth,
                     bottomHeight,
                     leftNeighborWidth,
                     leftNeighborHeight,
                     rightNeighborWidth,
                     rightNeighborHeight))
        {
            if (!TryPlaceOptionalStrictMainSkeletonNeighbor(
                    context,
                    spec,
                    searchArea,
                    gap,
                    occupied,
                    planned,
                    mainSkeleton))
                return false;
        }

        if (!TryValidateMainSkeletonSpacing(
                baseRect,
                mainSkeleton,
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
        var leftSectionPartition = PartitionStandardSections(context, baseRect, leftSections, SectionPlacementSide.Left);
        var rightSectionPartition = PartitionStandardSections(context, baseRect, rightSections, SectionPlacementSide.Right);
        var topSectionPartition = PartitionStandardSections(context, baseRect, topSections, SectionPlacementSide.Top);
        var bottomSectionPartition = PartitionStandardSections(context, baseRect, bottomSections, SectionPlacementSide.Bottom);
        if (!TryPlaceVerticalSectionStackWithFallback(
            context,
            leftSectionPartition.Normal,
            baseRect,
            leftAnchor,
            rightAnchor,
            RelativePlacement.Left,
            searchArea,
            gap,
            occupied,
            planned)
            && !TryPlaceDegradedStandardSections(
                context,
                leftSectionPartition.Normal,
                baseRect,
                leftAnchor,
                rightAnchor,
                RelativePlacement.Left,
                searchArea,
                gap,
                occupied,
                planned))
            return false;
        if (!TryPlaceOversizedStandardSections(
                context,
                leftSectionPartition.Oversized,
                baseRect,
                leftAnchor,
                rightAnchor,
                RelativePlacement.Left,
                searchArea,
                gap,
                occupied,
                planned))
            return false;
        if (!TryPlaceVerticalSectionStackWithFallback(
            context,
            rightSectionPartition.Normal,
            baseRect,
            rightAnchor,
            leftAnchor,
            RelativePlacement.Right,
            searchArea,
            gap,
            occupied,
            planned)
            && !TryPlaceDegradedStandardSections(
                context,
                rightSectionPartition.Normal,
                baseRect,
                rightAnchor,
                leftAnchor,
                RelativePlacement.Right,
                searchArea,
                gap,
                occupied,
                planned))
            return false;
        if (!TryPlaceOversizedStandardSections(
                context,
                rightSectionPartition.Oversized,
                baseRect,
                rightAnchor,
                leftAnchor,
                RelativePlacement.Right,
                searchArea,
                gap,
                occupied,
                planned))
            return false;

        if (!TryPlaceHorizontalSectionStackWithFallback(
            context,
            topSectionPartition.Normal,
            baseRect,
            mainSkeleton.GetAnchorOrBase("top", baseRect),
            mainSkeleton.GetAnchorOrBase("bottom", baseRect),
            RelativePlacement.Top,
            searchArea,
            gap,
            occupied,
            planned)
            && !TryPlaceDegradedStandardSections(
                context,
                topSectionPartition.Normal,
                baseRect,
                mainSkeleton.GetAnchorOrBase("top", baseRect),
                mainSkeleton.GetAnchorOrBase("bottom", baseRect),
                RelativePlacement.Top,
                searchArea,
                gap,
                occupied,
                planned))
            return false;
        if (!TryPlaceOversizedStandardSections(
                context,
                topSectionPartition.Oversized,
                baseRect,
                mainSkeleton.GetAnchorOrBase("top", baseRect),
                mainSkeleton.GetAnchorOrBase("bottom", baseRect),
                RelativePlacement.Top,
                searchArea,
                gap,
                occupied,
                planned))
            return false;
        if (!TryPlaceHorizontalSectionStackWithFallback(
            context,
            bottomSectionPartition.Normal,
            baseRect,
            mainSkeleton.GetAnchorOrBase("bottom", baseRect),
            mainSkeleton.GetAnchorOrBase("top", baseRect),
            RelativePlacement.Bottom,
            searchArea,
            gap,
            occupied,
            planned)
            && !TryPlaceDegradedStandardSections(
                context,
                bottomSectionPartition.Normal,
                baseRect,
                mainSkeleton.GetAnchorOrBase("bottom", baseRect),
                mainSkeleton.GetAnchorOrBase("top", baseRect),
                RelativePlacement.Bottom,
                searchArea,
                gap,
                occupied,
                planned))
            return false;
        if (!TryPlaceOversizedStandardSections(
                context,
                bottomSectionPartition.Oversized,
                baseRect,
                mainSkeleton.GetAnchorOrBase("bottom", baseRect),
                mainSkeleton.GetAnchorOrBase("top", baseRect),
                RelativePlacement.Bottom,
                searchArea,
                gap,
                occupied,
                planned))
            return false;

        TryPlaceDetailViews(
            context,
            detailRelations,
            searchArea,
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

        if (!TrySelectBaseRectWithBudgets(
                context,
                neighbors,
                leftSections,
                rightSections,
                topSections,
                bottomSections,
                blocked,
                includeRelaxedCandidates: true,
                requireAllStrictNeighborsFit: false,
                out var baseRect,
                out var baseDecision))
        {
            TracePlanReject(
                "relaxed",
                string.IsNullOrEmpty(baseDecision.RejectReason) ? "base" : baseDecision.RejectReason,
                context,
                planned,
                HasArea(baseDecision.RejectRect) ? baseDecision.RejectRect : null);
            return false;
        }

        AddPlannedRect(planned, baseView, baseRect);
        var occupied = new List<ReservedRect>(blocked) { baseRect };

        var deferred = new List<View>(secondaryViews);
        var mainSkeleton = new MainSkeletonPlacementState();
        var deferredMainSkeletonRoles = new List<string>();

        var searchArea = new ViewPlacementSearchArea(baseRect, freeMinX, freeMaxX, freeMinY, freeMaxY);

        foreach (var spec in CreateMainSkeletonNeighborSpecs(top, bottom, leftNeighbor, rightNeighbor))
        {
            TryPlaceOptionalRelaxedMainSkeletonNeighbor(
                context,
                spec,
                searchArea,
                occupied,
                planned,
                deferred,
                mainSkeleton);
        }

        while (!TryValidateMainSkeletonSpacing(
                   baseRect,
                   mainSkeleton,
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
        var leftSectionPartition = PartitionStandardSections(context, baseRect, leftSections, SectionPlacementSide.Left);
        var rightSectionPartition = PartitionStandardSections(context, baseRect, rightSections, SectionPlacementSide.Right);
        var topSectionPartition = PartitionStandardSections(context, baseRect, topSections, SectionPlacementSide.Top);
        var bottomSectionPartition = PartitionStandardSections(context, baseRect, bottomSections, SectionPlacementSide.Bottom);
        if (!TryPlaceVerticalSectionStackWithFallback(
            context,
            leftSectionPartition.Normal,
            baseRect,
            leftPlacedAnchor,
            rightPlacedAnchor,
            RelativePlacement.Left,
            searchArea,
            context.Gap,
            occupied,
            planned)
            && !TryPlaceDegradedStandardSections(
                context,
                leftSectionPartition.Normal,
                baseRect,
                leftPlacedAnchor,
                rightPlacedAnchor,
                RelativePlacement.Left,
                searchArea,
                context.Gap,
                occupied,
                planned))
            return false;
        if (!TryPlaceOversizedStandardSections(
                context,
                leftSectionPartition.Oversized,
                baseRect,
                leftPlacedAnchor,
                rightPlacedAnchor,
                RelativePlacement.Left,
                searchArea,
                context.Gap,
                occupied,
                planned))
            return false;
        if (!TryPlaceVerticalSectionStackWithFallback(
            context,
            rightSectionPartition.Normal,
            baseRect,
            rightPlacedAnchor,
            leftPlacedAnchor,
            RelativePlacement.Right,
            searchArea,
            context.Gap,
            occupied,
            planned)
            && !TryPlaceDegradedStandardSections(
                context,
                rightSectionPartition.Normal,
                baseRect,
                rightPlacedAnchor,
                leftPlacedAnchor,
                RelativePlacement.Right,
                searchArea,
                context.Gap,
                occupied,
                planned))
            return false;
        if (!TryPlaceOversizedStandardSections(
                context,
                rightSectionPartition.Oversized,
                baseRect,
                rightPlacedAnchor,
                leftPlacedAnchor,
                RelativePlacement.Right,
                searchArea,
                context.Gap,
                occupied,
                planned))
            return false;

        if (!TryPlaceHorizontalSectionStackWithFallback(
            context,
            topSectionPartition.Normal,
            baseRect,
            mainSkeleton.GetAnchorOrBase("top", baseRect),
            mainSkeleton.GetAnchorOrBase("bottom", baseRect),
            RelativePlacement.Top,
            searchArea,
            context.Gap,
            occupied,
            planned)
            && !TryPlaceDegradedStandardSections(
                context,
                topSectionPartition.Normal,
                baseRect,
                mainSkeleton.GetAnchorOrBase("top", baseRect),
                mainSkeleton.GetAnchorOrBase("bottom", baseRect),
                RelativePlacement.Top,
                searchArea,
                context.Gap,
                occupied,
                planned))
            return false;
        if (!TryPlaceOversizedStandardSections(
                context,
                topSectionPartition.Oversized,
                baseRect,
                mainSkeleton.GetAnchorOrBase("top", baseRect),
                mainSkeleton.GetAnchorOrBase("bottom", baseRect),
                RelativePlacement.Top,
                searchArea,
                context.Gap,
                occupied,
                planned))
            return false;
        if (!TryPlaceHorizontalSectionStackWithFallback(
            context,
            bottomSectionPartition.Normal,
            baseRect,
            mainSkeleton.GetAnchorOrBase("bottom", baseRect),
            mainSkeleton.GetAnchorOrBase("top", baseRect),
            RelativePlacement.Bottom,
            searchArea,
            context.Gap,
            occupied,
            planned)
            && !TryPlaceDegradedStandardSections(
                context,
                bottomSectionPartition.Normal,
                baseRect,
                mainSkeleton.GetAnchorOrBase("bottom", baseRect),
                mainSkeleton.GetAnchorOrBase("top", baseRect),
                RelativePlacement.Bottom,
                searchArea,
                context.Gap,
                occupied,
                planned))
            return false;
        if (!TryPlaceOversizedStandardSections(
                context,
                bottomSectionPartition.Oversized,
                baseRect,
                mainSkeleton.GetAnchorOrBase("bottom", baseRect),
                mainSkeleton.GetAnchorOrBase("top", baseRect),
                RelativePlacement.Bottom,
                searchArea,
                context.Gap,
                occupied,
                planned))
            return false;

        TryPlaceDetailViews(
            context,
            detailRelations,
            searchArea,
            context.Gap,
            occupied,
            planned);

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

    private static bool TryPlaceVerticalSectionStack(
        DrawingArrangeContext context,
        IReadOnlyList<View> sectionViews,
        ReservedRect frontRect,
        ReservedRect anchorRect,
        RelativePlacement zone,
        ViewPlacementSearchArea searchArea,
        double gap,
        List<ReservedRect> occupied,
        List<PlannedPlacement> planned,
        SectionPlacementSide preferredPlacementSide,
        SectionPlacementSide actualPlacementSide)
    {
        if (sectionViews.Count == 0)
            return true;

        if (!TryPlanVerticalSectionStack(
                context,
                sectionViews,
                frontRect,
                anchorRect,
                zone,
                searchArea,
                gap,
                occupied,
                out var proposed,
                out var failure))
        {
            TraceVerticalSectionStackFailure(context, zone, searchArea, failure);
            return false;
        }

        CommitSectionPlacements(proposed, planned, occupied, preferredPlacementSide, actualPlacementSide);

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
        => TryPlaceVerticalSectionStack(
            context,
            sectionViews,
            frontRect,
            anchorRect,
            zone,
            new ViewPlacementSearchArea(frontRect, freeMinX, freeMaxX, freeMinY, freeMaxY),
            gap,
            occupied,
            planned,
            preferredPlacementSide,
            actualPlacementSide);

    private static bool TryPlaceHorizontalSectionStack(
        DrawingArrangeContext context,
        IReadOnlyList<View> horizontalSections,
        ReservedRect frontRect,
        ReservedRect anchorRect,
        RelativePlacement zone,
        ViewPlacementSearchArea searchArea,
        double gap,
        List<ReservedRect> occupied,
        List<PlannedPlacement> planned,
        SectionPlacementSide preferredPlacementSide,
        SectionPlacementSide actualPlacementSide)
    {
        if (horizontalSections.Count == 0)
            return true;

        if (!TryPlanHorizontalSectionStack(
                context,
                horizontalSections,
                frontRect,
                anchorRect,
                zone,
                searchArea,
                gap,
                occupied,
                out var proposed,
                out var failure))
        {
            TraceHorizontalSectionStackFailure(context, zone, searchArea, occupied, planned, failure);
            return false;
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
        => TryPlaceHorizontalSectionStack(
            context,
            horizontalSections,
            frontRect,
            anchorRect,
            zone,
            new ViewPlacementSearchArea(frontRect, freeMinX, freeMaxX, freeMinY, freeMaxY),
            gap,
            occupied,
            planned,
            preferredPlacementSide,
            actualPlacementSide);

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

    private static bool TryPlaceHorizontalSectionStackWithFallback(
        DrawingArrangeContext context,
        IReadOnlyList<View> sectionViews,
        ReservedRect frontRect,
        ReservedRect preferredAnchorRect,
        ReservedRect fallbackAnchorRect,
        RelativePlacement preferredZone,
        ViewPlacementSearchArea searchArea,
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
                    searchArea,
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
        ViewPlacementSearchArea searchArea,
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
                    searchArea,
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

    private static bool TryPlaceOversizedStandardSections(
        DrawingArrangeContext context,
        IReadOnlyList<View> sectionViews,
        ReservedRect frontRect,
        ReservedRect preferredAnchorRect,
        ReservedRect fallbackAnchorRect,
        RelativePlacement preferredZone,
        ViewPlacementSearchArea searchArea,
        double gap,
        List<ReservedRect> occupied,
        List<PlannedPlacement> planned)
    {
        if (sectionViews.Count == 0)
            return true;

        PerfTrace.Write(
            "api-view",
            "section_oversized_degraded_attempt",
            0,
            $"preferred={preferredZone} sections=[{FormatSectionIds(sectionViews)}]");

        var ok = TryPlaceDegradedStandardSections(
            context,
            sectionViews,
            frontRect,
            preferredAnchorRect,
            fallbackAnchorRect,
            preferredZone,
            searchArea,
            gap,
            occupied,
            planned);

        PerfTrace.Write(
            "api-view",
            ok ? "section_oversized_degraded_result" : "section_oversized_degraded_reject",
            0,
            $"preferred={preferredZone} sections=[{FormatSectionIds(sectionViews)}]");

        return ok;
    }

    internal static bool TryPlaceDegradedStandardSections(
        DrawingArrangeContext context,
        IReadOnlyList<View> sectionViews,
        ReservedRect frontRect,
        ReservedRect preferredAnchorRect,
        ReservedRect fallbackAnchorRect,
        SectionPlacementSide preferredPlacementSide,
        double freeMinX,
        double freeMaxX,
        double freeMinY,
        double freeMaxY,
        double gap,
        List<ReservedRect> occupied,
        List<PlannedPlacement> planned)
        => TryPlaceDegradedStandardSections(
            context,
            sectionViews,
            frontRect,
            preferredAnchorRect,
            fallbackAnchorRect,
            ToRelativePlacement(preferredPlacementSide),
            new ViewPlacementSearchArea(frontRect, freeMinX, freeMaxX, freeMinY, freeMaxY),
            gap,
            occupied,
            planned);

    private static bool TryPlaceDegradedStandardSections(
        DrawingArrangeContext context,
        IReadOnlyList<View> sectionViews,
        ReservedRect frontRect,
        ReservedRect preferredAnchorRect,
        ReservedRect fallbackAnchorRect,
        RelativePlacement preferredZone,
        ViewPlacementSearchArea searchArea,
        double gap,
        List<ReservedRect> occupied,
        List<PlannedPlacement> planned)
    {
        if (sectionViews.Count == 0)
            return true;

        var preferredPlacementSide = ToPlacementSide(preferredZone);
        var fallbackZone = GetFallbackZone(preferredZone);
        var fallbackPlacementSide = GetFallbackPlacementSide(preferredPlacementSide);
        var preferredCurrentAnchor = preferredAnchorRect;
        var fallbackCurrentAnchor = fallbackAnchorRect;

        PerfTrace.Write(
            "api-view",
            "section_degraded_attempt",
            0,
            $"preferred={preferredZone} sections=[{FormatSectionIds(sectionViews)}]");

        foreach (var section in OrderSectionViewsForStack(context, sectionViews))
        {
            if (TryFindDegradedSectionRect(
                    context,
                    section,
                    frontRect,
                    preferredCurrentAnchor,
                    preferredZone,
                    searchArea,
                    gap,
                    occupied,
                    out var preferredRect,
                    out _))
            {
                AddPlannedAndOccupiedRect(planned, occupied, section, preferredRect, preferredPlacementSide, preferredPlacementSide);
                preferredCurrentAnchor = preferredRect;
                continue;
            }

            if (TryFindDegradedSectionRect(
                    context,
                    section,
                    frontRect,
                    fallbackCurrentAnchor,
                    fallbackZone,
                    searchArea,
                    gap,
                    occupied,
                    out var fallbackRect,
                    out _))
            {
                AddPlannedAndOccupiedRect(planned, occupied, section, fallbackRect, preferredPlacementSide, fallbackPlacementSide);
                fallbackCurrentAnchor = fallbackRect;
                continue;
            }

            PerfTrace.Write(
                "api-view",
                "section_degraded_reject",
                0,
                $"preferred={preferredZone} fallback={fallbackZone} section={section.GetIdentifier().ID}");
            return false;
        }

        PerfTrace.Write(
            "api-view",
            "section_degraded_result",
            0,
            $"preferred={preferredZone} sections=[{FormatSectionIds(sectionViews)}]");
        return true;
    }

    private static RelativePlacement ToRelativePlacement(SectionPlacementSide placementSide)
        => placementSide switch
        {
            SectionPlacementSide.Top => RelativePlacement.Top,
            SectionPlacementSide.Bottom => RelativePlacement.Bottom,
            SectionPlacementSide.Left => RelativePlacement.Left,
            SectionPlacementSide.Right => RelativePlacement.Right,
            _ => throw new System.ArgumentOutOfRangeException(nameof(placementSide))
        };

    private static bool TryFindDegradedSectionRect(
        DrawingArrangeContext context,
        View section,
        ReservedRect frontRect,
        ReservedRect anchorRect,
        RelativePlacement zone,
        ViewPlacementSearchArea searchArea,
        double gap,
        IReadOnlyList<ReservedRect> occupied,
        out ReservedRect rect,
        out SectionStackFailureInfo? failure)
    {
        if (zone is RelativePlacement.Top or RelativePlacement.Bottom)
        {
            return TryFindHorizontalSectionRectInSearchArea(
                context,
                section,
                frontRect,
                anchorRect,
                zone,
                searchArea,
                gap,
                occupied,
                System.Array.Empty<ReservedRect>(),
                out rect,
                out failure);
        }

        var width = DrawingArrangeContextSizing.GetWidth(context, section);
        var height = DrawingArrangeContextSizing.GetHeight(context, section);
        if (!TryCreateVerticalSectionRect(
                frontRect,
                anchorRect,
                zone,
                width,
                height,
                gap,
                searchArea,
                out rect))
        {
            failure = new SectionStackFailureInfo(section, new ReservedRect(0, 0, 0, 0), "out-of-bounds-y", "out-of-bounds");
            return false;
        }

        if (TryValidateSectionCandidateRect(rect, searchArea, occupied, System.Array.Empty<ReservedRect>(), out var reason))
        {
            failure = null;
            return true;
        }

        failure = new SectionStackFailureInfo(
            section,
            rect,
            reason == "out-of-bounds" ? "out-of-bounds-y" : "intersects-view",
            reason);
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

    private static ReservedRect? FindCenteredRelativeRectInSearchArea(
        ReservedRect anchorRect,
        RelativePlacement placement,
        double width,
        double height,
        double gap,
        ViewPlacementSearchArea searchArea,
        IReadOnlyList<ReservedRect> occupied)
    {
        if (!TryCreateCenteredRelativeRect(anchorRect, placement, width, height, gap, out var rect))
            return null;

        return TryValidateMainSkeletonNeighborRect(rect, searchArea, occupied)
            ? rect
            : null;
    }

    private static ReservedRect? FindRelativeRectInSearchArea(
        DrawingArrangeContext context,
        View view,
        ReservedRect anchorRect,
        ViewPlacementSearchArea searchArea,
        IReadOnlyList<ReservedRect> occupied,
        RelativePlacement placement)
        => FindRelativeRect(
            context,
            view,
            anchorRect,
            searchArea,
            context.Gap,
            occupied,
            placement);

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
        => ViewPlacementValidator.IntersectsAny(rect, others);

    private static bool Intersects(ReservedRect a, ReservedRect b)
        => ViewPlacementValidator.Intersects(a, b);

    private static bool IsWithinArea(ReservedRect rect, double minX, double maxX, double minY, double maxY)
        => ViewPlacementValidator.IsWithinArea(rect, minX, maxX, minY, maxY);

    private static string FormatValidationBlockers(IReadOnlyList<ViewPlacementBlocker> blockers)
        => blockers.Count == 0
            ? "none"
            : string.Join(
                ";",
                blockers.Select(blocker => blocker.Kind == ViewPlacementBlockerKind.View && blocker.ViewId.HasValue
                    ? $"{blocker.Kind}:{blocker.ViewId.Value}:[{blocker.Rect.MinX:F2},{blocker.Rect.MinY:F2},{blocker.Rect.MaxX:F2},{blocker.Rect.MaxY:F2}]"
                    : $"{blocker.Kind}:[{blocker.Rect.MinX:F2},{blocker.Rect.MinY:F2},{blocker.Rect.MaxX:F2},{blocker.Rect.MaxY:F2}]"));

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

    private static bool HasArea(ReservedRect rect)
        => rect.MaxX > rect.MinX && rect.MaxY > rect.MinY;

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

