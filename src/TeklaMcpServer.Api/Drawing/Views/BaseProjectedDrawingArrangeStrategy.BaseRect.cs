using System.Collections.Generic;
using Tekla.Structures.Drawing;
using TeklaMcpServer.Api.Algorithms.Packing;

namespace TeklaMcpServer.Api.Drawing;

public sealed partial class BaseProjectedDrawingArrangeStrategy
{
    internal readonly struct BaseRectViabilityDecision
    {
        public BaseRectViabilityDecision(
            ReservedRect baseRect,
            bool isViable,
            int strictNeighborFitCount,
            int preferredHorizontalStackFitCount,
            double safeGapScore,
            string rejectReason,
            string rejectZone,
            View? rejectView,
            ReservedRect rejectRect)
        {
            BaseRect = baseRect;
            IsViable = isViable;
            StrictNeighborFitCount = strictNeighborFitCount;
            PreferredHorizontalStackFitCount = preferredHorizontalStackFitCount;
            SafeGapScore = safeGapScore;
            RejectReason = rejectReason;
            RejectZone = rejectZone;
            RejectView = rejectView;
            RejectRect = rejectRect;
        }

        public ReservedRect BaseRect { get; }
        public bool IsViable { get; }
        public int StrictNeighborFitCount { get; }
        public int PreferredHorizontalStackFitCount { get; }
        public double SafeGapScore { get; }
        public string RejectReason { get; }
        public string RejectZone { get; }
        public View? RejectView { get; }
        public ReservedRect RejectRect { get; }
    }

    internal static bool TrySelectBaseRectWithBudgets(
        DrawingArrangeContext context,
        NeighborSet neighbors,
        IReadOnlyList<View> leftSections,
        IReadOnlyList<View> rightSections,
        IReadOnlyList<View> topSections,
        IReadOnlyList<View> bottomSections,
        IReadOnlyList<ReservedRect> blocked,
        bool includeRelaxedCandidates,
        bool requireAllStrictNeighborsFit,
        out ReservedRect baseRect,
        out BaseRectViabilityDecision decision)
    {
        var baseView = neighbors.BaseView;
        var (freeMinX, freeMaxX, freeMinY, freeMaxY) = ComputeFreeArea(context);
        var searchArea = CreateSearchArea(freeMinX, freeMaxX, freeMinY, freeMaxY);
        var baseWidth = DrawingArrangeContextSizing.GetWidth(context, baseView);
        var baseHeight = DrawingArrangeContextSizing.GetHeight(context, baseView);
        var budgets = ComputeZoneBudgets(context, neighbors, leftSections, rightSections, topSections, bottomSections);
        var currentBaseRect = TryGetViewBoundingRect(baseView, out var currentRect) ? currentRect : (ReservedRect?)null;

        var bestDecision = new BaseRectViabilityDecision(
            new ReservedRect(0, 0, 0, 0),
            isViable: false,
            strictNeighborFitCount: -1,
            preferredHorizontalStackFitCount: -1,
            safeGapScore: double.NegativeInfinity,
            rejectReason: string.Empty,
            rejectZone: "Center",
            rejectView: baseView,
            rejectRect: new ReservedRect(0, 0, 0, 0));
        var foundCandidate = false;

        foreach (var window in EnumerateBaseViewWindows(searchArea, budgets, includeRelaxedCandidates))
        {
            if (window.MaxX - window.MinX < baseWidth || window.MaxY - window.MinY < baseHeight)
                continue;

            if (!TryFindBaseViewRectInWindow(
                    blocked,
                    window,
                    baseWidth,
                    baseHeight,
                    context.Gap,
                    out var candidateBaseRect))
            {
                continue;
            }

            foundCandidate = true;
            var candidateSearchArea = new ViewPlacementSearchArea(candidateBaseRect, freeMinX, freeMaxX, freeMinY, freeMaxY);
            var candidateDecision = ProbeBaseRectViabilityCore(
                context,
                neighbors,
                leftSections,
                rightSections,
                topSections,
                bottomSections,
                budgets,
                candidateSearchArea,
                blocked,
                requireAllStrictNeighborsFit);

            if (IsBetterBaseRectViability(candidateDecision, bestDecision, currentBaseRect))
                bestDecision = candidateDecision;
        }

        if (!foundCandidate || !bestDecision.IsViable)
        {
            baseRect = new ReservedRect(0, 0, 0, 0);
            decision = bestDecision;
            return false;
        }

        baseRect = bestDecision.BaseRect;
        decision = bestDecision;
        return true;
    }

