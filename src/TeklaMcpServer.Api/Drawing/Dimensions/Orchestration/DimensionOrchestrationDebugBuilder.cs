using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

internal static class DimensionOrchestrationDebugBuilder
{
    public static DimensionOrchestrationDebugResult Build(DimensionReductionDebugResult debug, int? viewId)
    {
        var effectiveViewId = viewId ?? debug.DecisionContext.View.ViewId;
        var result = new DimensionOrchestrationDebugResult
        {
            ViewId = effectiveViewId
        };
        foreach (var warning in debug.DecisionContext.Warnings.Concat(debug.DecisionContext.View.Warnings).Distinct())
            result.Warnings.Add(warning);

        var itemsById = debug.Groups
            .SelectMany(static group => group.Items)
            .Where(static item => item.Item != null)
            .GroupBy(static item => item.Item.DimensionId)
            .ToDictionary(static grouping => grouping.Key, static grouping => grouping.First());
        var contextsById = debug.DecisionContext.Dimensions
            .GroupBy(static context => context.DimensionId)
            .ToDictionary(static group => group.Key, static group => group.First());
        var orderedItems = OrderItems(debug.DecisionContext, itemsById);
        var claimedDimensionIds = new HashSet<int>();

        foreach (var packet in BuildCombinePackets(debug.Groups, itemsById, contextsById, debug.DecisionContext.View, claimedDimensionIds))
            result.Packets.Add(packet);

        foreach (var packet in BuildSuppressPackets(orderedItems, contextsById, debug.DecisionContext.View, claimedDimensionIds))
            result.Packets.Add(packet);

        foreach (var packet in BuildReviewPackets(orderedItems, contextsById, debug.DecisionContext.View, claimedDimensionIds))
            result.Packets.Add(packet);

        foreach (var packet in BuildKeepPackets(orderedItems, contextsById, debug.DecisionContext.View, claimedDimensionIds))
            result.Packets.Add(packet);

        return result;
    }

    private static List<DimensionReductionItemDebugInfo> OrderItems(
        DimensionDecisionContext decisionContext,
        IReadOnlyDictionary<int, DimensionReductionItemDebugInfo> itemsById)
    {
        if (decisionContext.Dimensions.Count > 0)
        {
            return decisionContext.Dimensions
                .Select(context => itemsById.TryGetValue(context.DimensionId, out var item) ? item : null)
                .Where(static item => item != null)
                .Distinct()
                .ToList()!;
        }

        return itemsById.Values
            .OrderBy(static item => item.Item.ViewId)
            .ThenBy(static item => item.Item.DimensionType)
            .ThenBy(static item => item.Item.SortKey)
            .ThenBy(static item => item.Item.DimensionId)
            .ToList();
    }

