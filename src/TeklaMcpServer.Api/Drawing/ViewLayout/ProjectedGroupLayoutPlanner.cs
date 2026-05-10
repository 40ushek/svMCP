using System;
using System.Collections.Generic;
using System.Linq;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using TeklaMcpServer.Api.Algorithms.Packing;
using TeklaMcpServer.Api.Diagnostics;
using TeklaMcpServer.Api.Drawing;

namespace TeklaMcpServer.Api.Drawing.ViewLayout;

internal static class ProjectedGroupLayoutPlanner
{
    private sealed class PlannerItem
    {
        public PlannerItem(View view, SectionPlacementSide preferredSide, bool strongProjection)
        {
            View = view;
            PreferredSide = preferredSide;
            StrongProjection = strongProjection;
        }

        public View View { get; }
        public SectionPlacementSide PreferredSide { get; }
        public bool StrongProjection { get; }
        public int Id => View.GetIdentifier().ID;
    }

    private sealed class ScenarioResult
    {
        public string Scenario { get; set; } = string.Empty;
        public bool Fits { get; set; }
        public string RejectReason { get; set; } = string.Empty;
        public int AddedCount { get; set; }
        public int DeferredCount { get; set; }
        public int FallbackPlacedCount { get; set; }
        public ReservedRect? BaseRect { get; set; }
        public List<int> AddedIds { get; } = new();
        public List<int> DeferredIds { get; } = new();
    }

    private sealed class VirtualPlacement
    {
        public VirtualPlacement(PlannerItem item, ReservedRect rect)
        {
            Item = item;
            Rect = rect;
        }

        public PlannerItem Item { get; }
        public ReservedRect Rect { get; set; }
    }

    private sealed class VirtualState
    {
        private readonly DrawingArrangeContext _context;

        public VirtualState(DrawingArrangeContext context, PlannerItem baseItem, ReservedRect baseRect)
        {
            _context = context;
            BaseItem = baseItem;
            BaseRect = baseRect;
            Placements = new Dictionary<int, VirtualPlacement>();
            Placements[baseItem.Id] = new VirtualPlacement(baseItem, baseRect);
            TopNextMinY = baseRect.MaxY + context.Gap;
            BottomNextMaxY = baseRect.MinY - context.Gap;
            RightNextMinY = baseRect.MinY;
            LeftNextMinY = baseRect.MinY;
            RightMinX = baseRect.MaxX + context.Gap;
            LeftMaxX = baseRect.MinX - context.Gap;
        }

        private VirtualState(
            DrawingArrangeContext context,
            PlannerItem baseItem,
            ReservedRect baseRect,
            double topNextMinY,
            double bottomNextMaxY,
            double rightNextMinY,
            double leftNextMinY,
            double rightMinX,
            double leftMaxX,
            Dictionary<int, VirtualPlacement> placements)
        {
            _context = context;
            BaseItem = baseItem;
            BaseRect = baseRect;
            TopNextMinY = topNextMinY;
            BottomNextMaxY = bottomNextMaxY;
            RightNextMinY = rightNextMinY;
            LeftNextMinY = leftNextMinY;
            RightMinX = rightMinX;
            LeftMaxX = leftMaxX;
            Placements = placements;
        }

        public PlannerItem BaseItem { get; }
        public ReservedRect BaseRect { get; private set; }
        public Dictionary<int, VirtualPlacement> Placements { get; }
        public double TopNextMinY { get; private set; }
        public double BottomNextMaxY { get; private set; }
        public double RightNextMinY { get; private set; }
        public double LeftNextMinY { get; private set; }
        public double RightMinX { get; private set; }
        public double LeftMaxX { get; private set; }

        public VirtualState Clone()
            => new(
                _context,
                BaseItem,
                BaseRect,
                TopNextMinY,
                BottomNextMaxY,
                RightNextMinY,
                LeftNextMinY,
                RightMinX,
                LeftMaxX,
                Placements.ToDictionary(
                    placement => placement.Key,
                    placement => new VirtualPlacement(placement.Value.Item, placement.Value.Rect)));

