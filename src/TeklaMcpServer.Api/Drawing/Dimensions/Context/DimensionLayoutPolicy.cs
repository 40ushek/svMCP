using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

internal enum DimensionLayoutPolicyStatus
{
    Neutral = 0,
    Preferred,
    LessPreferred
}

internal enum DimensionRecommendedAction
{
    Keep = 0,
    PreferCombine,
    SuppressCandidate,
    OperatorReview
}

internal enum DimensionCombineClassification
{
    None = 0,
    DuplicateChain,
    InformationPreservingMerge
}

internal sealed class DimensionLayoutPolicyDecision
{
    public DimensionLayoutPolicyStatus Status { get; set; }
    public string Reason { get; set; } = string.Empty;
    public int? PreferredDimensionId { get; set; }
    public bool CombineCandidate { get; set; }
    public string CombineReason { get; set; } = string.Empty;
    public List<int> CombineWithDimensionIds { get; } = [];
    public DimensionCombineClassification CombineClassification { get; set; }
    public DimensionRecommendedAction RecommendedAction { get; set; } = DimensionRecommendedAction.Keep;
}

internal sealed class DimensionLayoutPolicy
{
    public static DimensionLayoutPolicy Default => new();

    public double PointMatchTolerance { get; set; } = 3.0;
    public double EquivalentGeometryPointTolerance { get; set; } = 3.0;
    public double ProjectedExtentTolerance { get; set; } = 3.0;
    public int AllowedMissingSharedPoints { get; set; } = 1;
    public bool RequireSameSourceKind { get; set; } = true;
    public bool RequireSharedSourceIdentity { get; set; } = true;
}

internal static class DimensionLayoutPolicyEvaluator
{
    public static IReadOnlyDictionary<DimensionItem, DimensionLayoutPolicyDecision> Evaluate(
        DimensionDecisionContext decisionContext,
        IReadOnlyList<DimensionItem> items,
        DimensionLayoutPolicy? policy = null)
    {
        var contexts = decisionContext.Dimensions
            .Where(static context => context.Item != null)
            .GroupBy(static context => context.Item)
            .ToDictionary(static group => group.Key, static group => group.First());

        return Evaluate(items, contexts, policy);
    }

    public static IReadOnlyDictionary<DimensionItem, DimensionLayoutPolicyDecision> Evaluate(
        IReadOnlyList<DimensionItem> items,
        IReadOnlyDictionary<DimensionItem, DimensionContext> contexts,
        DimensionLayoutPolicy? policy = null)
    {
        policy ??= DimensionLayoutPolicy.Default;
        var decisions = items.ToDictionary(
            static item => item,
            static _ => new DimensionLayoutPolicyDecision
            {
                Status = DimensionLayoutPolicyStatus.Neutral,
                Reason = "neutral"
            });

        ApplyEquivalentGeometryPreferences(items, contexts, policy, decisions);

        var ordered = items
            .OrderByDescending(GetInformationRank)
            .ThenBy(static item => item.DimensionId)
            .ToList();

        foreach (var poorer in ordered.OrderBy(static item => item.PointList.Count).ThenBy(static item => item.DimensionId))
        {
            if (decisions[poorer].Status != DimensionLayoutPolicyStatus.Neutral)
                continue;

            foreach (var richer in ordered)
            {
                if (ReferenceEquals(richer, poorer))
                    continue;

                if (!IsRicherSubchainCandidate(richer, poorer, contexts, policy))
                    continue;

                decisions[poorer] = new DimensionLayoutPolicyDecision
                {
                    Status = DimensionLayoutPolicyStatus.LessPreferred,
                    Reason = "subchain_of_richer_dimension",
                    PreferredDimensionId = richer.DimensionId
                };

                if (decisions[richer].Status == DimensionLayoutPolicyStatus.Neutral)
                {
                    decisions[richer] = new DimensionLayoutPolicyDecision
                    {
                        Status = DimensionLayoutPolicyStatus.Preferred,
                        Reason = "covers_poorer_chain",
                        PreferredDimensionId = richer.DimensionId
                    };
                }

                break;
            }
        }

        return decisions;
    }

