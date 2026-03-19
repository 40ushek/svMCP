using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

internal static class DimensionOperations
{
    public static List<DimensionGroup> EliminateRedundantItems(
        IReadOnlyList<DimensionGroup> groups,
        DimensionReductionPolicy? policy = null,
        DimensionCombinePolicy? combinePolicy = null)
    {
        return EliminateRedundantItemsWithDebug(groups, policy, combinePolicy).ReducedGroups;
    }

    public static DimensionReductionDebugResult EliminateRedundantItemsWithDebug(
        IReadOnlyList<DimensionGroup> groups,
        DimensionReductionPolicy? policy = null,
        DimensionCombinePolicy? combinePolicy = null)
    {
        policy ??= DimensionReductionPolicy.Default;
        combinePolicy ??= DimensionCombinePolicy.Default;
        var result = new DimensionReductionDebugResult();

        foreach (var group in groups)
        {
            var (reducedItems, itemDebug, packetDebug) = ReduceItems(group, policy, combinePolicy);
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
        DimensionReductionPolicy policy,
        DimensionCombinePolicy combinePolicy)
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
            ? SelectCommonRepresentatives(group, deduplicated, policy, combinePolicy, debugByItem, packetDebug)
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
        DimensionCombinePolicy combinePolicy,
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
            packetDebug.Add(CreatePacketDebug(group, items, packetStart, i - 1, packetIndex, policy, combinePolicy, split));
            packetStart = i;
            packetIndex++;
        }

        selected.Add(SelectRepresentative(group, items, packetStart, items.Count - 1, policy, packetIndex, debugByItem));
        packetDebug.Add(CreatePacketDebug(group, items, packetStart, items.Count - 1, packetIndex, policy, combinePolicy, splitInfo: null));
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
        DimensionCombinePolicy combinePolicy,
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

        var combineAnalysis = AnalyzeCombineCandidate(
            group,
            items,
            startIndex,
            endIndex,
            policy,
            combinePolicy,
            items[representativeIndex]);
        packet.IsCombineCandidate = combineAnalysis.BlockingReasons.Count == 0;
        packet.CombineConnectivityMode = combineAnalysis.ConnectivityMode;
        packet.BlockingReasons.AddRange(combineAnalysis.BlockingReasons);
        packet.CombinePreview = combineAnalysis.Preview;