    private static IEnumerable<DimensionOrchestrationActionPacket> BuildCombinePackets(
        IReadOnlyList<DimensionGroupReductionDebugInfo> groups,
        IReadOnlyDictionary<int, DimensionReductionItemDebugInfo> itemsById,
        IReadOnlyDictionary<int, DimensionContext> contextsById,
        DrawingViewContext viewContext,
        HashSet<int> claimedDimensionIds)
    {
        foreach (var group in groups)
        {
            var eligibleCandidates = group.CombineCandidates
                .Where(static candidate => candidate.IsCombineCandidate && candidate.DimensionIds.Count > 1)
                .GroupBy(CreateCandidateKey)
                .Select(static grouping => grouping.First())
                .Where(candidate => IsInformationPreservingCombineCandidate(candidate, itemsById))
                .ToList();
            if (eligibleCandidates.Count == 0)
                continue;

            var overlapCounts = new Dictionary<int, int>();
            foreach (var candidate in eligibleCandidates)
            {
                foreach (var dimensionId in candidate.DimensionIds.Distinct())
                    overlapCounts[dimensionId] = overlapCounts.TryGetValue(dimensionId, out var count) ? count + 1 : 1;
            }

            var overlappingIds = overlapCounts
                .Where(static pair => pair.Value > 1)
                .Select(static pair => pair.Key)
                .ToHashSet();
            if (overlappingIds.Count > 0)
            {
                foreach (var packet in BuildOverlappingCombineReviewPackets(
                             eligibleCandidates,
                             overlappingIds,
                             itemsById,
                             contextsById,
                             viewContext,
                             claimedDimensionIds))
                    yield return packet;
            }

            foreach (var candidate in eligibleCandidates)
            {
                var candidateDimensionIds = candidate.DimensionIds
                    .Distinct()
                    .OrderBy(static id => id)
                    .ToList();
                if (candidateDimensionIds.Count <= 1)
                    continue;

                if (candidateDimensionIds.Any(overlappingIds.Contains) ||
                    candidateDimensionIds.Any(claimedDimensionIds.Contains))
                {
                    continue;
                }

                var primaryDimensionId = candidate.CombinePreview?.BaseDimensionId ?? candidateDimensionIds[0];
                if (!itemsById.TryGetValue(primaryDimensionId, out var primaryItem))
                    primaryItem = itemsById[candidateDimensionIds[0]];

                var packet = new DimensionOrchestrationActionPacket
                {
                    Action = DimensionOrchestrationAction.Combine,
                    PrimaryDimensionId = primaryDimensionId,
                    ViewId = primaryItem.Item.ViewId,
                    DimensionType = primaryItem.Item.DimensionType,
                    Reason = "information_preserving_merge",
                    Source = "fused",
                    Evidence = CreateEvidence(
                        primaryItem,
                        contextsById.TryGetValue(primaryDimensionId, out var primaryContext) ? primaryContext : primaryItem.Context,
                        viewContext,
                        candidate.CombineConnectivityMode)
                };

                packet.DimensionIds.AddRange(candidateDimensionIds);
                packet.RelatedDimensionIds.AddRange(candidateDimensionIds.Where(id => id != primaryDimensionId));
                foreach (var dimensionId in candidateDimensionIds)
                    claimedDimensionIds.Add(dimensionId);

                yield return packet;
            }
        }
    }

