using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

internal static class DimensionOperations
{
    public static List<DimensionGroup> EliminateRedundantItems(
        IReadOnlyList<DimensionGroup> groups,
        DimensionReductionPolicy? policy = null)
    {
        policy ??= DimensionReductionPolicy.Default;
        var reducedGroups = new List<DimensionGroup>(groups.Count);

        foreach (var group in groups)
        {
            var reducedItems = ReduceItems(group, policy);
            if (reducedItems.Count == 0)
                continue;

            var reducedGroup = CloneGroup(group);
            reducedGroup.DimensionList.AddRange(reducedItems);
            reducedGroup.SortMembers();
            reducedGroup.RefreshMetrics();
            reducedGroups.Add(reducedGroup);
        }

        return reducedGroups;
    }

    private static List<DimensionItem> ReduceItems(
        DimensionGroup group,
        DimensionReductionPolicy policy)
    {
        var items = group.DimensionList;
        if (items.Count <= 1)
            return items.ToList();

        var ordered = items
            .OrderByDescending(GetInformationRank)
            .ThenBy(static item => item.DimensionId)
            .ThenBy(static item => item.SortKey)
            .ToList();

        var kept = new List<DimensionItem>(ordered.Count);
        foreach (var candidate in ordered)
        {
            if (policy.EnableEquivalentSimpleReduction &&
                IsSimpleItem(candidate) &&
                kept.Any(existing => AreEquivalentSimpleItems(existing, candidate, policy)))
                continue;

            if (policy.EnableCoverageReduction &&
                IsSimpleItem(candidate) &&
                kept.Any(existing => Covers(existing, candidate, policy)))
                continue;

            kept.Add(candidate);
        }

        var deduplicated = kept
            .OrderBy(static item => item.SortKey)
            .ThenBy(static item => item.DimensionId)
            .ToList();

        return policy.EnableRepresentativeSelection
            ? SelectCommonRepresentatives(group, deduplicated, policy)
            : deduplicated;
    }

    private static List<DimensionItem> SelectCommonRepresentatives(
        DimensionGroup group,
        IReadOnlyList<DimensionItem> items,
        DimensionReductionPolicy policy)
    {
        if (items.Count <= 1)
            return items.ToList();

        var selected = new List<DimensionItem>();
        var packetStart = 0;

        for (var i = 1; i < items.Count; i++)
        {
            if (!ShouldSplitRepresentativePacket(items[i - 1], items[i], group.MaximumDistance, policy))
                continue;

            selected.Add(SelectRepresentative(group, items, packetStart, i - 1, policy));
            packetStart = i;
        }

        selected.Add(SelectRepresentative(group, items, packetStart, items.Count - 1, policy));
        return selected;
    }

    private static bool ShouldSplitRepresentativePacket(
        DimensionItem previous,
        DimensionItem current,
        double maximumDistance,
        DimensionReductionPolicy policy)
    {
        if (previous.LeadLineMain == null || current.LeadLineMain == null)
            return true;

        var previousEndToCurrentStart = GetDistance(
            previous.LeadLineMain.EndX,
            previous.LeadLineMain.EndY,
            current.LeadLineMain.StartX,
            current.LeadLineMain.StartY);
        var previousStartToCurrentEnd = GetDistance(
            previous.LeadLineMain.StartX,
            previous.LeadLineMain.StartY,
            current.LeadLineMain.EndX,
            current.LeadLineMain.EndY);

        var threshold = maximumDistance * policy.RepresentativePacketGapFactor;
        return previousEndToCurrentStart > threshold &&
               previousStartToCurrentEnd > threshold;
    }

    private static DimensionItem SelectRepresentative(
        DimensionGroup group,
        IReadOnlyList<DimensionItem> items,
        int startIndex,
        int endIndex,
        DimensionReductionPolicy policy)
    {
        var count = endIndex - startIndex + 1;
        var selectionMode = ResolveRepresentativeSelectionMode(group, policy);
        var position = selectionMode switch
        {
            DimensionRepresentativeSelectionMode.FirstInPacket => startIndex,
            DimensionRepresentativeSelectionMode.LastInPacket => endIndex,
            _ => endIndex - ((count - 1) / 2)
        };

        return items[position];
    }

    private static DimensionRepresentativeSelectionMode ResolveRepresentativeSelectionMode(
        DimensionGroup group,
        DimensionReductionPolicy policy)
    {
        if (!policy.UseGeometryAwareRepresentativeSelection)
            return policy.RepresentativeSelectionMode;

        return group.DomainDimensionType switch
        {
            DimensionType.Horizontal => policy.HorizontalRepresentativeSelectionMode,
            DimensionType.Vertical => policy.VerticalRepresentativeSelectionMode,
            DimensionType.Free => policy.FreeRepresentativeSelectionMode,
            _ => policy.RepresentativeSelectionMode
        };
    }