    public static void AttachCombineCandidates(
        IReadOnlyDictionary<int, DimensionItem> itemsByDimensionId,
        IReadOnlyDictionary<DimensionItem, DimensionLayoutPolicyDecision> decisions,
        IReadOnlyList<DimensionCombineCandidateDebugInfo> combineCandidates,
        DimensionLayoutPolicy? policy = null)
    {
        policy ??= DimensionLayoutPolicy.Default;
        foreach (var candidate in combineCandidates)
        {
            if (!candidate.IsCombineCandidate || candidate.DimensionIds.Count <= 1)
                continue;

            var matchedItems = candidate.DimensionIds
                .Where(itemsByDimensionId.ContainsKey)
                .Select(dimensionId => itemsByDimensionId[dimensionId])
                .Distinct()
                .ToList();
            if (matchedItems.Count <= 1)
                continue;

            var combineClassification = ClassifyCombineCandidate(matchedItems, candidate, policy);

            foreach (var item in matchedItems)
            {
                if (!decisions.TryGetValue(item, out var decision))
                    continue;

                decision.CombineCandidate = true;
                decision.CombineClassification = combineClassification;
                if (string.IsNullOrWhiteSpace(decision.CombineReason) &&
                    !string.IsNullOrWhiteSpace(candidate.CombineConnectivityMode))
                {
                    decision.CombineReason = candidate.CombineConnectivityMode;
                }

                foreach (var other in matchedItems)
                {
                    if (ReferenceEquals(other, item))
                        continue;

                    if (!decision.CombineWithDimensionIds.Contains(other.DimensionId))
                        decision.CombineWithDimensionIds.Add(other.DimensionId);
                }

                decision.CombineWithDimensionIds.Sort();
            }
        }
    }

    public static void AttachRecommendedActions(
        IReadOnlyDictionary<DimensionItem, DimensionLayoutPolicyDecision> decisions)
    {
        foreach (var decision in decisions.Values)
        {
            decision.RecommendedAction = GetRecommendedAction(decision);
        }
    }

    private static void ApplyEquivalentGeometryPreferences(
        IReadOnlyList<DimensionItem> items,
        IReadOnlyDictionary<DimensionItem, DimensionContext> contexts,
        DimensionLayoutPolicy policy,
        Dictionary<DimensionItem, DimensionLayoutPolicyDecision> decisions)
    {
        var ordered = items
            .OrderBy(GetEquivalentGeometryPriority)
            .ThenBy(static item => item.DimensionId)
            .ToList();

        for (var i = 0; i < ordered.Count; i++)
        {
            var preferred = ordered[i];
            for (var j = i + 1; j < ordered.Count; j++)
            {
                var duplicate = ordered[j];
                if (decisions[duplicate].Status != DimensionLayoutPolicyStatus.Neutral)
                    continue;

                if (!IsEquivalentMeasuredGeometryCandidate(preferred, duplicate, contexts, policy))
                    continue;

                decisions[duplicate] = new DimensionLayoutPolicyDecision
                {
                    Status = DimensionLayoutPolicyStatus.LessPreferred,
                    Reason = "equivalent_measured_geometry",
                    PreferredDimensionId = preferred.DimensionId
                };

                if (decisions[preferred].Status == DimensionLayoutPolicyStatus.Neutral)
                {
                    decisions[preferred] = new DimensionLayoutPolicyDecision
                    {
                        Status = DimensionLayoutPolicyStatus.Preferred,
                        Reason = "keeps_compact_equivalent_geometry",
                        PreferredDimensionId = preferred.DimensionId
                    };
                }
            }
        }
    }

    private static bool IsRicherSubchainCandidate(
        DimensionItem richer,
        DimensionItem poorer,
        IReadOnlyDictionary<DimensionItem, DimensionContext> contexts,
        DimensionLayoutPolicy policy)
    {
        if (richer.PointList.Count <= poorer.PointList.Count)
            return false;

        if (richer.DomainDimensionType != poorer.DomainDimensionType ||
            richer.GeometryKind != poorer.GeometryKind)
        {
            return false;
        }

        if (policy.RequireSameSourceKind && richer.SourceKind != poorer.SourceKind)
            return false;

        if (!TryGetDirection(richer, poorer, out var direction))
            return false;

        if (!IsProjectedExtentCovered(richer, poorer, direction, policy))
            return false;

        var requiredSharedPoints = System.Math.Max(2, poorer.PointList.Count - policy.AllowedMissingSharedPoints);
        if (GetSharedPointCount(richer, poorer, policy.PointMatchTolerance) < requiredSharedPoints)
            return false;

        if (!contexts.TryGetValue(richer, out var richerContext) ||
            !contexts.TryGetValue(poorer, out var poorerContext))
        {
            return false;
        }

        if (ShouldSkipRole(richerContext.Role) || ShouldSkipRole(poorerContext.Role))
            return false;

        if (policy.RequireSharedSourceIdentity &&
            !HaveSharedSourceIdentity(richerContext, poorerContext))
        {
            return false;
        }

        return true;
    }

