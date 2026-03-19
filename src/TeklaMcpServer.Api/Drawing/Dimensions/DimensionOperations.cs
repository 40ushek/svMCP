using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

internal static class DimensionOperations
{
    public static List<DimensionGroup> EliminateRedundantItems(
        IReadOnlyList<DimensionGroup> groups,
        DimensionReductionPolicy? policy = null)
    {
        return EliminateRedundantItemsWithDebug(groups, policy).ReducedGroups;
    }

    public static DimensionReductionDebugResult EliminateRedundantItemsWithDebug(
        IReadOnlyList<DimensionGroup> groups,
        DimensionReductionPolicy? policy = null)
    {
        policy ??= DimensionReductionPolicy.Default;
        var result = new DimensionReductionDebugResult();

        foreach (var group in groups)
        {
            var (reducedItems, itemDebug, packetDebug) = ReduceItems(group, policy);
            if (reducedItems.Count == 0)
                continue;

            var rawGroup = CloneGroup(group);
            rawGroup.DimensionList.AddRange(group.DimensionList);
            rawGroup.SortMembers();
            rawGroup.RefreshMetrics();

            var reducedGroup = CloneGroup(group);
            reducedGroup.DimensionList.AddRange(reducedItems);
            reducedGroup.SortMembers();
            reducedGroup.RefreshMetrics();
            result.ReducedGroups.Add(reducedGroup);
            var debugGroup = new DimensionGroupReductionDebugInfo
            {
                RawGroup = rawGroup,
                ReducedGroup = reducedGroup
            };
            debugGroup.Items.AddRange(itemDebug);
            debugGroup.Packets.AddRange(packetDebug);
            result.Groups.Add(debugGroup);
        }

        return result;
    }

    private static (
        List<DimensionItem> ReducedItems,
        List<DimensionReductionItemDebugInfo> ItemDebug,
        List<DimensionRepresentativePacketDebugInfo> PacketDebug) ReduceItems(
        DimensionGroup group,
        DimensionReductionPolicy policy)
    {
        var items = group.DimensionList;
        if (items.Count <= 1)
        {
            return (
                items.ToList(),
                items.Select(static item => new DimensionReductionItemDebugInfo
                {
                    Item = item,
                    Status = "kept",
                    Reason = "kept"
                }).ToList(),
                []);
        }

        var ordered = items
            .OrderByDescending(GetInformationRank)
            .ThenBy(static item => item.DimensionId)
            .ThenBy(static item => item.SortKey)
            .ToList();
        var debugByItem = items.ToDictionary(
            static item => item,
            static item => new DimensionReductionItemDebugInfo
            {
                Item = item,
                Status = "pending",
                Reason = string.Empty
            });

        var kept = new List<DimensionItem>(ordered.Count);
        foreach (var candidate in ordered)
        {
            if (policy.EnableEquivalentSimpleReduction &&
                IsSimpleItem(candidate) &&
                kept.Any(existing => AreEquivalentSimpleItems(existing, candidate, policy)))
            {
                debugByItem[candidate].Status = "rejected";
                debugByItem[candidate].Reason = "equivalent_simple";
                continue;
            }

            if (policy.EnableCoverageReduction &&
                IsSimpleItem(candidate) &&
                kept.Any(existing => Covers(existing, candidate, policy)))
            {
                debugByItem[candidate].Status = "rejected";
                debugByItem[candidate].Reason = "covered";
                continue;
            }

            kept.Add(candidate);
            debugByItem[candidate].Status = "kept";
            debugByItem[candidate].Reason = "kept";
        }

        var deduplicated = kept
            .OrderBy(static item => item.SortKey)
            .ThenBy(static item => item.DimensionId)
            .ToList();

        List<DimensionRepresentativePacketDebugInfo> packetDebug = [];
        var reduced = policy.EnableRepresentativeSelection
            ? SelectCommonRepresentatives(group, deduplicated, policy, debugByItem, packetDebug)
            : deduplicated;

        return (
            reduced,
            debugByItem.Values.OrderBy(static info => info.Item.SortKey).ThenBy(static info => info.Item.DimensionId).ToList(),
            packetDebug);
    }