        public void Shift(double dx, double dy)
        {
            if (dx == 0 && dy == 0)
                return;

            foreach (var placement in Placements.Values)
                placement.Rect = ShiftRect(placement.Rect, dx, dy);

            BaseRect = ShiftRect(BaseRect, dx, dy);
            TopNextMinY += dy;
            BottomNextMaxY += dy;
            RightNextMinY += dy;
            LeftNextMinY += dy;
            RightMinX += dx;
            LeftMaxX += dx;
        }

        public ReservedRect CreateSideRect(PlannerItem item)
        {
            var width = DrawingArrangeContextSizing.GetWidth(_context, item.View);
            var height = DrawingArrangeContextSizing.GetHeight(_context, item.View);
            var baseCenterX = (BaseRect.MinX + BaseRect.MaxX) * 0.5;

            return item.PreferredSide switch
            {
                SectionPlacementSide.Top => ViewPlacementGeometryService.CreateCandidateRect(
                    item.View,
                    baseCenterX,
                    TopNextMinY + (height * 0.5),
                    width,
                    height),
                SectionPlacementSide.Bottom => ViewPlacementGeometryService.CreateCandidateRect(
                    item.View,
                    baseCenterX,
                    BottomNextMaxY - (height * 0.5),
                    width,
                    height),
                SectionPlacementSide.Right => ViewPlacementGeometryService.CreateCandidateRect(
                    item.View,
                    RightMinX + (width * 0.5),
                    RightNextMinY + (height * 0.5),
                    width,
                    height),
                SectionPlacementSide.Left => ViewPlacementGeometryService.CreateCandidateRect(
                    item.View,
                    LeftMaxX - (width * 0.5),
                    LeftNextMinY + (height * 0.5),
                    width,
                    height),
                _ => ViewPlacementGeometryService.CreateCandidateRect(
                    item.View,
                    baseCenterX,
                    TopNextMinY + (height * 0.5),
                    width,
                    height)
            };
        }

        public void Commit(PlannerItem item, ReservedRect rect)
        {
            Placements[item.Id] = new VirtualPlacement(item, rect);

            switch (item.PreferredSide)
            {
                case SectionPlacementSide.Top:
                    TopNextMinY = rect.MaxY + _context.Gap;
                    break;
                case SectionPlacementSide.Bottom:
                    BottomNextMaxY = rect.MinY - _context.Gap;
                    break;
                case SectionPlacementSide.Right:
                    RightNextMinY = rect.MaxY + _context.Gap;
                    break;
                case SectionPlacementSide.Left:
                    LeftNextMinY = rect.MaxY + _context.Gap;
                    break;
            }
        }

        private static ReservedRect ShiftRect(ReservedRect rect, double dx, double dy)
            => new(rect.MinX + dx, rect.MinY + dy, rect.MaxX + dx, rect.MaxY + dy);
    }