    internal static BaseRectViabilityDecision ProbeBaseRectViability(
        DrawingArrangeContext context,
        NeighborSet neighbors,
        IReadOnlyList<View> leftSections,
        IReadOnlyList<View> rightSections,
        IReadOnlyList<View> topSections,
        IReadOnlyList<View> bottomSections,
        ReservedRect baseRect,
        double freeMinX,
        double freeMaxX,
        double freeMinY,
        double freeMaxY,
        IReadOnlyList<ReservedRect>? blocked = null,
        bool requireAllStrictNeighborsFit = false)
    {
        blocked ??= System.Array.Empty<ReservedRect>();
        var budgets = ComputeZoneBudgets(context, neighbors, leftSections, rightSections, topSections, bottomSections);
        return ProbeBaseRectViabilityCore(
            context,
            neighbors,
            leftSections,
            rightSections,
            topSections,
            bottomSections,
            budgets,
            new ViewPlacementSearchArea(baseRect, freeMinX, freeMaxX, freeMinY, freeMaxY),
            blocked,
            requireAllStrictNeighborsFit);
    }

    internal static bool IsBetterBaseRectViability(
        BaseRectViabilityDecision candidate,
        BaseRectViabilityDecision currentBest)
        => IsBetterBaseRectViability(candidate, currentBest, currentBaseRect: null);

    private static bool IsBetterBaseRectViability(
        BaseRectViabilityDecision candidate,
        BaseRectViabilityDecision currentBest,
        ReservedRect? currentBaseRect)
    {
        if (candidate.IsViable != currentBest.IsViable)
            return candidate.IsViable;

        if (candidate.StrictNeighborFitCount != currentBest.StrictNeighborFitCount)
            return candidate.StrictNeighborFitCount > currentBest.StrictNeighborFitCount;

        if (candidate.PreferredHorizontalStackFitCount != currentBest.PreferredHorizontalStackFitCount)
            return candidate.PreferredHorizontalStackFitCount > currentBest.PreferredHorizontalStackFitCount;

        if (!candidate.SafeGapScore.Equals(currentBest.SafeGapScore))
            return candidate.SafeGapScore > currentBest.SafeGapScore;

        if (currentBaseRect != null)
        {
            var candidateDistance = ComputeBaseRectCenterDistance(candidate.BaseRect, currentBaseRect);
            var currentBestDistance = ComputeBaseRectCenterDistance(currentBest.BaseRect, currentBaseRect);
            if (!candidateDistance.Equals(currentBestDistance))
                return candidateDistance < currentBestDistance;
        }

        if (candidate.BaseRect.MinY != currentBest.BaseRect.MinY)
            return candidate.BaseRect.MinY < currentBest.BaseRect.MinY;

        return candidate.BaseRect.MinX < currentBest.BaseRect.MinX;
    }