    private static bool IsEquivalentMeasuredGeometryCandidate(
        DimensionItem preferred,
        DimensionItem duplicate,
        IReadOnlyDictionary<DimensionItem, DimensionContext> contexts,
        DimensionLayoutPolicy policy)
    {
        if (preferred.ViewId != duplicate.ViewId)
            return false;

        if (preferred.DomainDimensionType != duplicate.DomainDimensionType ||
            preferred.GeometryKind != duplicate.GeometryKind ||
            preferred.PointList.Count != duplicate.PointList.Count)
        {
            return false;
        }

        if (policy.RequireSameSourceKind && preferred.SourceKind != duplicate.SourceKind)
            return false;

        if (!HaveEquivalentPointSets(preferred.PointList, duplicate.PointList, policy.EquivalentGeometryPointTolerance))
            return false;

        if (!contexts.TryGetValue(preferred, out var preferredContext) ||
            !contexts.TryGetValue(duplicate, out var duplicateContext))
        {
            return false;
        }

        if (preferredContext.Role == DimensionContextRole.Grid || duplicateContext.Role == DimensionContextRole.Grid)
            return false;

        if (ShouldRequireSharedSourceIdentity(preferredContext, duplicateContext) &&
            policy.RequireSharedSourceIdentity &&
            !HaveSharedSourceIdentity(preferredContext, duplicateContext))
        {
            return false;
        }

        return true;
    }

    private static bool TryGetDirection(DimensionItem richer, DimensionItem poorer, out (double X, double Y) direction)
    {
        if (richer.Direction.HasValue)
        {
            direction = richer.Direction.Value;
            return true;
        }

        if (poorer.Direction.HasValue)
        {
            direction = poorer.Direction.Value;
            return true;
        }

        direction = default;
        return false;
    }

    private static bool IsProjectedExtentCovered(
        DimensionItem richer,
        DimensionItem poorer,
        (double X, double Y) direction,
        DimensionLayoutPolicy policy)
    {
        var richerExtent = GetProjectedExtent(richer.PointList, direction);
        var poorerExtent = GetProjectedExtent(poorer.PointList, direction);
        if (!richerExtent.HasValue || !poorerExtent.HasValue)
            return false;

        return poorerExtent.Value.Min >= richerExtent.Value.Min - policy.ProjectedExtentTolerance &&
               poorerExtent.Value.Max <= richerExtent.Value.Max + policy.ProjectedExtentTolerance;
    }

    private static (double Min, double Max)? GetProjectedExtent(
        IReadOnlyList<DrawingPointInfo> points,
        (double X, double Y) direction)
    {
        if (points.Count == 0)
            return null;

        var values = points
            .Select(point => Project(point.X, point.Y, direction.X, direction.Y))
            .ToList();
        return (values.Min(), values.Max());
    }

    private static int GetSharedPointCount(DimensionItem richer, DimensionItem poorer, double tolerance)
    {
        return poorer.PointList.Count(point =>
            richer.PointList.Any(candidate =>
                System.Math.Abs(point.X - candidate.X) <= tolerance &&
                System.Math.Abs(point.Y - candidate.Y) <= tolerance));
    }

    private static bool HaveEquivalentPointSets(
        IReadOnlyList<DrawingPointInfo> left,
        IReadOnlyList<DrawingPointInfo> right,
        double tolerance)
    {
        if (left.Count != right.Count)
            return false;

        var matched = new bool[right.Count];
        foreach (var point in left)
        {
            var found = false;
            for (var i = 0; i < right.Count; i++)
            {
                if (matched[i])
                    continue;

                if (System.Math.Abs(point.X - right[i].X) > tolerance ||
                    System.Math.Abs(point.Y - right[i].Y) > tolerance)
                {
                    continue;
                }

                matched[i] = true;
                found = true;
                break;
            }

            if (!found)
                return false;
        }

        return true;
    }

