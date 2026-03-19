using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

internal static class DimensionOperations
{
    private const double PositionTolerance = 3.0;

    public static List<DimensionGroup> EliminateRedundantItems(IReadOnlyList<DimensionGroup> groups)
    {
        var reducedGroups = new List<DimensionGroup>(groups.Count);

        foreach (var group in groups)
        {
            var reducedItems = ReduceItems(group.DimensionList);
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

    private static List<DimensionItem> ReduceItems(IReadOnlyList<DimensionItem> items)
    {
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
            if (IsSimpleItem(candidate) && kept.Any(existing => Covers(existing, candidate)))
                continue;

            kept.Add(candidate);
        }

        return kept
            .OrderBy(static item => item.SortKey)
            .ThenBy(static item => item.DimensionId)
            .ToList();
    }

    private static bool Covers(DimensionItem keeper, DimensionItem candidate)
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
            keeperPositions.Any(keeperPosition => System.Math.Abs(keeperPosition - candidatePosition) <= PositionTolerance));
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
            MaximumDistance = group.MaximumDistance
        };
    }
}