    private static BaseRectViabilityDecision ProbeBaseRectViabilityCore(
        DrawingArrangeContext context,
        NeighborSet neighbors,
        IReadOnlyList<View> leftSections,
        IReadOnlyList<View> rightSections,
        IReadOnlyList<View> topSections,
        IReadOnlyList<View> bottomSections,
        ZoneBudgets budgets,
        ViewPlacementSearchArea searchArea,
        IReadOnlyList<ReservedRect> blocked,
        bool requireAllStrictNeighborsFit)
    {
        var baseRect = searchArea.BaseRect;
        var gap = context.Gap;
        var top = neighbors.TopNeighbor;
        var bottom = neighbors.BottomNeighbor;
        var leftNeighbor = neighbors.SideNeighborLeft;
        var rightNeighbor = neighbors.SideNeighborRight;
        var topSectionPartition = PartitionStandardSections(context, baseRect, topSections, SectionPlacementSide.Top);
        var bottomSectionPartition = PartitionStandardSections(context, baseRect, bottomSections, SectionPlacementSide.Bottom);

        var occupied = new List<ReservedRect>(blocked) { baseRect };
        var placements = new MainSkeletonPlacementState();
        var strictSpecs = CreateStrictMainSkeletonNeighborSpecs(
            top,
            bottom,
            leftNeighbor,
            rightNeighbor,
            top != null ? DrawingArrangeContextSizing.GetWidth(context, top) : 0,
            top != null ? DrawingArrangeContextSizing.GetHeight(context, top) : 0,
            bottom != null ? DrawingArrangeContextSizing.GetWidth(context, bottom) : 0,
            bottom != null ? DrawingArrangeContextSizing.GetHeight(context, bottom) : 0,
            leftNeighbor != null ? DrawingArrangeContextSizing.GetWidth(context, leftNeighbor) : 0,
            leftNeighbor != null ? DrawingArrangeContextSizing.GetHeight(context, leftNeighbor) : 0,
            rightNeighbor != null ? DrawingArrangeContextSizing.GetWidth(context, rightNeighbor) : 0,
            rightNeighbor != null ? DrawingArrangeContextSizing.GetHeight(context, rightNeighbor) : 0);

        var strictNeighborFitCount = 0;
        var missingStrictSpec = default(MainSkeletonNeighborSpec?);
        foreach (var spec in strictSpecs)
        {
            if (spec.View == null)
                continue;

            var rect = FindStrictMainSkeletonNeighborRect(spec, searchArea, gap, occupied);
            if (rect == null)
            {
                missingStrictSpec ??= spec;
                continue;
            }

            CommitMainSkeletonPlacement(placements, spec.Role, occupied, rect);
            strictNeighborFitCount++;
        }

        if (!TryValidateMainSkeletonSpacing(
                baseRect,
                placements,
                context.SheetWidth,
                context.SheetHeight,
                context.Margin,
                gap,
                context.ReservedAreas,
                out var mainSkeletonReason,
                out var mainSkeletonRole,
                out var mainSkeletonRect))
        {
            return new BaseRectViabilityDecision(
                baseRect,
                isViable: false,
                strictNeighborFitCount,
                preferredHorizontalStackFitCount: 0,
                safeGapScore: ComputeBaseRectSafeGapScore(baseRect, searchArea, budgets),
                rejectReason: mainSkeletonReason,
                rejectZone: ToAttemptedZone(mainSkeletonRole),
                rejectView: ResolveMainSkeletonView(neighbors, mainSkeletonRole),
                rejectRect: mainSkeletonRect);
        }

        if (requireAllStrictNeighborsFit && missingStrictSpec.HasValue)
        {
            var missingRole = missingStrictSpec.Value.Role;
            return new BaseRectViabilityDecision(
                baseRect,
                isViable: false,
                strictNeighborFitCount,
                preferredHorizontalStackFitCount: 0,
                safeGapScore: ComputeBaseRectSafeGapScore(baseRect, searchArea, budgets),
                rejectReason: $"main-skeleton-slot-{missingRole}",
                rejectZone: ToAttemptedZone(missingRole),
                rejectView: ResolveMainSkeletonView(neighbors, missingRole),
                rejectRect: baseRect);
        }

        var preferredHorizontalStackFitCount = 0;
        if (!TryValidatePreferredHorizontalSectionStack(
                context,
                topSectionPartition.Normal,
                baseRect,
                placements.GetAnchorOrBase("top", baseRect),
                RelativePlacement.Top,
                searchArea,
                gap,
                occupied,
                out var topFailure))
        {
            return CreateBaseRectSectionRejectDecision(baseRect, strictNeighborFitCount, preferredHorizontalStackFitCount, searchArea, budgets, RelativePlacement.Top, topFailure);
        }

        if (topSectionPartition.Normal.Count > 0)
            preferredHorizontalStackFitCount++;

        if (!TryValidatePreferredHorizontalSectionStack(
                context,
                bottomSectionPartition.Normal,
                baseRect,
                placements.GetAnchorOrBase("bottom", baseRect),
                RelativePlacement.Bottom,
                searchArea,
                gap,
                occupied,
                out var bottomFailure))
        {
            return CreateBaseRectSectionRejectDecision(baseRect, strictNeighborFitCount, preferredHorizontalStackFitCount, searchArea, budgets, RelativePlacement.Bottom, bottomFailure);
        }

        if (bottomSectionPartition.Normal.Count > 0)
            preferredHorizontalStackFitCount++;

        return new BaseRectViabilityDecision(
            baseRect,
            isViable: true,
            strictNeighborFitCount,
            preferredHorizontalStackFitCount,
            ComputeBaseRectSafeGapScore(baseRect, searchArea, budgets),
            rejectReason: string.Empty,
            rejectZone: string.Empty,
            rejectView: null,
            rejectRect: new ReservedRect(0, 0, 0, 0));
    }