    private static List<DimensionItem> SelectCommonRepresentatives(
        DimensionGroup group,
        IReadOnlyList<DimensionItem> items,
        DimensionReductionPolicy policy,
        Dictionary<DimensionItem, DimensionReductionItemDebugInfo> debugByItem,
        List<DimensionRepresentativePacketDebugInfo> packetDebug)
    {
        if (items.Count <= 1)
            return items.ToList();

        var selected = new List<DimensionItem>();
        var packetStart = 0;
        var packetIndex = 0;

        for (var i = 1; i < items.Count; i++)
        {
            var split = EvaluateRepresentativePacketSplit(items[i - 1], items[i], group.MaximumDistance, policy);
            if (!split.ShouldSplit)
                continue;

            selected.Add(SelectRepresentative(group, items, packetStart, i - 1, policy, packetIndex, debugByItem));
            packetDebug.Add(CreatePacketDebug(group, items, packetStart, i - 1, packetIndex, policy, split));
            packetStart = i;
            packetIndex++;
        }

        selected.Add(SelectRepresentative(group, items, packetStart, items.Count - 1, policy, packetIndex, debugByItem));
        packetDebug.Add(CreatePacketDebug(group, items, packetStart, items.Count - 1, packetIndex, policy, splitInfo: null));
        return selected;
    }

    private static RepresentativePacketSplitInfo EvaluateRepresentativePacketSplit(
        DimensionItem previous,
        DimensionItem current,
        double maximumDistance,
        DimensionReductionPolicy policy)
    {
        if (previous.LeadLineMain == null || current.LeadLineMain == null)
        {
            return new RepresentativePacketSplitInfo
            {
                ShouldSplit = true,
                Threshold = maximumDistance * policy.RepresentativePacketGapFactor
            };
        }

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
        return new RepresentativePacketSplitInfo
        {
            ShouldSplit = previousEndToCurrentStart > threshold &&
                          previousStartToCurrentEnd > threshold,
            PreviousEndToCurrentStart = previousEndToCurrentStart,
            PreviousStartToCurrentEnd = previousStartToCurrentEnd,
            Threshold = threshold
        };
    }