    public static void Trace(
        DrawingArrangeContext context,
        NeighborSet neighbors,
        IReadOnlyList<View> leftSections,
        IReadOnlyList<View> rightSections,
        IReadOnlyList<View> topSections,
        IReadOnlyList<View> bottomSections,
        IReadOnlyList<View> secondaryViews,
        DrawingPackingEstimator.RelaxedPackingResult relaxedPacking)
    {
        if (!PerfTrace.IsActive)
            return;

        var items = BuildItems(context, neighbors, leftSections, rightSections, topSections, bottomSections, secondaryViews);
        if (items.Projected.Count == 0)
        {
            PerfTrace.Write("api-view", "projected_group_planner_result", 0, "result=skipped reason=no-projected-items");
            return;
        }

        PerfTrace.Write(
            "api-view",
            "projected_group_planner_trigger",
            0,
            $"views={context.Views.Count} projected={items.Projected.Count} initialFallback={items.InitialFallback.Count} relaxedOrder={relaxedPacking.Order} heuristic={relaxedPacking.Heuristic} attempts={relaxedPacking.Attempts}");

        var scenarios = CreateScenarios(context, items.Projected);
        var results = new List<ScenarioResult>(scenarios.Count);

        foreach (var scenario in scenarios)
        {
            var result = RunScenario(context, items.BaseItem, scenario.Name, scenario.Items, items.InitialFallback);
            results.Add(result);
            PerfTrace.Write(
                "api-view",
                "projected_group_scenario_result",
                0,
                $"scenario={result.Scenario} result={(result.Fits ? "ok" : "reject")} added={result.AddedCount} deferred={result.DeferredCount} fallbackPlaced={result.FallbackPlacedCount} reason={result.RejectReason} baseRect={FormatRect(result.BaseRect)} addedIds={FormatIds(result.AddedIds)} deferredIds={FormatIds(result.DeferredIds)}");
        }

        var best = results
            .Where(result => result.Fits)
            .OrderBy(result => result.DeferredCount)
            .ThenByDescending(result => result.AddedCount)
            .ThenBy(result => result.Scenario, StringComparer.Ordinal)
            .FirstOrDefault();

        PerfTrace.Write(
            "api-view",
            "projected_group_planner_result",
            0,
            best != null
                ? $"result=ok selected={best.Scenario} candidates={results.Count} rejected={results.Count - results.Count(r => r.Fits)} added={best.AddedCount} deferred={best.DeferredCount} fallbackPlaced={best.FallbackPlacedCount} baseRect={FormatRect(best.BaseRect)}"
                : $"result=reject candidates={results.Count} rejected={results.Count} reason=no-valid-scenario");
    }

    private static (PlannerItem BaseItem, List<PlannerItem> Projected, List<PlannerItem> InitialFallback) BuildItems(
        DrawingArrangeContext context,
        NeighborSet neighbors,
        IReadOnlyList<View> leftSections,
        IReadOnlyList<View> rightSections,
        IReadOnlyList<View> topSections,
        IReadOnlyList<View> bottomSections,
        IReadOnlyList<View> secondaryViews)
    {
        var baseItem = new PlannerItem(neighbors.BaseView, SectionPlacementSide.Unknown, strongProjection: true);
        var byId = new Dictionary<int, PlannerItem>();

        void Add(View? view, SectionPlacementSide side, bool strong)
        {
            if (view == null)
                return;

            var id = view.GetIdentifier().ID;
            if (id == baseItem.Id || byId.ContainsKey(id))
                return;

            byId[id] = new PlannerItem(view, side, strong);
        }

        Add(neighbors.TopNeighbor, SectionPlacementSide.Top, strong: true);
        Add(neighbors.BottomNeighbor, SectionPlacementSide.Bottom, strong: true);
        Add(neighbors.SideNeighborLeft, SectionPlacementSide.Left, strong: true);
        Add(neighbors.SideNeighborRight, SectionPlacementSide.Right, strong: true);

        foreach (var view in topSections)
            Add(view, SectionPlacementSide.Top, strong: false);
        foreach (var view in bottomSections)
            Add(view, SectionPlacementSide.Bottom, strong: false);
        foreach (var view in leftSections)
            Add(view, SectionPlacementSide.Left, strong: false);
        foreach (var view in rightSections)
            Add(view, SectionPlacementSide.Right, strong: false);

        var initialFallback = new List<PlannerItem>();
        var fallbackIds = new HashSet<int>();
        foreach (var view in secondaryViews)
        {
            var id = view.GetIdentifier().ID;
            if (id == baseItem.Id || byId.ContainsKey(id))
                continue;

            initialFallback.Add(new PlannerItem(view, SectionPlacementSide.Unknown, strongProjection: false));
            fallbackIds.Add(id);
        }

        foreach (var view in context.Views)
        {
            var id = view.GetIdentifier().ID;
            if (id == baseItem.Id || byId.ContainsKey(id) || fallbackIds.Contains(id))
                continue;

            initialFallback.Add(new PlannerItem(view, SectionPlacementSide.Unknown, strongProjection: false));
            fallbackIds.Add(id);
        }

        return (baseItem, byId.Values.ToList(), initialFallback);
    }