    private static BaseRectViabilityDecision CreateBaseRectSectionRejectDecision(
        ReservedRect baseRect,
        int strictNeighborFitCount,
        int preferredHorizontalStackFitCount,
        ViewPlacementSearchArea searchArea,
        ZoneBudgets budgets,
        RelativePlacement zone,
        SectionStackFailureInfo? failure)
    {
        if (failure == null)
        {
            return new BaseRectViabilityDecision(
                baseRect,
                isViable: false,
                strictNeighborFitCount,
                preferredHorizontalStackFitCount,
                ComputeBaseRectSafeGapScore(baseRect, searchArea, budgets),
                rejectReason: "section-stack-failed",
                rejectZone: "Center",
                rejectView: null,
                rejectRect: baseRect);
        }

        return new BaseRectViabilityDecision(
            baseRect,
            isViable: false,
            strictNeighborFitCount,
            preferredHorizontalStackFitCount,
            ComputeBaseRectSafeGapScore(baseRect, searchArea, budgets),
            rejectReason: failure.Value.RejectReason,
            rejectZone: zone.ToString(),
            rejectView: failure.Value.Section,
            rejectRect: failure.Value.Rect);
    }

    private static bool TryValidatePreferredHorizontalSectionStack(
        DrawingArrangeContext context,
        IReadOnlyList<View> sections,
        ReservedRect frontRect,
        ReservedRect anchorRect,
        RelativePlacement zone,
        ViewPlacementSearchArea searchArea,
        double gap,
        IReadOnlyList<ReservedRect> occupied,
        out SectionStackFailureInfo? failure)
    {
        if (sections.Count == 0)
        {
            failure = null;
            return true;
        }

        return TryPlanHorizontalSectionStack(
            context,
            sections,
            frontRect,
            anchorRect,
            zone,
            searchArea,
            gap,
            occupied,
            out _,
            out failure);
    }

    private static double ComputeBaseRectSafeGapScore(
        ReservedRect baseRect,
        ViewPlacementSearchArea searchArea,
        ZoneBudgets budgets)
    {
        var leftSlack = System.Math.Max(0, baseRect.MinX - (searchArea.FreeMinX + budgets.LeftWidth));
        var rightSlack = System.Math.Max(0, (searchArea.FreeMaxX - budgets.RightWidth) - baseRect.MaxX);
        var bottomSlack = System.Math.Max(0, baseRect.MinY - (searchArea.FreeMinY + budgets.BottomHeight));
        var topSlack = System.Math.Max(0, (searchArea.FreeMaxY - budgets.TopHeight) - baseRect.MaxY);
        return leftSlack + rightSlack + bottomSlack + topSlack;
    }

    private static double ComputeBaseRectCenterDistance(ReservedRect rect, ReservedRect currentBaseRect)
    {
        var dx = CenterX(rect) - CenterX(currentBaseRect);
        var dy = CenterY(rect) - CenterY(currentBaseRect);
        return (dx * dx) + (dy * dy);
    }

