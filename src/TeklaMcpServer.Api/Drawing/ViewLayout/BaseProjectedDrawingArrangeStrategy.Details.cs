using System.Collections.Generic;
using System.Linq;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using TeklaMcpServer.Api.Diagnostics;
using TeklaMcpServer.Api.Drawing;

namespace TeklaMcpServer.Api.Drawing.ViewLayout;

public sealed partial class BaseProjectedDrawingArrangeStrategy
{
    internal readonly struct DetailPlacementDecision
    {
        public DetailPlacementDecision(
            bool success,
            ReservedRect rect,
            double anchorDistance,
            double preferredDistance,
            bool preferredBand,
            string degradedReason)
        {
            Success = success;
            Rect = rect;
            AnchorDistance = anchorDistance;
            PreferredDistance = preferredDistance;
            PreferredBand = preferredBand;
            DegradedReason = degradedReason;
        }

        public bool Success { get; }
        public ReservedRect Rect { get; }
        public double AnchorDistance { get; }
        public double PreferredDistance { get; }
        public bool PreferredBand { get; }
        public string DegradedReason { get; }
    }

    private static void TryPlaceDetailViews(
        DrawingArrangeContext context,
        IReadOnlyList<DetailRelation> detailRelations,
        ViewPlacementSearchArea searchArea,
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
                return ViewPlacementGeometryService.CreateRectFromFrameCenter(
                    item.FrameCenterX,
                    item.FrameCenterY,
                    width,
                    height);
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
            var decision = ProbeDetailPlacement(
                ownerRect,
                detailWidth,
                detailHeight,
                offset,
                searchArea.FreeMinX,
                searchArea.FreeMaxX,
                searchArea.FreeMinY,
                searchArea.FreeMaxY,
                blockedRects,
                relation.AnchorX,
                relation.AnchorY);
            if (!decision.Success)
                continue;

            if (!string.IsNullOrEmpty(decision.DegradedReason))
            {
                PerfTrace.Write(
                    "api-view",
                    "detail_placement_degraded",
                    0,
                    $"detail={relation.DetailView.GetIdentifier().ID} owner={relation.OwnerView.GetIdentifier().ID} reason={decision.DegradedReason} anchorDistance={decision.AnchorDistance:F2} preferredDistance={decision.PreferredDistance:F2}");
            }

            AddPlannedAndOccupiedRect(planned, occupied, relation.DetailView, decision.Rect);
            blockedRects.Add(decision.Rect);
            plannedById[relation.DetailView.GetIdentifier().ID] = decision.Rect;
        }
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
        => TryPlaceDetailViews(
            context,
            detailRelations,
            CreateSearchArea(freeMinX, freeMaxX, freeMinY, freeMaxY),
            gap,
            occupied,
            planned);

    private static bool TryFindDetailRect(
        ReservedRect ownerRect,
        double detailWidth,
        double detailHeight,
        double offset,
        ViewPlacementSearchArea searchArea,
        IReadOnlyList<ReservedRect> occupied,
        double? anchorX,
        double? anchorY,
        out ReservedRect bestRect)
    {
        if (!TrySelectDetailPlacementDecision(
                ownerRect,
                detailWidth,
                detailHeight,
                offset,
                searchArea,
                occupied,
                anchorX,
                anchorY,
                out var decision))
        {
            bestRect = default;
            return false;
        }

        bestRect = decision.Rect;
        return true;
    }

    private static bool TrySelectDetailPlacementDecision(
        ReservedRect ownerRect,
        double detailWidth,
        double detailHeight,
        double offset,
        ViewPlacementSearchArea searchArea,
        IReadOnlyList<ReservedRect> occupied,
        double? anchorX,
        double? anchorY,
        out DetailPlacementDecision bestDecision)
    {
        var ownerCenterX = CenterX(ownerRect);
        var ownerCenterY = CenterY(ownerRect);
        var effectiveAnchorX = anchorX ?? ownerCenterX;
        var effectiveAnchorY = anchorY ?? ownerCenterY;
        var hasHorizontalAnchor = anchorX.HasValue;
        var hasVerticalAnchor = anchorY.HasValue;
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
            searchArea.FreeMinX,
            ownerRect.MinX - offset - detailWidth,
            ownerRect.MaxX + offset,
            ownerCenterX - detailWidth * 0.5
        };
        var yCandidates = new HashSet<double>
        {
            searchArea.FreeMinY,
            ownerRect.MinY - offset - detailHeight,
            ownerRect.MaxY + offset,
            ownerCenterY - detailHeight * 0.5
        };

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

        bestDecision = default;
        foreach (var minX in xCandidates)
        {
            foreach (var minY in yCandidates)
            {
                var rect = new ReservedRect(minX, minY, minX + detailWidth, minY + detailHeight);
                if (!IsWithinArea(rect, searchArea.FreeMinX, searchArea.FreeMaxX, searchArea.FreeMinY, searchArea.FreeMaxY))
                    continue;

                if (IntersectsAny(rect, occupied))
                    continue;

                var centerX = CenterX(rect);
                var centerY = CenterY(rect);
                var anchorDistance = System.Math.Abs(centerX - effectiveAnchorX) + System.Math.Abs(centerY - effectiveAnchorY);
                var preferredDistance = System.Math.Abs(centerX - preferredCenterX) + System.Math.Abs(centerY - preferredCenterY);
                var horizontalPreferred = !hasHorizontalAnchor || (preferRight ? centerX >= ownerCenterX : centerX <= ownerCenterX);
                var verticalPreferred = !hasVerticalAnchor || (preferTop ? centerY >= ownerCenterY : centerY <= ownerCenterY);
                var preferredBand = horizontalPreferred && verticalPreferred;
                var degradedReason = preferredBand ? string.Empty : "cross-band";
                var candidateDecision = new DetailPlacementDecision(
                    success: true,
                    rect,
                    anchorDistance,
                    preferredDistance,
                    preferredBand,
                    degradedReason);
                if (IsBetterDetailPlacementDecision(candidateDecision, bestDecision))
                    bestDecision = candidateDecision;
            }
        }

        return bestDecision.Success;
    }

    private static bool IsBetterDetailPlacementDecision(DetailPlacementDecision candidate, DetailPlacementDecision currentBest)
    {
        if (candidate.Success != currentBest.Success)
            return candidate.Success;

        if (candidate.AnchorDistance != currentBest.AnchorDistance)
            return candidate.AnchorDistance < currentBest.AnchorDistance;

        if (candidate.PreferredBand != currentBest.PreferredBand)
            return candidate.PreferredBand;

        if (candidate.PreferredDistance != currentBest.PreferredDistance)
            return candidate.PreferredDistance < currentBest.PreferredDistance;

        if (candidate.Rect.MinY != currentBest.Rect.MinY)
            return candidate.Rect.MinY < currentBest.Rect.MinY;

        return candidate.Rect.MinX < currentBest.Rect.MinX;
    }

    internal static DetailPlacementDecision ProbeDetailPlacement(
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
        double? anchorY)
    {
        TrySelectDetailPlacementDecision(
            ownerRect,
            detailWidth,
            detailHeight,
            offset,
            CreateSearchArea(freeMinX, freeMaxX, freeMinY, freeMaxY),
            occupied,
            anchorX,
            anchorY,
            out var decision);
        return decision;
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
        => TryFindDetailRect(
            ownerRect,
            detailWidth,
            detailHeight,
            offset,
            CreateSearchArea(freeMinX, freeMaxX, freeMinY, freeMaxY),
            occupied,
            anchorX,
            anchorY,
            out bestRect);
}