    private static List<(string Name, IReadOnlyList<PlannerItem> Items)> CreateScenarios(DrawingArrangeContext context, IReadOnlyList<PlannerItem> items)
        => new()
        {
            ("TopFirst", OrderBySides(context, items, SectionPlacementSide.Top, SectionPlacementSide.Bottom, SectionPlacementSide.Left, SectionPlacementSide.Right)),
            ("BottomFirst", OrderBySides(context, items, SectionPlacementSide.Bottom, SectionPlacementSide.Top, SectionPlacementSide.Left, SectionPlacementSide.Right)),
            ("LeftFirst", OrderBySides(context, items, SectionPlacementSide.Left, SectionPlacementSide.Right, SectionPlacementSide.Top, SectionPlacementSide.Bottom)),
            ("RightFirst", OrderBySides(context, items, SectionPlacementSide.Right, SectionPlacementSide.Left, SectionPlacementSide.Top, SectionPlacementSide.Bottom)),
            ("VerticalFirst", OrderByAxis(context, items, verticalFirst: true)),
            ("HorizontalFirst", OrderByAxis(context, items, verticalFirst: false)),
            ("LargeFirst", items.OrderByDescending(item => GetArea(context, item)).ToList()),
            ("ProjectionFirst", items.OrderByDescending(item => item.StrongProjection).ThenByDescending(item => GetArea(context, item)).ToList()),
            ("CurrentOrder", items.ToList())
        };

    private static IReadOnlyList<PlannerItem> OrderBySides(DrawingArrangeContext context, IReadOnlyList<PlannerItem> items, params SectionPlacementSide[] sides)
    {
        var orderBySide = sides
            .Select((side, index) => (side, index))
            .ToDictionary(pair => pair.side, pair => pair.index);

        return items
            .OrderBy(item => orderBySide.TryGetValue(item.PreferredSide, out var index) ? index : int.MaxValue)
            .ThenByDescending(item => GetArea(context, item))
            .ToList();
    }

    private static IReadOnlyList<PlannerItem> OrderByAxis(DrawingArrangeContext context, IReadOnlyList<PlannerItem> items, bool verticalFirst)
        => items
            .OrderBy(item => GetAxisOrder(item.PreferredSide, verticalFirst))
            .ThenByDescending(item => GetArea(context, item))
            .ToList();

    private static int GetAxisOrder(SectionPlacementSide side, bool verticalFirst)
    {
        var isVertical = side is SectionPlacementSide.Top or SectionPlacementSide.Bottom;
        var isHorizontal = side is SectionPlacementSide.Left or SectionPlacementSide.Right;
        if (verticalFirst)
            return isVertical ? 0 : isHorizontal ? 1 : 2;

        return isHorizontal ? 0 : isVertical ? 1 : 2;
    }