    private static DimensionCombineClassification ClassifyCombineCandidate(
        IReadOnlyList<DimensionItem> matchedItems,
        DimensionCombineCandidateDebugInfo candidate,
        DimensionLayoutPolicy policy)
    {
        var previewPoints = candidate.CombinePreview?.PointList;
        if (previewPoints == null || previewPoints.Count == 0)
            return DimensionCombineClassification.InformationPreservingMerge;

        var normalizedPreviewPoints = NormalizePointSet(previewPoints, policy.EquivalentGeometryPointTolerance);
        foreach (var item in matchedItems)
        {
            var normalizedItemPoints = NormalizePointSet(item.PointList, policy.EquivalentGeometryPointTolerance);
            if (HaveEquivalentPointSets(normalizedItemPoints, normalizedPreviewPoints, policy.EquivalentGeometryPointTolerance))
                return DimensionCombineClassification.DuplicateChain;
        }

        return DimensionCombineClassification.InformationPreservingMerge;
    }

    private static List<DrawingPointInfo> NormalizePointSet(
        IReadOnlyList<DrawingPointInfo> points,
        double tolerance)
    {
        var normalized = new List<DrawingPointInfo>(points.Count);
        foreach (var point in points)
        {
            if (normalized.Any(existing =>
                System.Math.Abs(existing.X - point.X) <= tolerance &&
                System.Math.Abs(existing.Y - point.Y) <= tolerance))
            {
                continue;
            }

            normalized.Add(new DrawingPointInfo
            {
                X = point.X,
                Y = point.Y,
                Order = point.Order
            });
        }

        return normalized;
    }

    private static bool ShouldSkipRole(DimensionContextRole role)
    {
        return role == DimensionContextRole.Control || role == DimensionContextRole.Grid;
    }

    private static bool ShouldRequireSharedSourceIdentity(DimensionContext left, DimensionContext right)
    {
        return left.Role != DimensionContextRole.Control &&
               right.Role != DimensionContextRole.Control;
    }

    private static bool HaveSharedSourceIdentity(DimensionContext richer, DimensionContext poorer)
    {
        var richerSourceKeys = GetSourceKeys(richer);
        var poorerSourceKeys = GetSourceKeys(poorer);
        if (richerSourceKeys.Count > 0 && poorerSourceKeys.Count > 0)
            return richerSourceKeys.Overlaps(poorerSourceKeys);

        if (richer.SourceModelIds.Count > 0 && poorer.SourceModelIds.Count > 0 &&
            richer.SourceModelIds.Intersect(poorer.SourceModelIds).Any())
        {
            return true;
        }

        if (richer.SourceDrawingObjectIds.Count > 0 && poorer.SourceDrawingObjectIds.Count > 0 &&
            richer.SourceDrawingObjectIds.Intersect(poorer.SourceDrawingObjectIds).Any())
        {
            return true;
        }

        return false;
    }

    private static HashSet<int> GetSourceKeys(DimensionContext context)
    {
        var keys = new HashSet<int>();
        foreach (var association in context.PointAssociations)
        {
            if (association.MatchedModelId.HasValue && association.MatchedModelId.Value > 0)
                keys.Add(association.MatchedModelId.Value);
            else if (association.MatchedDrawingObjectId.HasValue && association.MatchedDrawingObjectId.Value > 0)
                keys.Add(association.MatchedDrawingObjectId.Value);
        }

        return keys;
    }

    private static int GetInformationRank(DimensionItem item) => (item.PointList.Count * 1000) + item.SegmentIds.Count;

    private static double GetEquivalentGeometryPriority(DimensionItem item) => System.Math.Abs(item.Distance);

    private static double Project(double x, double y, double axisX, double axisY) => (x * axisX) + (y * axisY);

    private static DimensionRecommendedAction GetRecommendedAction(DimensionLayoutPolicyDecision decision)
    {
        if (decision.CombineCandidate &&
            decision.CombineClassification == DimensionCombineClassification.InformationPreservingMerge)
        {
            return DimensionRecommendedAction.PreferCombine;
        }

        if (decision.Status == DimensionLayoutPolicyStatus.LessPreferred &&
            decision.Reason == "equivalent_measured_geometry")
        {
            return DimensionRecommendedAction.SuppressCandidate;
        }

        if (decision.CombineCandidate &&
            decision.CombineClassification == DimensionCombineClassification.DuplicateChain)
        {
            return decision.Status == DimensionLayoutPolicyStatus.LessPreferred
                ? DimensionRecommendedAction.OperatorReview
                : DimensionRecommendedAction.Keep;
        }

        if (decision.CombineCandidate)
            return DimensionRecommendedAction.OperatorReview;

        if (decision.Status == DimensionLayoutPolicyStatus.LessPreferred)
            return DimensionRecommendedAction.OperatorReview;

        return DimensionRecommendedAction.Keep;
    }
}