        return packet;
    }

    private static CombineCandidateAnalysis AnalyzeCombineCandidate(
        DimensionGroup group,
        IReadOnlyList<DimensionItem> items,
        int startIndex,
        int endIndex,
        DimensionReductionPolicy policy,
        DimensionCombinePolicy combinePolicy,
        DimensionItem representativeItem)
    {
        var analysis = new CombineCandidateAnalysis();
        var count = endIndex - startIndex + 1;
        if (count <= 1)
        {
            analysis.ConnectivityMode = "single_item_packet";
            analysis.BlockingReasons.Add("single_item_packet");
            return analysis;
        }

        if (group.DomainDimensionType == DimensionType.Free && !combinePolicy.AllowFreeDimensionCombine)
        {
            analysis.ConnectivityMode = "free_dimension_blocked";
            analysis.BlockingReasons.Add("free_dimension_combine_disabled");
            return analysis;
        }

        var first = items[startIndex];
        var firstDirection = first.Direction;
        if (!firstDirection.HasValue)
        {
            analysis.ConnectivityMode = "missing_direction";
            analysis.BlockingReasons.Add("missing_direction");
            return analysis;
        }

        for (var i = startIndex + 1; i <= endIndex; i++)
        {
            var current = items[i];
            var currentDirection = current.Direction;
            if (!currentDirection.HasValue || !AreParallel(firstDirection.Value, currentDirection.Value))
            {
                analysis.ConnectivityMode = "inconsistent_direction";
                analysis.BlockingReasons.Add("inconsistent_direction");
                return analysis;
            }

            if (combinePolicy.RequireSameTopDirection &&
                current.TopDirection != 0 &&
                first.TopDirection != 0 &&
                current.TopDirection != first.TopDirection)
            {
                analysis.ConnectivityMode = "different_top_direction";
                analysis.BlockingReasons.Add("different_top_direction");
                return analysis;
            }

            if (!AreOnSameMeasuredLine(first, current, policy))
            {
                analysis.ConnectivityMode = "different_measured_line";
                analysis.BlockingReasons.Add("different_measured_line");
                return analysis;
            }
        }

        if (combinePolicy.RequireReferenceLineOffsetCompatibility)
        {
            var firstOffset = TryGetReferenceLineOffset(first, firstDirection.Value);
            if (!firstOffset.HasValue)
            {
                analysis.ConnectivityMode = "missing_reference_line";
                analysis.BlockingReasons.Add("missing_reference_line");
                return analysis;
            }

            for (var i = startIndex + 1; i <= endIndex; i++)
            {
                var currentOffset = TryGetReferenceLineOffset(items[i], firstDirection.Value);
                if (!currentOffset.HasValue)
                {
                    analysis.ConnectivityMode = "missing_reference_line";
                    analysis.BlockingReasons.Add("missing_reference_line");
                    return analysis;
                }

                if (System.Math.Abs(firstOffset.Value - currentOffset.Value) > combinePolicy.ReferenceLineOffsetTolerance)
                {
                    analysis.ConnectivityMode = "different_reference_line_band";
                    analysis.BlockingReasons.Add("different_reference_line_band");
                    return analysis;
                }
            }
        }

        if (combinePolicy.RequireDistanceCompatibility)
        {
            for (var i = startIndex + 1; i <= endIndex; i++)
            {
                if (System.Math.Abs(first.Distance - items[i].Distance) > combinePolicy.DistanceTolerance)
                {
                    analysis.ConnectivityMode = "distance_delta_exceeds_tolerance";
                    analysis.BlockingReasons.Add("distance_delta_exceeds_tolerance");
                    return analysis;
                }
            }
        }

        if (combinePolicy.RequireSameSourceKind)
        {
            for (var i = startIndex + 1; i <= endIndex; i++)
            {
                if (items[i].SourceKind != first.SourceKind)
                {
                    analysis.ConnectivityMode = "different_source_kind";
                    analysis.BlockingReasons.Add("different_source_kind");
                    return analysis;
                }
            }
        }

        var connectivity = AnalyzePacketMeasuredPointConnectivity(items, startIndex, endIndex, policy);
        analysis.ConnectivityMode = connectivity.Mode;
        if (!connectivity.IsConnected)
        {
            analysis.BlockingReasons.Add("no_shared_or_adjacent_measured_points");
            return analysis;
        }

        if (!connectivity.HasSharedPointChain && !combinePolicy.AllowAdjacentMeasuredPointOrderFallback)
        {
            analysis.BlockingReasons.Add("requires_adjacent_order_fallback");
            return analysis;
        }

        if (analysis.BlockingReasons.Count == 0)
        {
            var previewBaseItem = combinePolicy.UseRepresentativeAsPreviewBase
                ? representativeItem
                : first;
            analysis.Preview = BuildCombinePreview(items, startIndex, endIndex, previewBaseItem);
        }

        return analysis;
    }

    private static DimensionCombinePreviewDebugInfo BuildCombinePreview(
        IReadOnlyList<DimensionItem> items,
        int startIndex,
        int endIndex,
        DimensionItem baseItem)
    {
        var preview = new DimensionCombinePreviewDebugInfo
        {
            BaseDimensionId = baseItem.DimensionId,
            Distance = baseItem.Distance
        };

        for (var i = startIndex; i <= endIndex; i++)
            preview.DimensionIds.Add(items[i].DimensionId);

        var direction = baseItem.Direction;
        var orderedPoints = items
            .Skip(startIndex)
            .Take(endIndex - startIndex + 1)
            .SelectMany(static item => item.PointList)
            .GroupBy(static point => point.Order >= 0
                ? $"o:{point.Order}"
                : $"p:{System.Math.Round(point.X, 3)}:{System.Math.Round(point.Y, 3)}")
            .Select(static group => group.First())
            .ToList();

        if (direction.HasValue)
        {
            orderedPoints = orderedPoints
                .OrderBy(point => Project(point.X, point.Y, direction.Value.X, direction.Value.Y))
                .ThenBy(static point => point.Order)
                .ToList();
        }
        else
        {
            orderedPoints = orderedPoints
                .OrderBy(static point => point.Order)
                .ThenBy(static point => point.X)
                .ThenBy(static point => point.Y)
                .ToList();
        }

        if (orderedPoints.Count == 0)
            return preview;

        preview.PointList.AddRange(orderedPoints.Select(static point => new DrawingPointInfo
        {
            X = point.X,
            Y = point.Y,
            Order = point.Order
        }));

        preview.StartPoint = new DrawingPointInfo
        {
            X = orderedPoints[0].X,
            Y = orderedPoints[0].Y,
            Order = orderedPoints[0].Order
        };
        var lastPointIndex = orderedPoints.Count - 1;
        preview.EndPoint = new DrawingPointInfo
        {
            X = orderedPoints[lastPointIndex].X,
            Y = orderedPoints[lastPointIndex].Y,
            Order = orderedPoints[lastPointIndex].Order
        };

        var startX = orderedPoints[0].X;
        var startY = orderedPoints[0].Y;
        for (var i = 1; i < orderedPoints.Count; i++)
        {
            var point = orderedPoints[i];
            var length = System.Math.Round(
                System.Math.Sqrt(
                    System.Math.Pow(point.X - startX, 2) +
                    System.Math.Pow(point.Y - startY, 2)),
                2);
            preview.LengthList.Add(length);
        }

        return preview;
    }

    private static PacketConnectivityAnalysis AnalyzePacketMeasuredPointConnectivity(
        IReadOnlyList<DimensionItem> items,
        int startIndex,
        int endIndex,
        DimensionReductionPolicy policy)
    {
        var usedAdjacentFallback = false;
        var hasSharedPointChain = true;

        for (var i = startIndex + 1; i <= endIndex; i++)
        {
            var previous = items[i - 1];
            var current = items[i];
            var hasSharedPoint = HaveSharedMeasuredPoint(previous, current, policy);
            var hasAdjacentOrder = HaveAdjacentMeasuredPointOrders(previous, current);

            if (hasSharedPoint)
                continue;

            hasSharedPointChain = false;
            if (hasAdjacentOrder)
            {
                usedAdjacentFallback = true;
                continue;
            }

            return new PacketConnectivityAnalysis
            {
                IsConnected = false,
                HasSharedPointChain = false,
                UsedAdjacentOrderFallback = usedAdjacentFallback,
                Mode = usedAdjacentFallback ? "broken_mixed_chain" : "not_connected"
            };
        }

        return new PacketConnectivityAnalysis
        {
            IsConnected = true,
            HasSharedPointChain = hasSharedPointChain,
            UsedAdjacentOrderFallback = usedAdjacentFallback,
            Mode = hasSharedPointChain
                ? "shared_point_chain"
                : (usedAdjacentFallback ? "adjacent_order_fallback" : "connected")
        };
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

        return HasDimStyleEqualPositions(keeper, candidate, policy, direction.Value);
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

        return HasDimStyleEqualPositions(keeper, candidate, policy, keeperDirection.Value) &&
               HasDimStyleEqualPositions(candidate, keeper, policy, keeperDirection.Value);
    }

    private static bool HasDimStyleEqualPositions(
        DimensionItem item,
        DimensionItem originalItem,
        DimensionReductionPolicy policy,
        (double X, double Y) direction)
    {
        if (originalItem.LengthList.Count == 0 || item.PointList.Count < 2)
            return false;

        var originLength = originalItem.LengthList[0];
        var probeHalfLength = originalItem.GetLeadLineMainLength();
        if (probeHalfLength <= 0)
            probeHalfLength = item.GetLeadLineMainLength();
        if (probeHalfLength <= 0)
            probeHalfLength = originLength;

        var normal = (direction.Y, -direction.X);
        var matchingIndices = GetLengthMatchedPointIndices(item, originLength, policy);
        if (matchingIndices.Count == 0)
            return false;

        foreach (var matchedIndex in matchingIndices)
        {
            if (matchedIndex >= item.PointList.Count)
                continue;

            if (IsPointOnPerpendicularProbe(
                    item.PointList[0],
                    originalItem.StartPoint,
                    direction,
                    normal,
                    probeHalfLength,
                    policy) &&
                IsPointOnPerpendicularProbe(
                    item.PointList[matchedIndex],
                    originalItem.EndPoint,
                    direction,
                    normal,
                    probeHalfLength,
                    policy))
            {
                return true;
            }
        }

        return false;
    }

    private static List<int> GetLengthMatchedPointIndices(
        DimensionItem item,
        double originLength,
        DimensionReductionPolicy policy)
    {
        var indices = new List<int>();
        for (var i = 0; i < item.LengthList.Count; i++)
        {
            if (System.Math.Abs(item.LengthList[i] - originLength) <= policy.LengthTolerance)
                indices.Add(i + 1);
        }

        return indices;
    }

    private static bool IsPointOnPerpendicularProbe(
        DrawingPointInfo point,
        DrawingPointInfo anchor,
        (double X, double Y) direction,
        (double X, double Y) normal,
        double halfLength,
        DimensionReductionPolicy policy)
    {
        var anchorDirection = Project(anchor.X, anchor.Y, direction.X, direction.Y);
        var pointDirection = Project(point.X, point.Y, direction.X, direction.Y);
        if (System.Math.Abs(anchorDirection - pointDirection) > policy.PositionTolerance)
            return false;

        var anchorNormal = Project(anchor.X, anchor.Y, normal.X, normal.Y);
        var pointNormal = Project(point.X, point.Y, normal.X, normal.Y);
        return pointNormal >= anchorNormal - halfLength - policy.PositionTolerance &&
               pointNormal <= anchorNormal + halfLength + policy.PositionTolerance;
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

    private sealed class CombineCandidateAnalysis
    {
        public string ConnectivityMode { get; set; } = string.Empty;
        public List<string> BlockingReasons { get; } = [];
        public DimensionCombinePreviewDebugInfo? Preview { get; set; }
    }

    private sealed class PacketConnectivityAnalysis
    {
        public bool IsConnected { get; set; }
        public bool HasSharedPointChain { get; set; }
        public bool UsedAdjacentOrderFallback { get; set; }
        public string Mode { get; set; } = string.Empty;
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