    private static bool Covers(
        DimensionItem keeper,
        DimensionItem candidate,
        DimensionReductionPolicy policy)
    {
        if (candidate.PointList.Count < 2 || keeper.PointList.Count < 2)
            return false;

        if (keeper.PointList.Count < candidate.PointList.Count)
            return false;

        var direction = keeper.Direction ?? candidate.Direction;
        if (!direction.HasValue)
            return false;

        var keeperPositions = GetProjectedPositions(keeper.PointList, direction.Value);
        var candidatePositions = GetProjectedPositions(candidate.PointList, direction.Value);
        if (keeperPositions.Count == 0 || candidatePositions.Count == 0)
            return false;

        return candidatePositions.All(candidatePosition =>
            keeperPositions.Any(keeperPosition => System.Math.Abs(keeperPosition - candidatePosition) <= policy.PositionTolerance));
    }

    private static bool AreEquivalentSimpleItems(
        DimensionItem keeper,
        DimensionItem candidate,
        DimensionReductionPolicy policy)
    {
        if (!IsSimpleItem(keeper) || !IsSimpleItem(candidate))
            return false;

        var keeperDirection = keeper.Direction;
        var candidateDirection = candidate.Direction;
        if (!keeperDirection.HasValue || !candidateDirection.HasValue)
            return false;

        if (!AreParallel(keeperDirection.Value, candidateDirection.Value))
            return false;

        var keeperPositions = GetProjectedPositions(keeper.PointList, keeperDirection.Value);
        var candidatePositions = GetProjectedPositions(candidate.PointList, keeperDirection.Value);
        if (keeperPositions.Count != 2 || candidatePositions.Count != 2)
            return false;

        var keeperLength = keeperPositions[1] - keeperPositions[0];
        var candidateLength = candidatePositions[1] - candidatePositions[0];
        if (System.Math.Abs(keeperLength - candidateLength) > policy.LengthTolerance)
            return false;

        var sameStart = System.Math.Abs(keeperPositions[0] - candidatePositions[0]) <= policy.PositionTolerance;
        var sameEnd = System.Math.Abs(keeperPositions[1] - candidatePositions[1]) <= policy.PositionTolerance;
        if (!sameStart || !sameEnd)
            return false;

        var keeperOffset = TryGetReferenceLineOffset(keeper, keeperDirection.Value);
        var candidateOffset = TryGetReferenceLineOffset(candidate, keeperDirection.Value);
        if (!keeperOffset.HasValue || !candidateOffset.HasValue)
            return false;

        return System.Math.Abs(keeperOffset.Value - candidateOffset.Value) <= policy.PositionTolerance;
    }

    private static List<double> GetProjectedPositions(
        IReadOnlyList<DrawingPointInfo> points,
        (double X, double Y) direction)
    {
        return points
            .Select(point => Project(point.X, point.Y, direction.X, direction.Y))
            .OrderBy(static value => value)
            .ToList();
    }

    private static bool IsSimpleItem(DimensionItem item) => item.PointList.Count <= 2;

    private static int GetInformationRank(DimensionItem item)
    {
        return (item.PointList.Count * 1000) + item.SegmentIds.Count;
    }

    private static double GetDistance(double leftX, double leftY, double rightX, double rightY)
    {
        var dx = leftX - rightX;
        var dy = leftY - rightY;
        return System.Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static double? TryGetReferenceLineOffset(DimensionItem item, (double X, double Y) direction)
    {
        if (item.ReferenceLine == null)
            return null;

        var normal = (-direction.Y, direction.X);
        return Project(item.ReferenceLine.StartX, item.ReferenceLine.StartY, normal.Item1, normal.Item2);
    }

    private static bool AreParallel(
        (double X, double Y) left,
        (double X, double Y) right)
    {
        var dot = System.Math.Abs((left.X * right.X) + (left.Y * right.Y));
        return dot >= 0.995;
    }

    private static double Project(double x, double y, double axisX, double axisY) => (x * axisX) + (y * axisY);

    private static DimensionGroup CloneGroup(DimensionGroup group)
    {
        return new DimensionGroup
        {
            ViewId = group.ViewId,
            ViewType = group.ViewType,
            DomainDimensionType = group.DomainDimensionType,
            SourceKind = group.SourceKind,
            GeometryKind = group.GeometryKind,
            TeklaDimensionType = group.TeklaDimensionType,
            Orientation = group.Orientation,
            TopDirection = group.TopDirection,
            Direction = group.Direction,
            Bounds = group.Bounds,
            ReferenceLine = group.ReferenceLine,
            LeadLineMain = group.LeadLineMain,
            LeadLineSecond = group.LeadLineSecond,
            MaximumDistance = group.MaximumDistance,
            RawItemCount = group.RawItemCount
        };
    }
}