    private static ScenarioResult RunScenario(
        DrawingArrangeContext context,
        PlannerItem baseItem,
        string scenarioName,
        IReadOnlyList<PlannerItem> orderedItems,
        IReadOnlyList<PlannerItem> initialFallback)
    {
        var result = new ScenarioResult { Scenario = scenarioName };
        var baseRect = CreateInitialBaseRect(context, baseItem.View);
        var state = new VirtualState(context, baseItem, baseRect);

        if (!TryValidateState(context, state, out var baseReject))
        {
            result.RejectReason = $"base:{baseReject}";
            result.BaseRect = state.BaseRect;
            return result;
        }

        var deferred = new List<PlannerItem>(initialFallback);

        foreach (var item in orderedItems)
        {
            var candidateState = state.Clone();
            var candidate = candidateState.CreateSideRect(item);
            candidateState.Commit(item, candidate);
            var shiftReject = string.Empty;
            var validateReject = string.Empty;

            if (TryShiftIntoSheet(context, candidateState, out shiftReject) &&
                TryValidateState(context, candidateState, out validateReject))
            {
                state = candidateState;
                result.AddedIds.Add(item.Id);
                continue;
            }

            deferred.Add(item);
            result.DeferredIds.Add(item.Id);
            PerfTrace.Write(
                "api-view",
                "projected_group_add_reject",
                0,
                $"scenario={scenarioName} view={item.Id} preferred={item.PreferredSide} reason={(string.IsNullOrEmpty(shiftReject) ? validateReject : shiftReject)} rect={FormatRect(candidate)}");
        }

        result.AddedCount = result.AddedIds.Count;
        result.DeferredCount = deferred.Count;
        result.BaseRect = state.BaseRect;

        if (!TryPlaceFallbackViews(context, scenarioName, state, deferred, out var fallbackPlaced, out var fallbackReject))
        {
            result.RejectReason = $"fallback:{fallbackReject}";
            result.FallbackPlacedCount = fallbackPlaced;
            return result;
        }

        result.Fits = true;
        result.FallbackPlacedCount = fallbackPlaced;
        return result;
    }

    private static ReservedRect CreateInitialBaseRect(DrawingArrangeContext context, View baseView)
    {
        var width = DrawingArrangeContextSizing.GetWidth(context, baseView);
        var height = DrawingArrangeContextSizing.GetHeight(context, baseView);
        var centerX = context.Margin + ((context.SheetWidth - (2 * context.Margin)) * 0.5);
        var centerY = context.Margin + ((context.SheetHeight - (2 * context.Margin)) * 0.5);
        return ViewPlacementGeometryService.CreateCandidateRect(baseView, centerX, centerY, width, height);
    }

    private static bool TryShiftIntoSheet(DrawingArrangeContext context, VirtualState state, out string rejectReason)
    {
        rejectReason = string.Empty;
        var bounds = GetBounds(state.Placements.Values.Select(placement => placement.Rect));
        var minX = context.Margin;
        var maxX = context.SheetWidth - context.Margin;
        var minY = context.Margin;
        var maxY = context.SheetHeight - context.Margin;

        if (bounds.Width > maxX - minX || bounds.Height > maxY - minY)
        {
            rejectReason = "group-larger-than-sheet";
            return false;
        }

        var dx = bounds.MinX < minX ? minX - bounds.MinX : bounds.MaxX > maxX ? maxX - bounds.MaxX : 0.0;
        var dy = bounds.MinY < minY ? minY - bounds.MinY : bounds.MaxY > maxY ? maxY - bounds.MaxY : 0.0;
        state.Shift(dx, dy);

        var shiftedBounds = GetBounds(state.Placements.Values.Select(placement => placement.Rect));
        if (shiftedBounds.MinX < minX || shiftedBounds.MaxX > maxX || shiftedBounds.MinY < minY || shiftedBounds.MaxY > maxY)
        {
            rejectReason = "sheet-bounds";
            return false;
        }

        return true;
    }

    private static bool TryValidateState(DrawingArrangeContext context, VirtualState state, out string rejectReason)
    {
        rejectReason = string.Empty;
        var minX = context.Margin;
        var maxX = context.SheetWidth - context.Margin;
        var minY = context.Margin;
        var maxY = context.SheetHeight - context.Margin;

        foreach (var placement in state.Placements.Values)
        {
            var others = state.Placements
                .Where(other => other.Key != placement.Item.Id)
                .ToDictionary(other => other.Key, other => Inflate(other.Value.Rect, context.Gap));
            var validation = ViewPlacementValidator.Validate(
                placement.Rect,
                minX,
                maxX,
                minY,
                maxY,
                context.ReservedAreas,
                others);

            if (validation.Fits)
                continue;

            rejectReason = $"{validation.Reason}:view={placement.Item.Id}";
            return false;
        }

        return true;
    }