    private static DimensionItem SelectRepresentative(
        DimensionGroup group,
        IReadOnlyList<DimensionItem> items,
        int startIndex,
        int endIndex,
        DimensionReductionPolicy policy,
        int packetIndex,
        Dictionary<DimensionItem, DimensionReductionItemDebugInfo> debugByItem)
    {
        var count = endIndex - startIndex + 1;
        var selectionMode = ResolveRepresentativeSelectionMode(group, policy);
        var position = selectionMode switch
        {
            DimensionRepresentativeSelectionMode.FirstInPacket => startIndex,
            DimensionRepresentativeSelectionMode.LastInPacket => endIndex,
            _ => endIndex - ((count - 1) / 2)
        };

        var representative = items[position];
        for (var i = startIndex; i <= endIndex; i++)
        {
            var item = items[i];
            var debug = debugByItem[item];
            debug.PacketIndex = packetIndex;
            debug.RepresentativeDimensionId = representative.DimensionId;

            if (count == 1)
            {
                debug.Status = "kept";
                debug.Reason = "kept";
                continue;
            }

            if (ReferenceEquals(item, representative))
            {
                debug.Status = "kept";
                debug.Reason = "representative_packet";
            }
            else
            {
                debug.Status = "rejected";
                debug.Reason = "representative_packet";
            }
        }

        return representative;
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

    private static DimensionRepresentativePacketDebugInfo CreatePacketDebug(
        DimensionGroup group,
        IReadOnlyList<DimensionItem> items,
        int startIndex,
        int endIndex,
        int packetIndex,
        DimensionReductionPolicy policy,
        RepresentativePacketSplitInfo? splitInfo)
    {
        var selectionMode = ResolveRepresentativeSelectionMode(group, policy);
        var count = endIndex - startIndex + 1;
        var representativeIndex = selectionMode switch
        {
            DimensionRepresentativeSelectionMode.FirstInPacket => startIndex,
            DimensionRepresentativeSelectionMode.LastInPacket => endIndex,
            _ => endIndex - ((count - 1) / 2)
        };

        var packet = new DimensionRepresentativePacketDebugInfo
        {
            PacketIndex = packetIndex,
            StartDimensionId = items[startIndex].DimensionId,
            EndDimensionId = items[endIndex].DimensionId,
            ItemCount = count,
            SelectionMode = selectionMode.ToString(),
            RepresentativeDimensionId = items[representativeIndex].DimensionId,
            SplitGapFromPreviousEndToCurrentStart = splitInfo?.PreviousEndToCurrentStart,
            SplitGapFromPreviousStartToCurrentEnd = splitInfo?.PreviousStartToCurrentEnd,
            SplitThreshold = splitInfo?.Threshold ?? (group.MaximumDistance * policy.RepresentativePacketGapFactor)
        };

        for (var i = startIndex; i <= endIndex; i++)
            packet.DimensionIds.Add(items[i].DimensionId);

        var blockingReasons = GetCombineBlockingReasons(items, startIndex, endIndex, policy);
        packet.IsCombineCandidate = blockingReasons.Count == 0;
        packet.BlockingReasons.AddRange(blockingReasons);

        return packet;
    }

    private static List<string> GetCombineBlockingReasons(
        IReadOnlyList<DimensionItem> items,
        int startIndex,
        int endIndex,
        DimensionReductionPolicy policy)
    {
        var reasons = new List<string>();
        var count = endIndex - startIndex + 1;
        if (count <= 1)
        {
            reasons.Add("single_item_packet");
            return reasons;
        }

        var first = items[startIndex];
        var firstDirection = first.Direction;
        if (!firstDirection.HasValue)
        {
            reasons.Add("missing_direction");
            return reasons;
        }

        for (var i = startIndex + 1; i <= endIndex; i++)
        {
            var current = items[i];
            var currentDirection = current.Direction;
            if (!currentDirection.HasValue || !AreParallel(firstDirection.Value, currentDirection.Value))
            {
                reasons.Add("inconsistent_direction");
                break;
            }

            if (current.TopDirection != 0 && first.TopDirection != 0 && current.TopDirection != first.TopDirection)
            {
                reasons.Add("different_top_direction");
                break;
            }

            if (!AreOnSameMeasuredLine(first, current, policy))
            {
                reasons.Add("different_measured_line");
                break;
            }
        }

        if (!HasPacketMeasuredPointConnectivity(items, startIndex, endIndex, policy))
            reasons.Add("no_shared_or_adjacent_measured_points");

        return reasons;
    }

    private static bool HasPacketMeasuredPointConnectivity(
        IReadOnlyList<DimensionItem> items,
        int startIndex,
        int endIndex,
        DimensionReductionPolicy policy)
    {
        for (var i = startIndex + 1; i <= endIndex; i++)
        {
            if (HaveSharedMeasuredPoint(items[i - 1], items[i], policy) ||
                HaveAdjacentMeasuredPointOrders(items[i - 1], items[i]))
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private static bool HaveSharedMeasuredPoint(
        DimensionItem left,
        DimensionItem right,
        DimensionReductionPolicy policy)
    {
        return (left.StartPointOrder >= 0 && (left.StartPointOrder == right.StartPointOrder || left.StartPointOrder == right.EndPointOrder)) ||
               (left.EndPointOrder >= 0 && (left.EndPointOrder == right.StartPointOrder || left.EndPointOrder == right.EndPointOrder)) ||
               left.PointList.Any(leftPoint => right.PointList.Any(rightPoint =>
                   System.Math.Abs(leftPoint.X - rightPoint.X) <= policy.PositionTolerance &&
                   System.Math.Abs(leftPoint.Y - rightPoint.Y) <= policy.PositionTolerance));
    }

    private static bool HaveAdjacentMeasuredPointOrders(DimensionItem left, DimensionItem right)
    {
        if (left.StartPointOrder < 0 || left.EndPointOrder < 0 || right.StartPointOrder < 0 || right.EndPointOrder < 0)
            return false;

        var leftOrders = new[] { left.StartPointOrder, left.EndPointOrder };
        var rightOrders = new[] { right.StartPointOrder, right.EndPointOrder };
        return leftOrders.Any(leftOrder => rightOrders.Any(rightOrder => System.Math.Abs(leftOrder - rightOrder) <= 1));
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

        if (!AreOnSameMeasuredLine(keeper, candidate, policy))
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

        if (!AreOnSameMeasuredLine(keeper, candidate, policy))
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

    private static bool AreOnSameMeasuredLine(
        DimensionItem left,
        DimensionItem right,
        DimensionReductionPolicy policy)
    {
        var direction = left.Direction ?? right.Direction;
        if (!direction.HasValue)
            return false;

        var normal = (-direction.Value.Y, direction.Value.X);
        var leftStart = Project(left.StartX, left.StartY, normal.Item1, normal.Item2);
        var leftEnd = Project(left.EndX, left.EndY, normal.Item1, normal.Item2);
        var rightStart = Project(right.StartX, right.StartY, normal.Item1, normal.Item2);
        var rightEnd = Project(right.EndX, right.EndY, normal.Item1, normal.Item2);

        return System.Math.Abs(leftStart - rightStart) <= policy.MeasuredLineTolerance &&
               System.Math.Abs(leftEnd - rightEnd) <= policy.MeasuredLineTolerance;
    }

    private static bool AreParallel(
        (double X, double Y) left,
        (double X, double Y) right)
    {
        var dot = System.Math.Abs((left.X * right.X) + (left.Y * right.Y));
        return dot >= 0.995;
    }

    private static double Project(double x, double y, double axisX, double axisY) => (x * axisX) + (y * axisY);

    private sealed class RepresentativePacketSplitInfo
    {
        public bool ShouldSplit { get; set; }
        public double PreviousEndToCurrentStart { get; set; }
        public double PreviousStartToCurrentEnd { get; set; }
        public double Threshold { get; set; }
    }

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
