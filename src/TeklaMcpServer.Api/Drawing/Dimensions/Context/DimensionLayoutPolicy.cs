using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

internal enum DimensionLayoutPolicyStatus
{
    Neutral = 0,
    Preferred,
    LessPreferred
}

internal sealed class DimensionLayoutPolicyDecision
{
    public DimensionLayoutPolicyStatus Status { get; set; }
    public string Reason { get; set; } = string.Empty;
    public int? PreferredDimensionId { get; set; }
}

internal sealed class DimensionLayoutPolicy
{
    public static DimensionLayoutPolicy Default => new();

    public double PointMatchTolerance { get; set; } = 3.0;
    public double ProjectedExtentTolerance { get; set; } = 3.0;
    public int AllowedMissingSharedPoints { get; set; } = 1;
    public bool RequireSameSourceKind { get; set; } = true;
    public bool RequireSharedSourceIdentity { get; set; } = true;
}

internal static class DimensionLayoutPolicyEvaluator
{
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

        var ordered = items
            .OrderByDescending(GetInformationRank)
            .ThenBy(static item => item.DimensionId)
            .ToList();

        foreach (var poorer in ordered.OrderBy(static item => item.PointList.Count).ThenBy(static item => item.DimensionId))
        {
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

    private static bool ShouldSkipRole(DimensionContextRole role)
    {
        return role == DimensionContextRole.Control || role == DimensionContextRole.Grid;
    }

    private static bool HaveSharedSourceIdentity(DimensionContext richer, DimensionContext poorer)
    {
        var richerSourceKeys = GetSourceKeys(richer);
        var poorerSourceKeys = GetSourceKeys(poorer);
        if (richerSourceKeys.Count > 0 && poorerSourceKeys.Count > 0)
            return richerSourceKeys.Overlaps(poorerSourceKeys);

        return richer.SourceObjectIds.Intersect(poorer.SourceObjectIds).Any();
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

    private static double Project(double x, double y, double axisX, double axisY) => (x * axisX) + (y * axisY);
}