    private static bool TryPlaceFallbackViews(
        DrawingArrangeContext context,
        string scenarioName,
        VirtualState state,
        IReadOnlyList<PlannerItem> fallbackItems,
        out int placedCount,
        out string rejectReason)
    {
        placedCount = 0;
        rejectReason = string.Empty;
        if (fallbackItems.Count == 0)
            return true;

        var availableWidth = context.SheetWidth - (2 * context.Margin);
        var availableHeight = context.SheetHeight - (2 * context.Margin);
        if (availableWidth <= 0 || availableHeight <= 0)
        {
            rejectReason = "no-available-area";
            return false;
        }

        var blocked = context.ReservedAreas
            .Concat(state.Placements.Values.Select(placement => placement.Rect))
            .SelectMany(rect => ToBlockedRectangles(context, rect))
            .ToList();
        var packer = new MaxRectsBinPacker(availableWidth + context.Gap, availableHeight + context.Gap, allowRotation: false, blocked);

        foreach (var item in fallbackItems.OrderByDescending(item => GetArea(context, item)))
        {
            var width = DrawingArrangeContextSizing.GetWidth(context, item.View);
            var height = DrawingArrangeContextSizing.GetHeight(context, item.View);
            if (!packer.TryInsert(width + context.Gap, height + context.Gap, MaxRectsHeuristic.BestAreaFit, out var placement))
            {
                rejectReason = $"no-fallback-space:view={item.Id}";
                PerfTrace.Write(
                    "api-view",
                    "projected_group_fallback_result",
                    0,
                    $"scenario={scenarioName} view={item.Id} preferred={item.PreferredSide} actual=Packed result=reject reason=no-fallback-space");
                return false;
            }

            placedCount++;
            PerfTrace.Write(
                "api-view",
                "projected_group_fallback_result",
                0,
                $"scenario={scenarioName} view={item.Id} preferred={item.PreferredSide} actual=Packed result=ok placementFallbackUsed=1 rect=[{context.Margin + placement.X:F2},{context.SheetHeight - context.Margin - placement.Y - height:F2},{context.Margin + placement.X + width:F2},{context.SheetHeight - context.Margin - placement.Y:F2}]");
        }

        return true;
    }

    private static IEnumerable<PackedRectangle> ToBlockedRectangles(DrawingArrangeContext context, ReservedRect area)
    {
        var minX = Math.Max(context.Margin, area.MinX - context.Gap);
        var maxX = Math.Min(context.SheetWidth - context.Margin, area.MaxX + context.Gap);
        var minY = Math.Max(context.Margin, area.MinY - context.Gap);
        var maxY = Math.Min(context.SheetHeight - context.Margin, area.MaxY + context.Gap);

        if (maxX <= minX || maxY <= minY)
            yield break;

        yield return new PackedRectangle(
            minX - context.Margin,
            (context.SheetHeight - context.Margin) - maxY,
            maxX - minX,
            maxY - minY);
    }

    private static ReservedRect GetBounds(IEnumerable<ReservedRect> rects)
    {
        var list = rects.ToList();
        return new ReservedRect(
            list.Min(rect => rect.MinX),
            list.Min(rect => rect.MinY),
            list.Max(rect => rect.MaxX),
            list.Max(rect => rect.MaxY));
    }

    private static ReservedRect Inflate(ReservedRect rect, double amount)
        => new(rect.MinX - amount, rect.MinY - amount, rect.MaxX + amount, rect.MaxY + amount);

    private static double GetArea(DrawingArrangeContext context, PlannerItem item)
    {
        var width = DrawingArrangeContextSizing.GetWidth(context, item.View);
        var height = DrawingArrangeContextSizing.GetHeight(context, item.View);
        return width * height;
    }

    private static string FormatIds(IEnumerable<int> ids)
        => string.Join(",", ids);

    private static string FormatRect(ReservedRect? rect)
        => rect == null
            ? "[]"
            : $"[{rect.MinX:F2},{rect.MinY:F2},{rect.MaxX:F2},{rect.MaxY:F2}]";
}