    private static IEnumerable<DimensionOrchestrationActionPacket> BuildOverlappingCombineReviewPackets(
        IReadOnlyList<DimensionCombineCandidateDebugInfo> eligibleCandidates,
        HashSet<int> overlappingIds,
        IReadOnlyDictionary<int, DimensionReductionItemDebugInfo> itemsById,
        IReadOnlyDictionary<int, DimensionContext> contextsById,
        DrawingViewContext viewContext,
        HashSet<int> claimedDimensionIds)
    {
        var overlappingCandidates = eligibleCandidates
            .Where(candidate => candidate.DimensionIds.Any(overlappingIds.Contains))
            .ToList();
        var adjacency = new Dictionary<int, HashSet<int>>();

        foreach (var candidate in overlappingCandidates)
        {
            var ids = candidate.DimensionIds
                .Where(itemsById.ContainsKey)
                .Distinct()
                .ToList();
            foreach (var id in ids)
            {
                if (!adjacency.TryGetValue(id, out var neighbors))
                {
                    neighbors = [];
                    adjacency[id] = neighbors;
                }

                foreach (var otherId in ids)
                {
                    if (otherId != id)
                        neighbors.Add(otherId);
                }
            }
        }

        var visited = new HashSet<int>();
        foreach (var startId in adjacency.Keys.OrderBy(static id => id))
        {
            if (!visited.Add(startId))
                continue;

            var queue = new Queue<int>();
            var component = new List<int>();
            queue.Enqueue(startId);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                component.Add(current);
                foreach (var neighbor in adjacency[current].OrderBy(static id => id))
                {
                    if (visited.Add(neighbor))
                        queue.Enqueue(neighbor);
                }
            }

            component.Sort();
            if (component.Count == 0 || component.Any(claimedDimensionIds.Contains))
                continue;

            var primaryDimensionId = component[0];
            var primaryItem = itemsById[primaryDimensionId];
            var combineConnectivityMode = string.Join(
                ",",
                overlappingCandidates
                    .Where(candidate => candidate.DimensionIds.Any(component.Contains))
                    .Select(static candidate => candidate.CombineConnectivityMode)
                    .Where(static value => !string.IsNullOrWhiteSpace(value))
                    .Distinct()
                    .OrderBy(static value => value));

            var packet = new DimensionOrchestrationActionPacket
            {
                Action = DimensionOrchestrationAction.Review,
                PrimaryDimensionId = primaryDimensionId,
                ViewId = primaryItem.Item.ViewId,
                DimensionType = primaryItem.Item.DimensionType,
                Reason = "overlapping_combine_candidates",
                Source = "fused",
                Evidence = CreateEvidence(
                    primaryItem,
                    contextsById.TryGetValue(primaryDimensionId, out var primaryContext) ? primaryContext : primaryItem.Context,
                    viewContext,
                    combineConnectivityMode)
            };

            packet.DimensionIds.AddRange(component);
            packet.RelatedDimensionIds.AddRange(component.Where(id => id != primaryDimensionId));

            foreach (var dimensionId in component)
                claimedDimensionIds.Add(dimensionId);

            yield return packet;
        }
    }

    private static IEnumerable<DimensionOrchestrationActionPacket> BuildSuppressPackets(
        IReadOnlyList<DimensionReductionItemDebugInfo> orderedItems,
        IReadOnlyDictionary<int, DimensionContext> contextsById,
        DrawingViewContext viewContext,
        HashSet<int> claimedDimensionIds)
    {
        foreach (var item in orderedItems)
        {
            var dimensionId = item.Item.DimensionId;
            if (claimedDimensionIds.Contains(dimensionId))
                continue;

            var hasLayoutSuppress = item.LayoutPolicy?.RecommendedAction == DimensionRecommendedAction.SuppressCandidate;
            var hasReductionSuppress = item.Status == "rejected" &&
                                       (item.Reason == "covered" || item.Reason == "equivalent_simple");
            if (!hasLayoutSuppress && !hasReductionSuppress)
                continue;

            var packet = new DimensionOrchestrationActionPacket
            {
                Action = DimensionOrchestrationAction.Suppress,
                PrimaryDimensionId = dimensionId,
                ViewId = item.Item.ViewId,
                DimensionType = item.Item.DimensionType,
                Reason = hasLayoutSuppress ? "exact_duplicate" : NormalizeReductionReason(item.Reason),
                Source = hasLayoutSuppress && hasReductionSuppress
                    ? "fused"
                    : hasLayoutSuppress
                        ? "layout_policy"
                        : "reduction",
                Evidence = CreateEvidence(
                    item,
                    contextsById.TryGetValue(dimensionId, out var context) ? context : item.Context,
                    viewContext)
            };

            packet.DimensionIds.Add(dimensionId);
            claimedDimensionIds.Add(dimensionId);
            yield return packet;
        }
    }

    private static IEnumerable<DimensionOrchestrationActionPacket> BuildReviewPackets(
        IReadOnlyList<DimensionReductionItemDebugInfo> orderedItems,
        IReadOnlyDictionary<int, DimensionContext> contextsById,
        DrawingViewContext viewContext,
        HashSet<int> claimedDimensionIds)
    {
        foreach (var item in orderedItems)
        {
            var dimensionId = item.Item.DimensionId;
            if (claimedDimensionIds.Contains(dimensionId))
                continue;

            if (item.LayoutPolicy?.RecommendedAction != DimensionRecommendedAction.OperatorReview)
                continue;

            var packet = new DimensionOrchestrationActionPacket
            {
                Action = DimensionOrchestrationAction.Review,
                PrimaryDimensionId = dimensionId,
                ViewId = item.Item.ViewId,
                DimensionType = item.Item.DimensionType,
                Reason = string.IsNullOrWhiteSpace(item.LayoutPolicy.Reason)
                    ? "operator_review"
                    : item.LayoutPolicy.Reason,
                Source = string.IsNullOrWhiteSpace(item.Status)
                    ? "layout_policy"
                    : "fused",
                Evidence = CreateEvidence(
                    item,
                    contextsById.TryGetValue(dimensionId, out var context) ? context : item.Context,
                    viewContext)
            };

            packet.DimensionIds.Add(dimensionId);
            claimedDimensionIds.Add(dimensionId);
            yield return packet;
        }
    }

    private static IEnumerable<DimensionOrchestrationActionPacket> BuildKeepPackets(
        IReadOnlyList<DimensionReductionItemDebugInfo> orderedItems,
        IReadOnlyDictionary<int, DimensionContext> contextsById,
        DrawingViewContext viewContext,
        HashSet<int> claimedDimensionIds)
    {
        foreach (var item in orderedItems)
        {
            var dimensionId = item.Item.DimensionId;
            if (claimedDimensionIds.Contains(dimensionId) || item.Status != "kept")
                continue;

            var packet = new DimensionOrchestrationActionPacket
            {
                Action = DimensionOrchestrationAction.Keep,
                PrimaryDimensionId = dimensionId,
                ViewId = item.Item.ViewId,
                DimensionType = item.Item.DimensionType,
                Reason = "keep",
                Source = item.LayoutPolicy != null ? "fused" : "reduction",
                Evidence = CreateEvidence(
                    item,
                    contextsById.TryGetValue(dimensionId, out var context) ? context : item.Context,
                    viewContext)
            };

            packet.DimensionIds.Add(dimensionId);
            claimedDimensionIds.Add(dimensionId);
            yield return packet;
        }
    }

    private static bool IsInformationPreservingCombineCandidate(
        DimensionCombineCandidateDebugInfo candidate,
        IReadOnlyDictionary<int, DimensionReductionItemDebugInfo> itemsById)
    {
        var matchedItems = candidate.DimensionIds
            .Where(itemsById.ContainsKey)
            .Select(dimensionId => itemsById[dimensionId])
            .Distinct()
            .ToList();
        if (matchedItems.Count <= 1)
            return false;

        return matchedItems.All(static item => item.LayoutPolicy?.CombineClassification == DimensionCombineClassification.InformationPreservingMerge) &&
               matchedItems.Any(static item => item.LayoutPolicy?.RecommendedAction == DimensionRecommendedAction.PreferCombine);
    }

    private static DimensionOrchestrationEvidence CreateEvidence(
        DimensionReductionItemDebugInfo item,
        DimensionContext? context,
        DrawingViewContext viewContext,
        string? combineConnectivityMode = null)
    {
        var viewPlacement = DimensionViewPlacementInfoBuilder.Build(context, viewContext);
        var partsBoundsGap = DimensionPartsBoundsGapPolicy.Evaluate(viewPlacement);
        return new DimensionOrchestrationEvidence
        {
            LayoutPolicyStatus = item.LayoutPolicy?.Status.ToString() ?? string.Empty,
            LayoutRecommendedAction = item.LayoutPolicy?.RecommendedAction.ToString() ?? string.Empty,
            LayoutCombineClassification = item.LayoutPolicy?.CombineClassification.ToString() ?? string.Empty,
            ReductionStatus = item.Status,
            ReductionReason = item.Reason,
            CombineConnectivityMode = combineConnectivityMode ?? item.LayoutPolicy?.CombineReason ?? string.Empty,
            PreferredDimensionId = item.LayoutPolicy?.PreferredDimensionId,
            RepresentativeDimensionId = item.RepresentativeDimensionId,
            HasPartsBounds = viewPlacement.HasPartsBounds,
            PartsBoundsSide = viewPlacement.PartsBoundsSide,
            IsOutsidePartsBounds = viewPlacement.IsOutsidePartsBounds,
            IntersectsPartsBounds = viewPlacement.IntersectsPartsBounds,
            OffsetFromPartsBounds = viewPlacement.OffsetFromPartsBounds ?? 0,
            ReferenceLineLength = viewPlacement.ReferenceLineLength ?? 0,
            Distance = viewPlacement.Distance,
            TopDirection = viewPlacement.TopDirection,
            ViewScale = viewPlacement.ViewScale,
            CanEvaluatePartsBoundsGap = partsBoundsGap.CanEvaluate,
            CurrentPartsBoundsGapDrawing = partsBoundsGap.CurrentGapDrawing,
            TargetPartsBoundsGapPaper = partsBoundsGap.TargetGapPaper,
            TargetPartsBoundsGapDrawing = partsBoundsGap.TargetGapDrawing,
            RequiresPartsBoundsGapCorrection = partsBoundsGap.RequiresOutwardCorrection,
            SuggestedOutwardDeltaFromPartsBounds = partsBoundsGap.SuggestedOutwardDeltaDrawing
        };
    }

    private static string NormalizeReductionReason(string reason)
    {
        return reason switch
        {
            "covered" => "covered",
            "equivalent_simple" => "equivalent_simple",
            _ => string.IsNullOrWhiteSpace(reason) ? "reduction_rejected" : reason
        };
    }

    private static string CreateCandidateKey(DimensionCombineCandidateDebugInfo candidate)
    {
        var dimensionIds = string.Join(",", candidate.DimensionIds.Distinct().OrderBy(static id => id));
        return $"{candidate.CombineConnectivityMode}|{dimensionIds}";
    }
}