    private static bool TryFindBaseViewWindow(
        ViewPlacementSearchArea searchArea,
        double baseWidth,
        double baseHeight,
        ZoneBudgets budgets,
        out ReservedRect window)
    {
        var minX = searchArea.FreeMinX + budgets.LeftWidth;
        var maxX = searchArea.FreeMaxX - budgets.RightWidth;
        var minY = searchArea.FreeMinY + budgets.BottomHeight;
        var maxY = searchArea.FreeMaxY - budgets.TopHeight;

        if (maxX - minX < baseWidth || maxY - minY < baseHeight)
        {
            window = new ReservedRect(0, 0, 0, 0);
            return false;
        }

        window = new ReservedRect(minX, minY, maxX, maxY);
        return true;
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
        => TryFindBaseViewWindow(
            CreateSearchArea(freeMinX, freeMaxX, freeMinY, freeMaxY),
            baseWidth,
            baseHeight,
            budgets,
            out window);

    private static IEnumerable<ReservedRect> EnumerateBaseViewWindows(
        ViewPlacementSearchArea searchArea,
        ZoneBudgets budgets,
        bool includeRelaxedCandidates)
    {
        yield return (
            new ReservedRect(
                searchArea.FreeMinX + budgets.LeftWidth,
                searchArea.FreeMinY + budgets.BottomHeight,
                searchArea.FreeMaxX - budgets.RightWidth,
                searchArea.FreeMaxY - budgets.TopHeight));

        if (!includeRelaxedCandidates)
            yield break;

        yield return new ReservedRect(
            searchArea.FreeMinX + budgets.LeftWidth,
            searchArea.FreeMinY,
            searchArea.FreeMaxX - budgets.RightWidth,
            searchArea.FreeMaxY);
        yield return new ReservedRect(
            searchArea.FreeMinX,
            searchArea.FreeMinY + budgets.BottomHeight,
            searchArea.FreeMaxX,
            searchArea.FreeMaxY - budgets.TopHeight);
        yield return new ReservedRect(
            searchArea.FreeMinX,
            searchArea.FreeMinY,
            searchArea.FreeMaxX,
            searchArea.FreeMaxY);
    }

    private static IEnumerable<(double minX, double maxX, double minY, double maxY)> EnumerateBaseViewWindows(
        double freeMinX,
        double freeMaxX,
        double freeMinY,
        double freeMaxY,
        ZoneBudgets budgets,
        bool includeRelaxedCandidates)
    {
        foreach (var window in EnumerateBaseViewWindows(
                     CreateSearchArea(freeMinX, freeMaxX, freeMinY, freeMaxY),
                     budgets,
                     includeRelaxedCandidates))
        {
            yield return (window.MinX, window.MaxX, window.MinY, window.MaxY);
        }
    }

    private static bool TryPlaceBaseViewWithBudgets(
        IReadOnlyList<ReservedRect> blocked,
        ViewPlacementSearchArea searchArea,
        double baseWidth,
        double baseHeight,
        ZoneBudgets budgets,
        double gap,
        bool includeRelaxedCandidates,
        out ReservedRect baseRect)
    {
        foreach (var window in EnumerateBaseViewWindows(searchArea, budgets, includeRelaxedCandidates))
        {
            if (window.MaxX - window.MinX < baseWidth || window.MaxY - window.MinY < baseHeight)
                continue;

            if (TryFindBaseViewRectInWindow(
                    blocked,
                    window,
                    baseWidth,
                    baseHeight,
                    gap,
                    out baseRect))
                return true;
        }

        baseRect = new ReservedRect(0, 0, 0, 0);
        return false;
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
        => TryPlaceBaseViewWithBudgets(
            blocked,
            CreateSearchArea(freeMinX, freeMaxX, freeMinY, freeMaxY),
            baseWidth,
            baseHeight,
            budgets,
            gap,
            includeRelaxedCandidates,
            out baseRect);

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
}
