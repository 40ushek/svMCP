using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
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
        public string BaseCandidate { get; set; } = string.Empty;
        public double Margin { get; set; }
        public double Gap { get; set; }
        public bool Fits { get; set; }
        public string RejectReason { get; set; } = string.Empty;
        public int AddedCount { get; set; }
        public int DeferredCount { get; set; }
        public int FallbackPlacedCount { get; set; }
        public ReservedRect? BaseRect { get; set; }
        public double CompactnessRatio { get; set; } = double.MaxValue;
        public double BaseCenterDistanceRatio { get; set; } = double.MaxValue;
        public double PlacementSidePenalty { get; set; }
        public double EdgeMarginPenalty { get; set; }
        public double Score { get; set; } = double.MaxValue;
        public List<int> AddedIds { get; } = new();
        public List<int> DeferredIds { get; } = new();
        public VirtualState? FinalState { get; set; }
        public List<(PlannerItem Item, ReservedRect Rect)> FallbackPlacements { get; } = new();
    }

    private sealed class BaseRectCandidate
    {
        public BaseRectCandidate(string name, ReservedRect rect)
        {
            Name = name;
            Rect = rect;
        }

        public string Name { get; }
        public ReservedRect Rect { get; }
    }

    private readonly struct SpacingCandidate
    {
        public SpacingCandidate(double margin, double gap)
        {
            Margin = margin;
            Gap = gap;
        }

        public double Margin { get; }
        public double Gap { get; }
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
                SectionPlacementSide.Top => ViewPlacementGeometryService.CreateRectFromFrameCenter(
                    baseCenterX,
                    TopNextMinY + (height * 0.5),
                    width,
                    height),
                SectionPlacementSide.Bottom => ViewPlacementGeometryService.CreateRectFromFrameCenter(
                    baseCenterX,
                    BottomNextMaxY - (height * 0.5),
                    width,
                    height),
                SectionPlacementSide.Right => ViewPlacementGeometryService.CreateRectFromFrameCenter(
                    RightMinX + (width * 0.5),
                    RightNextMinY + (height * 0.5),
                    width,
                    height),
                SectionPlacementSide.Left => ViewPlacementGeometryService.CreateRectFromFrameCenter(
                    LeftMaxX - (width * 0.5),
                    LeftNextMinY + (height * 0.5),
                    width,
                    height),
                _ => ViewPlacementGeometryService.CreateRectFromFrameCenter(
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

    public static bool Fits(
        DrawingArrangeContext context,
        NeighborSet neighbors,
        IReadOnlyList<View> leftSections,
        IReadOnlyList<View> rightSections,
        IReadOnlyList<View> topSections,
        IReadOnlyList<View> bottomSections,
        IReadOnlyList<View> secondaryViews,
        DrawingPackingEstimator.RelaxedPackingResult relaxedPacking,
        bool trace)
    {
        var items = BuildItems(context, neighbors, leftSections, rightSections, topSections, bottomSections, secondaryViews);
        if (items.Projected.Count == 0)
        {
            if (trace)
                PerfTrace.Write("api-view", "projected_group_planner_result", 0, "result=skipped reason=no-projected-items");
            return false;
        }

        if (trace)
        {
            PerfTrace.Write(
                "api-view",
                "projected_group_planner_trigger",
                0,
                $"views={context.Views.Count} projected={items.Projected.Count} initialFallback={items.InitialFallback.Count} relaxedOrder={relaxedPacking.Order} heuristic={relaxedPacking.Heuristic} attempts={relaxedPacking.Attempts}");
        }

        var spacingCandidates = CreateSpacingCandidates(context);
        var results = new List<ScenarioResult>();

        foreach (var spacing in spacingCandidates)
        {
            var candidateContext = context.With(margin: spacing.Margin, gap: spacing.Gap);
            var scenarios = CreateScenarios(candidateContext, items.Projected);
            var baseCandidates = CreateBaseRectCandidates(candidateContext, items.BaseItem.View);
            if (baseCandidates.Count == 0)
                continue;

            if (trace)
            {
                PerfTrace.Write(
                    "api-view",
                    "projected_group_base_candidates",
                    0,
                    $"margin={spacing.Margin:F1} gap={spacing.Gap:F1} count={baseCandidates.Count} candidates={string.Join(";", baseCandidates.Select(candidate => $"{candidate.Name}:{FormatRect(candidate.Rect)}"))}");
            }

            foreach (var baseCandidate in baseCandidates)
            foreach (var scenario in scenarios)
            {
                var result = RunScenario(candidateContext, items.BaseItem, baseCandidate, scenario.Name, scenario.Items, items.InitialFallback, trace);
                results.Add(result);
                if (trace)
                {
                    PerfTrace.Write(
                        "api-view",
                        "projected_group_scenario_result",
                        0,
                        $"margin={result.Margin:F1} gap={result.Gap:F1} base={result.BaseCandidate} scenario={result.Scenario} result={(result.Fits ? "ok" : "reject")} added={result.AddedCount} deferred={result.DeferredCount} fallbackPlaced={result.FallbackPlacedCount} score={FormatScore(result.Score)} compactness={FormatScore(result.CompactnessRatio)} baseCenterDistance={FormatScore(result.BaseCenterDistanceRatio)} sidePenalty={FormatScore(result.PlacementSidePenalty)} edgePenalty={FormatScore(result.EdgeMarginPenalty)} reason={result.RejectReason} baseRect={FormatRect(result.BaseRect)} addedIds={FormatIds(result.AddedIds)} deferredIds={FormatIds(result.DeferredIds)}");
                }
            }
        }

        var best = results
            .Where(result => result.Fits)
            .OrderBy(result => result.DeferredCount)
            .ThenByDescending(result => result.AddedCount)
            .ThenByDescending(result => result.Margin)
            .ThenByDescending(result => result.Gap)
            .ThenBy(result => result.Score)
            .ThenBy(result => result.Scenario, StringComparer.Ordinal)
            .FirstOrDefault();

        if (trace)
        {
            PerfTrace.Write(
                "api-view",
                "projected_group_planner_result",
                0,
                best != null
                    ? $"result=ok selectedBase={best.BaseCandidate} selected={best.Scenario} margin={best.Margin:F1} gap={best.Gap:F1} candidates={results.Count} rejected={results.Count - results.Count(r => r.Fits)} added={best.AddedCount} deferred={best.DeferredCount} fallbackPlaced={best.FallbackPlacedCount} score={best.Score:F4} compactness={best.CompactnessRatio:F4} baseCenterDistance={best.BaseCenterDistanceRatio:F4} sidePenalty={best.PlacementSidePenalty:F4} edgePenalty={best.EdgeMarginPenalty:F4} baseRect={FormatRect(best.BaseRect)}"
                    : $"result=reject candidates={results.Count} rejected={results.Count} reason=no-valid-scenario");
        }

        return best != null;
    }

    public static List<BaseProjectedDrawingArrangeStrategy.PlannedPlacement>? Plan(
        DrawingArrangeContext context,
        NeighborSet neighbors,
        IReadOnlyList<View> leftSections,
        IReadOnlyList<View> rightSections,
        IReadOnlyList<View> topSections,
        IReadOnlyList<View> bottomSections,
        IReadOnlyList<View> secondaryViews,
        DrawingPackingEstimator.RelaxedPackingResult relaxedPacking)
    {
        var planSw = Stopwatch.StartNew();
        var items = BuildItems(context, neighbors, leftSections, rightSections, topSections, bottomSections, secondaryViews);
        if (items.Projected.Count == 0)
            return null;

        var results = new List<ScenarioResult>();
        var scenarioIndex = 0;
        foreach (var spacing in CreateSpacingCandidates(context))
        {
            var candidateContext = context.With(margin: spacing.Margin, gap: spacing.Gap);
            var scenarios = CreateScenarios(candidateContext, items.Projected);
            var baseCandidates = CreateBaseRectCandidates(candidateContext, items.BaseItem.View);

            foreach (var baseCandidate in baseCandidates)
            foreach (var scenario in scenarios)
            {
                scenarioIndex++;
                var result = RunScenario(
                    candidateContext,
                    items.BaseItem,
                    baseCandidate,
                    scenario.Name,
                    scenario.Items,
                    items.InitialFallback,
                    trace: false,
                    collectPlacements: true);
                results.Add(result);
            }
        }

        var best = results
            .Where(r => r.Fits)
            .OrderBy(r => r.DeferredCount)
            .ThenByDescending(r => r.AddedCount)
            .ThenByDescending(r => r.Margin)
            .ThenByDescending(r => r.Gap)
            .ThenBy(r => r.Score)
            .ThenBy(r => r.Scenario, StringComparer.Ordinal)
            .FirstOrDefault();

        if (best?.FinalState == null)
        {
            PerfTrace.Write(
                "api-view",
                "projected_group_plan_result",
                planSw.ElapsedMilliseconds,
                $"result=reject scenarios={scenarioIndex} rejected={results.Count} reason=no-valid-scenario");
            return null;
        }

        var planned = new List<BaseProjectedDrawingArrangeStrategy.PlannedPlacement>();
        foreach (var vp in best.FinalState.Placements.Values)
        {
            var cx = (vp.Rect.MinX + vp.Rect.MaxX) / 2.0;
            var cy = (vp.Rect.MinY + vp.Rect.MaxY) / 2.0;
            var preferred = vp.Item.PreferredSide == SectionPlacementSide.Unknown
                ? (SectionPlacementSide?)null
                : vp.Item.PreferredSide;
            planned.Add(new BaseProjectedDrawingArrangeStrategy.PlannedPlacement(
                vp.Item.View,
                cx,
                cy,
                preferred,
                preferred,
                best.Margin,
                best.Gap));
        }

        foreach (var (item, rect) in best.FallbackPlacements)
        {
            var cx = (rect.MinX + rect.MaxX) / 2.0;
            var cy = (rect.MinY + rect.MaxY) / 2.0;
            var preferred = item.PreferredSide == SectionPlacementSide.Unknown
                ? (SectionPlacementSide?)null
                : item.PreferredSide;
            var actual = InferActualPlacementSide(best.FinalState.BaseRect, rect);
            planned.Add(new BaseProjectedDrawingArrangeStrategy.PlannedPlacement(
                item.View,
                cx,
                cy,
                preferred,
                actual,
                best.Margin,
                best.Gap));
        }

        PerfTrace.Write(
            "api-view",
            "projected_group_plan_result",
            planSw.ElapsedMilliseconds,
            $"result=ok selectedBase={best.BaseCandidate} selected={best.Scenario} margin={best.Margin:F1} gap={best.Gap:F1} scenarios={scenarioIndex} added={best.AddedCount} deferred={best.DeferredCount} fallbackPlaced={best.FallbackPlacedCount} score={best.Score:F4} compactness={best.CompactnessRatio:F4} baseCenterDistance={best.BaseCenterDistanceRatio:F4} sidePenalty={best.PlacementSidePenalty:F4} edgePenalty={best.EdgeMarginPenalty:F4} views={planned.Count}");

        return planned;
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
            .ThenBy(item => item.StrongProjection ? 0 : 1)
            .ThenBy(item => GetViewTypeStackRank(item.View.ViewType, item.PreferredSide))
            .ThenByDescending(item => GetArea(context, item))
            .ToList();
    }

    private static IReadOnlyList<PlannerItem> OrderByAxis(DrawingArrangeContext context, IReadOnlyList<PlannerItem> items, bool verticalFirst)
        => items
            .OrderBy(item => GetAxisOrder(item.PreferredSide, verticalFirst))
            .ThenBy(item => item.StrongProjection ? 0 : 1)
            .ThenBy(item => GetViewTypeStackRank(item.View.ViewType, item.PreferredSide))
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

    private static IReadOnlyList<SpacingCandidate> CreateSpacingCandidates(DrawingArrangeContext context)
    {
        var raw = new[]
        {
            new SpacingCandidate(context.Margin, context.Gap),
            new SpacingCandidate(5, 4),
            new SpacingCandidate(8, 4),
            new SpacingCandidate(10, 4),
            new SpacingCandidate(10, 6)
        };

        var result = new List<SpacingCandidate>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in raw)
        {
            if (candidate.Margin < context.Margin || candidate.Gap < context.Gap)
                continue;

            if (candidate.Margin * 2 >= context.SheetWidth || candidate.Margin * 2 >= context.SheetHeight)
                continue;

            var key = $"{candidate.Margin:F3}:{candidate.Gap:F3}";
            if (seen.Add(key))
                result.Add(candidate);
        }

        return result.Count > 0
            ? result
            : new[] { new SpacingCandidate(context.Margin, context.Gap) };
    }

    private static ScenarioResult RunScenario(
        DrawingArrangeContext context,
        PlannerItem baseItem,
        BaseRectCandidate baseCandidate,
        string scenarioName,
        IReadOnlyList<PlannerItem> orderedItems,
        IReadOnlyList<PlannerItem> initialFallback,
        bool trace,
        bool collectPlacements = false)
    {
        var result = new ScenarioResult
        {
            Scenario = scenarioName,
            BaseCandidate = baseCandidate.Name,
            Margin = context.Margin,
            Gap = context.Gap
        };
        var state = new VirtualState(context, baseItem, baseCandidate.Rect);

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
            if (trace)
            {
                PerfTrace.Write(
                    "api-view",
                    "projected_group_add_reject",
                    0,
                    $"scenario={scenarioName} view={item.Id} preferred={item.PreferredSide} reason={(string.IsNullOrEmpty(shiftReject) ? validateReject : shiftReject)} rect={FormatRect(candidate)}");
            }
        }

        result.AddedCount = result.AddedIds.Count;
        result.DeferredCount = deferred.Count;
        result.BaseRect = state.BaseRect;

        var scoredFallbackRects = collectPlacements ? result.FallbackPlacements : new List<(PlannerItem Item, ReservedRect Rect)>();
        var fallbackRects = scoredFallbackRects;
        if (!TryPlaceFallbackViews(
                context,
                scenarioName,
                state,
                deferred,
                trace,
                out var fallbackPlaced,
                out var fallbackReject,
                fallbackRects))
        {
            result.RejectReason = $"fallback:{fallbackReject}";
            result.FallbackPlacedCount = fallbackPlaced;
            return result;
        }

        result.Fits = true;
        result.FallbackPlacedCount = fallbackPlaced;
        ApplyScore(context, result, state, scoredFallbackRects);
        if (collectPlacements)
            result.FinalState = state;
        return result;
    }

    private static IReadOnlyList<BaseRectCandidate> CreateBaseRectCandidates(DrawingArrangeContext context, View baseView)
    {
        var width = DrawingArrangeContextSizing.GetWidth(context, baseView);
        var height = DrawingArrangeContextSizing.GetHeight(context, baseView);
        var minX = context.Margin;
        var maxX = context.SheetWidth - context.Margin;
        var minY = context.Margin;
        var maxY = context.SheetHeight - context.Margin;
        var availableWidth = maxX - minX;
        var availableHeight = maxY - minY;
        if (availableWidth <= 0 || availableHeight <= 0 || width > availableWidth || height > availableHeight)
            return Array.Empty<BaseRectCandidate>();

        var targets = new[]
        {
            (Name: "Center", X: minX + availableWidth * 0.5, Y: minY + availableHeight * 0.5),
            (Name: "LeftCenter", X: minX + availableWidth * 0.25, Y: minY + availableHeight * 0.5),
            (Name: "RightCenter", X: minX + availableWidth * 0.75, Y: minY + availableHeight * 0.5),
            (Name: "TopCenter", X: minX + availableWidth * 0.5, Y: minY + availableHeight * 0.75),
            (Name: "BottomCenter", X: minX + availableWidth * 0.5, Y: minY + availableHeight * 0.25),
            (Name: "TopLeft", X: minX + availableWidth * 0.25, Y: minY + availableHeight * 0.75),
            (Name: "TopRight", X: minX + availableWidth * 0.75, Y: minY + availableHeight * 0.75),
            (Name: "BottomLeft", X: minX + availableWidth * 0.25, Y: minY + availableHeight * 0.25),
            (Name: "BottomRight", X: minX + availableWidth * 0.75, Y: minY + availableHeight * 0.25)
        };

        var frame = new PlacementFrame(minX, minY, maxX, maxY);
        var result = new List<BaseRectCandidate>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var target in targets)
        {
            if (!ViewPlacementService.TryPlaceNearPoint(
                    frame, width, height,
                    target.X, target.Y,
                    context.ReservedAreas, context.Gap,
                    out var sheetRect,
                    tag: $"planner-base-candidate:{target.Name}"))
                continue;

            var rect = sheetRect;
            if (!IsInsideSheetMargins(context, rect))
                continue;

            var key = $"{Math.Round(rect.MinX, 2)}:{Math.Round(rect.MinY, 2)}:{Math.Round(rect.MaxX, 2)}:{Math.Round(rect.MaxY, 2)}";
            if (!seen.Add(key))
                continue;

            result.Add(new BaseRectCandidate(target.Name, rect));
        }

        return result;
    }

    private static bool IsInsideSheetMargins(DrawingArrangeContext context, ReservedRect rect)
        => rect.MinX >= context.Margin
           && rect.MaxX <= context.SheetWidth - context.Margin
           && rect.MinY >= context.Margin
           && rect.MaxY <= context.SheetHeight - context.Margin;

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
        bool trace,
        out int placedCount,
        out string rejectReason,
        List<(PlannerItem Item, ReservedRect Rect)>? collectPlacements = null)
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

        var placedFallbacks = new List<(PlannerItem Item, ReservedRect Rect)>();

        foreach (var item in fallbackItems
                     .OrderBy(item => GetFallbackPlacementSortPriority(item.PreferredSide, item.StrongProjection))
                     .ThenBy(item => IsModel3D(context, item.View) ? 1 : 0)
                     .ThenByDescending(item => GetArea(context, item)))
        {
            var width = DrawingArrangeContextSizing.GetWidth(context, item.View);
            var height = DrawingArrangeContextSizing.GetHeight(context, item.View);

            if (TryPlacePreferredSideFallbackView(context, state, placedFallbacks, item, out var preferredRect))
            {
                placedFallbacks.Add((item, preferredRect));
                collectPlacements?.Add((item, preferredRect));
                placedCount++;

                if (trace)
                {
                    PerfTrace.Write(
                        "api-view",
                        "projected_group_fallback_result",
                        0,
                        $"scenario={scenarioName} view={item.Id} preferred={item.PreferredSide} actual={item.PreferredSide} result=ok placementFallbackUsed=0 mode=preferred-side rect={FormatRect(preferredRect)}");
                }

                continue;
            }

            var blocked = context.ReservedAreas
                .Concat(state.Placements.Values.Select(placement => placement.Rect))
                .Concat(placedFallbacks.Select(placement => placement.Rect))
                .ToList();
            var fallbackFrame = new PlacementFrame(context.Margin, context.Margin,
                context.SheetWidth - context.Margin, context.SheetHeight - context.Margin);
            var target = GetPackedFallbackTargetPoint(context, state.BaseRect, item.PreferredSide);
            // Old model: bin+gap, item+gap, blockers+gap — use inflated variant to preserve asymmetric clearance.
            if (!ViewPlacementService.TryPlaceNearPointItemInflated(
                    fallbackFrame, width, height,
                    target.X, target.Y,
                    blocked, context.Gap,
                    out var rect,
                    tag: $"planner-fallback-packed:view={item.Id}:preferred={item.PreferredSide}"))
            {
                rejectReason = $"no-fallback-space:view={item.Id}";
                if (trace)
                {
                    PerfTrace.Write(
                        "api-view",
                        "projected_group_fallback_result",
                        0,
                        $"scenario={scenarioName} view={item.Id} preferred={item.PreferredSide} actual=Packed result=reject reason=no-fallback-space");
                }
                return false;
            }

            if (!ValidateFallbackRect(context, state, placedFallbacks, item, rect))
            {
                rejectReason = $"fallback-overlap:view={item.Id}";
                if (trace)
                {
                    PerfTrace.Write(
                        "api-view",
                        "projected_group_fallback_result",
                        0,
                        $"scenario={scenarioName} view={item.Id} preferred={item.PreferredSide} actual=Packed result=reject reason=fallback-overlap rect={FormatRect(rect)}");
                }

                return false;
            }

            placedFallbacks.Add((item, rect));
            collectPlacements?.Add((item, rect));
            placedCount++;

            if (trace)
            {
                var actual = InferActualPlacementSide(state.BaseRect, rect);
                PerfTrace.Write(
                    "api-view",
                    "projected_group_fallback_result",
                    0,
                    $"scenario={scenarioName} view={item.Id} preferred={item.PreferredSide} actual={actual} result=ok placementFallbackUsed=1 mode=packed-targeted target=({target.X:F1},{target.Y:F1}) rect={FormatRect(rect)}");
            }
        }

        return true;
    }

    private static int GetViewTypeStackRank(View.ViewTypes viewType, SectionPlacementSide side)
        => side switch
        {
            // Bottom stack: BottomView commits first (lands closest to FrontView),
            // BackView commits second (lands furthest from FrontView, at the very bottom).
            SectionPlacementSide.Bottom => viewType switch
            {
                View.ViewTypes.BottomView => 0,
                View.ViewTypes.BackView => 1,
                _ => 2
            },
            _ => 0
        };

    internal static int GetFallbackPlacementSortPriority(SectionPlacementSide preferredSide, bool strongProjection)
    {
        var projectionRank = strongProjection ? 0 : 100;
        var sideRank = preferredSide switch
        {
            SectionPlacementSide.Top => 0,
            SectionPlacementSide.Bottom => 1,
            SectionPlacementSide.Left => 2,
            SectionPlacementSide.Right => 3,
            _ => 4
        };

        return projectionRank + sideRank;
    }

    private static bool IsModel3D(DrawingArrangeContext context, View view)
        // Workspace may be absent in estimate/test contexts; ViewType keeps ModelView last there too.
        => view.ViewType == View.ViewTypes.ModelView
           || context.Workspace?.GetSemanticKind(view.GetIdentifier().ID) == ViewSemanticKind.Model3D;

    private static (double X, double Y) GetPackedFallbackTargetPoint(
        DrawingArrangeContext context,
        ReservedRect baseRect,
        SectionPlacementSide side)
    {
        var minX = context.Margin;
        var maxX = context.SheetWidth - context.Margin;
        var minY = context.Margin;
        var maxY = context.SheetHeight - context.Margin;
        var baseCenterX = (baseRect.MinX + baseRect.MaxX) * 0.5;
        var baseCenterY = (baseRect.MinY + baseRect.MaxY) * 0.5;

        return side switch
        {
            SectionPlacementSide.Top => (baseCenterX, maxY),
            SectionPlacementSide.Bottom => (baseCenterX, minY),
            SectionPlacementSide.Left => (minX, baseCenterY),
            SectionPlacementSide.Right => (maxX, baseCenterY),
            _ => ((minX + maxX) * 0.5, (minY + maxY) * 0.5)
        };
    }

    private static bool TryPlacePreferredSideFallbackView(
        DrawingArrangeContext context,
        VirtualState state,
        IReadOnlyList<(PlannerItem Item, ReservedRect Rect)> placedFallbacks,
        PlannerItem item,
        out ReservedRect rect)
    {
        rect = null!;
        if (item.PreferredSide == SectionPlacementSide.Unknown)
            return false;

        if (!TryCreatePreferredFallbackBand(context, state.BaseRect, item.PreferredSide, out var band))
            return false;

        var width = DrawingArrangeContextSizing.GetWidth(context, item.View);
        var height = DrawingArrangeContextSizing.GetHeight(context, item.View);
        if (width <= 0 || height <= 0 || width > band.Width || height > band.Height)
            return false;

        var blocked = context.ReservedAreas
            .Concat(state.Placements.Values.Select(placement => placement.Rect))
            .Concat(placedFallbacks.Select(placement => placement.Rect))
            .ToList();

        var bandFrame = new PlacementFrame(band.MinX, band.MinY, band.MaxX, band.MaxY);
        var (targetX, targetY) = GetPreferredFallbackTargetPoint(state.BaseRect, band, item.PreferredSide);
        // Old model: bin+gap, item+gap, blockers+gap — use inflated variant to preserve asymmetric clearance.
        if (!ViewPlacementService.TryPlaceNearPointItemInflated(
                bandFrame, width, height,
                targetX, targetY,
                blocked, context.Gap,
                out rect,
                tag: $"planner-fallback-preferred:view={item.Id}:preferred={item.PreferredSide}"))
            return false;

        return ValidateFallbackRect(context, state, placedFallbacks, item, rect);
    }

    private static bool TryCreatePreferredFallbackBand(
        DrawingArrangeContext context,
        ReservedRect baseRect,
        SectionPlacementSide side,
        out ReservedRect band)
    {
        var minX = context.Margin;
        var maxX = context.SheetWidth - context.Margin;
        var minY = context.Margin;
        var maxY = context.SheetHeight - context.Margin;

        band = side switch
        {
            SectionPlacementSide.Top => new ReservedRect(minX, baseRect.MaxY + context.Gap, maxX, maxY),
            SectionPlacementSide.Bottom => new ReservedRect(minX, minY, maxX, baseRect.MinY - context.Gap),
            SectionPlacementSide.Left => new ReservedRect(minX, minY, baseRect.MinX - context.Gap, maxY),
            SectionPlacementSide.Right => new ReservedRect(baseRect.MaxX + context.Gap, minY, maxX, maxY),
            _ => new ReservedRect(0, 0, 0, 0)
        };

        return band.Width > 0 && band.Height > 0;
    }

    private static (double X, double Y) GetPreferredFallbackTargetPoint(
        ReservedRect baseRect,
        ReservedRect band,
        SectionPlacementSide side)
    {
        var baseCenterX = (baseRect.MinX + baseRect.MaxX) * 0.5;
        var baseCenterY = (baseRect.MinY + baseRect.MaxY) * 0.5;

        return side switch
        {
            SectionPlacementSide.Top => (baseCenterX, band.MinY),
            SectionPlacementSide.Bottom => (baseCenterX, band.MaxY),
            SectionPlacementSide.Left => (band.MaxX, baseCenterY),
            SectionPlacementSide.Right => (band.MinX, baseCenterY),
            _ => ((band.MinX + band.MaxX) * 0.5, (band.MinY + band.MaxY) * 0.5)
        };
    }

    private static bool ValidateFallbackRect(
        DrawingArrangeContext context,
        VirtualState state,
        IReadOnlyList<(PlannerItem Item, ReservedRect Rect)> placedFallbacks,
        PlannerItem item,
        ReservedRect rect)
    {
        var others = state.Placements
            .Where(placement => placement.Key != item.Id)
            .ToDictionary(placement => placement.Key, placement => Inflate(placement.Value.Rect, context.Gap));
        foreach (var placed in placedFallbacks)
            others[placed.Item.Id] = Inflate(placed.Rect, context.Gap);

        return ViewPlacementValidator.Validate(
            rect,
            context.Margin,
            context.SheetWidth - context.Margin,
            context.Margin,
            context.SheetHeight - context.Margin,
            context.ReservedAreas,
            others).Fits;
    }

    internal static SectionPlacementSide InferActualPlacementSide(ReservedRect baseRect, ReservedRect rect)
    {
        var leftGap = baseRect.MinX - rect.MaxX;
        var rightGap = rect.MinX - baseRect.MaxX;
        var topGap = rect.MinY - baseRect.MaxY;
        var bottomGap = baseRect.MinY - rect.MaxY;

        var side = SectionPlacementSide.Unknown;
        var bestGap = 0.0;
        Consider(SectionPlacementSide.Left, leftGap);
        Consider(SectionPlacementSide.Right, rightGap);
        Consider(SectionPlacementSide.Top, topGap);
        Consider(SectionPlacementSide.Bottom, bottomGap);
        if (side != SectionPlacementSide.Unknown)
            return side;

        var rectCenterX = (rect.MinX + rect.MaxX) * 0.5;
        var rectCenterY = (rect.MinY + rect.MaxY) * 0.5;
        var baseCenterX = (baseRect.MinX + baseRect.MaxX) * 0.5;
        var baseCenterY = (baseRect.MinY + baseRect.MaxY) * 0.5;
        var dx = rectCenterX - baseCenterX;
        var dy = rectCenterY - baseCenterY;

        if (Math.Abs(dx) < 1e-6 && Math.Abs(dy) < 1e-6)
            return SectionPlacementSide.Unknown;

        if (Math.Abs(dx) >= Math.Abs(dy))
            return dx < 0 ? SectionPlacementSide.Left : SectionPlacementSide.Right;

        return dy < 0 ? SectionPlacementSide.Bottom : SectionPlacementSide.Top;

        void Consider(SectionPlacementSide candidate, double gap)
        {
            if (gap <= bestGap)
                return;

            bestGap = gap;
            side = candidate;
        }
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

    private static void ApplyScore(
        DrawingArrangeContext context,
        ScenarioResult result,
        VirtualState state,
        IReadOnlyList<(PlannerItem Item, ReservedRect Rect)> fallbackPlacements)
    {
        var allRects = state.Placements.Values
            .Select(placement => placement.Rect)
            .Concat(fallbackPlacements.Select(placement => placement.Rect));
        var bounds = GetBounds(allRects);
        var usableWidth = context.SheetWidth - (2 * context.Margin);
        var usableHeight = context.SheetHeight - (2 * context.Margin);
        var usableArea = usableWidth * usableHeight;
        if (usableArea <= 0)
            return;

        var sheetCenterX = context.Margin + (usableWidth * 0.5);
        var sheetCenterY = context.Margin + (usableHeight * 0.5);
        var baseCenterX = (state.BaseRect.MinX + state.BaseRect.MaxX) * 0.5;
        var baseCenterY = (state.BaseRect.MinY + state.BaseRect.MaxY) * 0.5;
        var sheetDiagonal = Math.Sqrt((usableWidth * usableWidth) + (usableHeight * usableHeight));
        var baseDistance = sheetDiagonal <= 0
            ? 0
            : Math.Sqrt(Math.Pow(baseCenterX - sheetCenterX, 2) + Math.Pow(baseCenterY - sheetCenterY, 2)) / sheetDiagonal;

        result.CompactnessRatio = (bounds.Width * bounds.Height) / usableArea;
        result.BaseCenterDistanceRatio = baseDistance;
        result.PlacementSidePenalty = fallbackPlacements.Sum(placement =>
            GetPlacementSideMismatchPenalty(
                placement.Item.PreferredSide,
                InferActualPlacementSide(state.BaseRect, placement.Rect)));
        result.EdgeMarginPenalty = GetEdgeMarginPenalty(context, bounds);
        result.Score = result.CompactnessRatio
                       + (result.BaseCenterDistanceRatio * 0.25)
                       + result.PlacementSidePenalty
                       + result.EdgeMarginPenalty;
    }

    internal static double GetEdgeMarginPenalty(DrawingArrangeContext context, ReservedRect bounds)
    {
        var usableWidth = context.SheetWidth - (2 * context.Margin);
        var usableHeight = context.SheetHeight - (2 * context.Margin);
        var safeDistance = Math.Max(context.Gap * 2.0, 10.0);
        var available = Math.Min(usableWidth, usableHeight);
        if (safeDistance <= 0 || available <= 0)
            return 0;

        safeDistance = Math.Min(safeDistance, available * 0.25);
        if (safeDistance <= 0)
            return 0;

        var left = bounds.MinX - context.Margin;
        var right = (context.SheetWidth - context.Margin) - bounds.MaxX;
        var bottom = bounds.MinY - context.Margin;
        var top = (context.SheetHeight - context.Margin) - bounds.MaxY;

        return (GetEdgeShortfall(left, safeDistance)
                + GetEdgeShortfall(right, safeDistance)
                + GetEdgeShortfall(bottom, safeDistance)
                + GetEdgeShortfall(top, safeDistance)) * 0.03;
    }

    private static double GetEdgeShortfall(double distance, double safeDistance)
        => distance >= safeDistance ? 0 : (safeDistance - Math.Max(distance, 0)) / safeDistance;

    internal static double GetPlacementSideMismatchPenalty(SectionPlacementSide preferred, SectionPlacementSide actual)
    {
        if (preferred == SectionPlacementSide.Unknown || actual == SectionPlacementSide.Unknown || preferred == actual)
            return 0;

        return IsOppositeSide(preferred, actual) ? 0.16 : 0.08;
    }

    private static bool IsOppositeSide(SectionPlacementSide preferred, SectionPlacementSide actual)
        => (preferred == SectionPlacementSide.Left && actual == SectionPlacementSide.Right)
           || (preferred == SectionPlacementSide.Right && actual == SectionPlacementSide.Left)
           || (preferred == SectionPlacementSide.Top && actual == SectionPlacementSide.Bottom)
           || (preferred == SectionPlacementSide.Bottom && actual == SectionPlacementSide.Top);

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

    private static string FormatScore(double value)
        => double.IsInfinity(value) || double.IsNaN(value) || value == double.MaxValue
            ? "n/a"
            : value.ToString("F4");
}
