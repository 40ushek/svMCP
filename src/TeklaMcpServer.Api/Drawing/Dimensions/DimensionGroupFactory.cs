using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

internal static class DimensionGroupFactory
{
    public static List<DimensionItem> BuildItems(
        IEnumerable<DrawingDimensionInfo> dimensions,
        DimensionGroupingPolicy? groupingPolicy = null)
    {
        groupingPolicy ??= DimensionGroupingPolicy.Default;
        return dimensions
            .SelectMany(dimension => CreateItems(dimension, groupingPolicy))
            .ToList();
    }

    public static List<DimensionGroup> BuildGroups(
        IEnumerable<DrawingDimensionInfo> dimensions,
        DimensionGroupingPolicy? groupingPolicy = null,
        DimensionReductionPolicy? reductionPolicy = null)
    {
        groupingPolicy ??= DimensionGroupingPolicy.Default;
        reductionPolicy ??= DimensionReductionPolicy.Default;

        var items = BuildItems(dimensions, groupingPolicy);
        var groups = BuildConnectedGroups(items, groupingPolicy);

        foreach (var group in groups)
        {
            group.SortMembers();
            group.RefreshMetrics();
            group.RawItemCount = group.DimensionList.Count;
        }

        return DimensionOperations.EliminateRedundantItems(groups, reductionPolicy);
    }

    private static List<DimensionGroup> BuildConnectedGroups(
        IReadOnlyList<DimensionItem> items,
        DimensionGroupingPolicy groupingPolicy)
    {
        var groups = new List<DimensionGroup>();
        var visited = new bool[items.Count];

        for (var i = 0; i < items.Count; i++)
        {
            if (visited[i])
                continue;

            var component = new List<DimensionItem>();
            var queue = new Queue<int>();
            queue.Enqueue(i);
            visited[i] = true;

            while (queue.Count > 0)
            {
                var index = queue.Dequeue();
                var current = items[index];
                component.Add(current);

                for (var candidateIndex = 0; candidateIndex < items.Count; candidateIndex++)
                {
                    if (visited[candidateIndex])
                        continue;

                    if (!CanGroupItemsTogether(current, items[candidateIndex], groupingPolicy))
                        continue;

                    visited[candidateIndex] = true;
                    queue.Enqueue(candidateIndex);
                }
            }

            groups.Add(CreateGroup(component));
        }

        return groups;
    }

    private static IEnumerable<DimensionItem> CreateItems(
        DrawingDimensionInfo dimension,
        DimensionGroupingPolicy groupingPolicy)
    {
        var segmentItems = dimension.Segments
            .Select(segment => CreateItem(dimension, segment))
            .Where(static item => item != null)
            .Cast<DimensionItem>()
            .ToList();

        if (segmentItems.Count > 0)
            return MergeItemsIntoChains(dimension, segmentItems, groupingPolicy);

        return [CreateFallbackItem(dimension)];
    }

    private static IReadOnlyList<DimensionItem> MergeItemsIntoChains(
        DrawingDimensionInfo dimension,
        IReadOnlyList<DimensionItem> rawItems,
        DimensionGroupingPolicy groupingPolicy)
    {
        if (rawItems.Count <= 1 || dimension.MeasuredPoints.Count == 0)
            return rawItems.ToList();

        var chains = new List<List<DimensionItem>>();
        var visited = new bool[rawItems.Count];

        for (var i = 0; i < rawItems.Count; i++)
        {
            if (visited[i])
                continue;

            var component = new List<DimensionItem>();
            var queue = new Queue<int>();
            queue.Enqueue(i);
            visited[i] = true;

            while (queue.Count > 0)
            {
                var currentIndex = queue.Dequeue();
                var current = rawItems[currentIndex];
                component.Add(current);

                for (var candidateIndex = 0; candidateIndex < rawItems.Count; candidateIndex++)
                {
                    if (visited[candidateIndex])
                        continue;

                    if (!CanChainItemsWithinDimension(current, rawItems[candidateIndex], groupingPolicy))
                        continue;

                    visited[candidateIndex] = true;
                    queue.Enqueue(candidateIndex);
                }
            }

            chains.Add(component);
        }

        return chains.Select(chain => CreateChainItem(dimension, chain)).ToList();
    }

    private static bool CanChainItemsWithinDimension(
        DimensionItem left,
        DimensionItem right,
        DimensionGroupingPolicy groupingPolicy)
    {
        if (left.DimensionId != right.DimensionId)
            return false;

        if (left.DomainDimensionType != right.DomainDimensionType)
            return false;

        var leftDirection = TryGetItemDirection(left);
        var rightDirection = TryGetItemDirection(right);
        if (leftDirection.HasValue &&
            rightDirection.HasValue &&
            !AreParallel(leftDirection.Value, rightDirection.Value, groupingPolicy))
            return false;

        if (left.TopDirection != 0 && right.TopDirection != 0 && left.TopDirection != right.TopDirection)
            return false;

        return HaveAdjacentMeasuredPointOrders(left, right) || HaveSharedMeasuredPoint(left, right, groupingPolicy);
    }

    private static DimensionItem CreateChainItem(
        DrawingDimensionInfo dimension,
        IReadOnlyList<DimensionItem> chain)
    {
        var orderedChain = chain
            .OrderBy(item => System.Math.Min(item.StartPointOrder, item.EndPointOrder))
            .ThenBy(item => System.Math.Max(item.StartPointOrder, item.EndPointOrder))
            .ToList();

        var first = orderedChain[0];
        var last = orderedChain[orderedChain.Count - 1];
        var minOrder = orderedChain.Min(item => System.Math.Min(item.StartPointOrder, item.EndPointOrder));
        var maxOrder = orderedChain.Max(item => System.Math.Max(item.StartPointOrder, item.EndPointOrder));

        var pointList = dimension.MeasuredPoints
            .Where(point => point.Order >= minOrder && point.Order <= maxOrder)
            .OrderBy(point => point.Order)
            .Select(static point => new DrawingPointInfo
            {
                X = point.X,
                Y = point.Y,
                Order = point.Order
            })
            .ToList();

        if (pointList.Count < 2)
        {
            pointList =
            [
                new DrawingPointInfo { X = first.StartX, Y = first.StartY, Order = minOrder },
                new DrawingPointInfo { X = last.EndX, Y = last.EndY, Order = maxOrder }
            ];
        }

        var item = new DimensionItem
        {
            DimensionId = dimension.Id,
            ViewId = dimension.ViewId,
            ViewType = dimension.ViewType ?? string.Empty,
            ViewScale = dimension.ViewScale,
            DomainDimensionType = first.DomainDimensionType,
            SourceKind = first.SourceKind,
            GeometryKind = first.GeometryKind,
            TeklaDimensionType = first.TeklaDimensionType,
            Distance = dimension.Distance,
            DirectionX = first.DirectionX,
            DirectionY = first.DirectionY,
            TopDirection = first.TopDirection,
            Bounds = CombineItemBounds(orderedChain),
            ReferenceLine = dimension.ReferenceLine != null ? CopyLine(dimension.ReferenceLine) : CopyLine(first.ReferenceLine),
            LeadLineMain = CopyLine(first.LeadLineMain),
            LeadLineSecond = CopyLine(last.LeadLineSecond),
            Dimension = dimension
        };

        foreach (var segmentId in orderedChain.SelectMany(static item => item.SegmentIds).Distinct())
            item.SegmentIds.Add(segmentId);

        item.ReplacePointList(pointList);
        return item;
    }

    private static DrawingBoundsInfo? CombineItemBounds(IEnumerable<DimensionItem> items)
    {
        return TeklaDrawingDimensionsApi.CombineBounds(items.Select(static item => item.Bounds));
    }

    private static DimensionGroup CreateGroup(IReadOnlyList<DimensionItem> items)
    {
        var item = items[0];
        var group = new DimensionGroup
        {
            ViewId = item.ViewId,
            ViewType = item.ViewType,
            DomainDimensionType = ResolveGroupDimensionType(items),
            SourceKind = ResolveGroupSourceKind(items),
            GeometryKind = ResolveGroupGeometryKind(items),
            TeklaDimensionType = ResolveGroupTeklaDimensionType(items),
            Orientation = DetermineGroupOrientation(item),
            TopDirection = item.TopDirection,
            Direction = TryGetItemDirection(item),
            ReferenceLine = CopyLine(item.ReferenceLine)
        };

        foreach (var groupItem in items)
            AddItem(group, groupItem);

        return group;
    }

    private static void AddItem(DimensionGroup group, DimensionItem item)
    {
        item.SortKey = DetermineSortKey(item, group.Direction);
        group.DimensionList.Add(item);

        if (group.Direction == null)
            group.Direction = TryGetItemDirection(item);

        if (group.ReferenceLine == null && item.ReferenceLine != null)
            group.ReferenceLine = CopyLine(item.ReferenceLine);
    }

    private static bool CanGroupItemsTogether(
        DimensionItem left,
        DimensionItem right,
        DimensionGroupingPolicy groupingPolicy)
    {
        if (left.ViewId != right.ViewId)
            return false;

        if (!string.Equals(left.ViewType, right.ViewType, System.StringComparison.Ordinal))
            return false;

        if (left.DomainDimensionType != right.DomainDimensionType)
            return false;

        if (left.TopDirection != 0 && right.TopDirection != 0 && left.TopDirection != right.TopDirection)
            return false;

        var leftDirection = TryGetItemDirection(left);
        var rightDirection = TryGetItemDirection(right);
        if (leftDirection.HasValue &&
            rightDirection.HasValue &&
            !AreParallel(leftDirection.Value, rightDirection.Value, groupingPolicy))
            return false;

        var sharedMeasuredPoint = HaveSharedMeasuredPoint(left, right, groupingPolicy);
        if (sharedMeasuredPoint && HaveCompatibleChainTransition(left, right, leftDirection ?? rightDirection, groupingPolicy))
            return true;

        if (HaveCompatibleSameLineChain(left, right, leftDirection ?? rightDirection, groupingPolicy))
            return true;

        if (!HaveCompatibleLineBand(left, right, leftDirection ?? rightDirection, groupingPolicy))
            return false;

        if (sharedMeasuredPoint)
            return true;

        return HaveCompatibleLeadLines(left, right, groupingPolicy) &&
               HaveCompatibleExtents(left, right, leftDirection ?? rightDirection, groupingPolicy);
    }

    private static bool HaveCompatibleChainTransition(
        DimensionItem left,
        DimensionItem right,
        (double X, double Y)? direction,
        DimensionGroupingPolicy groupingPolicy)
    {
        if (!direction.HasValue)
            return false;

        var leftOffset = TryGetLineOffset(left.ReferenceLine, direction.Value);
        var rightOffset = TryGetLineOffset(right.ReferenceLine, direction.Value);
        if (!leftOffset.HasValue || !rightOffset.HasValue)
            return false;

        return System.Math.Abs(leftOffset.Value - rightOffset.Value) <= groupingPolicy.ChainBandTolerance;
    }

    private static bool HaveCompatibleSameLineChain(
        DimensionItem left,
        DimensionItem right,
        (double X, double Y)? direction,
        DimensionGroupingPolicy groupingPolicy)
    {
        if (!direction.HasValue)
            return false;

        var leftOffset = TryGetLineOffset(left.ReferenceLine, direction.Value);
        var rightOffset = TryGetLineOffset(right.ReferenceLine, direction.Value);
        if (!leftOffset.HasValue || !rightOffset.HasValue)
            return false;

        if (System.Math.Abs(leftOffset.Value - rightOffset.Value) > groupingPolicy.ChainBandTolerance)
            return false;

        var leftExtent = TryGetExtent([left.ReferenceLine, left.LeadLineMain, left.LeadLineSecond], direction.Value);
        var rightExtent = TryGetExtent([right.ReferenceLine, right.LeadLineMain, right.LeadLineSecond], direction.Value);
        if (!leftExtent.HasValue || !rightExtent.HasValue)
            return false;

        return leftExtent.Value.Min <= rightExtent.Value.Max + groupingPolicy.ChainExtentGapTolerance &&
               rightExtent.Value.Min <= leftExtent.Value.Max + groupingPolicy.ChainExtentGapTolerance;
    }

    private static bool HaveCompatibleLineBand(
        DimensionItem left,
        DimensionItem right,
        (double X, double Y)? direction,
        DimensionGroupingPolicy groupingPolicy)
    {
        if (!direction.HasValue)
            return false;

        var leftOffset = TryGetLineOffset(left.ReferenceLine, direction.Value);
        var rightOffset = TryGetLineOffset(right.ReferenceLine, direction.Value);
        if (!leftOffset.HasValue || !rightOffset.HasValue)
            return false;

        return System.Math.Abs(leftOffset.Value - rightOffset.Value) <= groupingPolicy.LineBandTolerance;
    }

    private static bool HaveSharedMeasuredPoint(
        DimensionItem left,
        DimensionItem right,
        DimensionGroupingPolicy groupingPolicy)
    {
        return (left.StartPointOrder >= 0 && (left.StartPointOrder == right.StartPointOrder || left.StartPointOrder == right.EndPointOrder)) ||
               (left.EndPointOrder >= 0 && (left.EndPointOrder == right.StartPointOrder || left.EndPointOrder == right.EndPointOrder)) ||
               PointsEqual(left.StartX, left.StartY, right.StartX, right.StartY, groupingPolicy) ||
               PointsEqual(left.StartX, left.StartY, right.EndX, right.EndY, groupingPolicy) ||
               PointsEqual(left.EndX, left.EndY, right.StartX, right.StartY, groupingPolicy) ||
               PointsEqual(left.EndX, left.EndY, right.EndX, right.EndY, groupingPolicy);
    }

    private static bool HaveAdjacentMeasuredPointOrders(DimensionItem left, DimensionItem right)
    {
        if (left.StartPointOrder < 0 || left.EndPointOrder < 0 || right.StartPointOrder < 0 || right.EndPointOrder < 0)
            return false;

        var leftOrders = new[] { left.StartPointOrder, left.EndPointOrder };
        var rightOrders = new[] { right.StartPointOrder, right.EndPointOrder };
        return leftOrders.Any(leftOrder => rightOrders.Any(rightOrder => System.Math.Abs(leftOrder - rightOrder) <= 1));
    }

    private static bool HaveCompatibleLeadLines(
        DimensionItem left,
        DimensionItem right,
        DimensionGroupingPolicy groupingPolicy)
    {
        var leftLines = new[] { left.LeadLineMain, left.LeadLineSecond }.Where(static line => line != null).Cast<DrawingLineInfo>().ToList();
        var rightLines = new[] { right.LeadLineMain, right.LeadLineSecond }.Where(static line => line != null).Cast<DrawingLineInfo>().ToList();

        if (leftLines.Count == 0 || rightLines.Count == 0)
            return false;

        return leftLines.Any(leftLine => rightLines.Any(rightLine => AreCollinear(leftLine, rightLine, groupingPolicy)));
    }

    private static bool HaveCompatibleExtents(
        DimensionItem left,
        DimensionItem right,
        (double X, double Y)? direction,
        DimensionGroupingPolicy groupingPolicy)
    {
        if (!direction.HasValue)
            return false;

        var leftExtent = TryGetExtent([left.ReferenceLine, left.LeadLineMain, left.LeadLineSecond], direction.Value)
            ?? TryGetExtent([left.Bounds], direction.Value);
        var rightExtent = TryGetExtent([right.ReferenceLine, right.LeadLineMain, right.LeadLineSecond], direction.Value)
            ?? TryGetExtent([right.Bounds], direction.Value);

        if (!leftExtent.HasValue || !rightExtent.HasValue)
            return false;

        return leftExtent.Value.Min <= rightExtent.Value.Max + groupingPolicy.ExtentOverlapTolerance &&
               rightExtent.Value.Min <= leftExtent.Value.Max + groupingPolicy.ExtentOverlapTolerance;
    }

    private static (double Min, double Max)? TryGetExtent(IEnumerable<DrawingLineInfo?> lines, (double X, double Y) direction)
    {
        var values = lines
            .Where(static line => line != null)
            .Cast<DrawingLineInfo>()
            .SelectMany(line => new[]
            {
                Project(line.StartX, line.StartY, direction.X, direction.Y),
                Project(line.EndX, line.EndY, direction.X, direction.Y)
            })
            .ToList();

        return values.Count == 0 ? null : (values.Min(), values.Max());
    }

    private static (double Min, double Max)? TryGetExtent(IEnumerable<DrawingBoundsInfo?> bounds, (double X, double Y) direction)
    {
        var values = bounds
            .Where(static bounds => bounds != null)
            .Cast<DrawingBoundsInfo>()
            .SelectMany(bounds => new[]
            {
                Project(bounds.MinX, bounds.MinY, direction.X, direction.Y),
                Project(bounds.MinX, bounds.MaxY, direction.X, direction.Y),
                Project(bounds.MaxX, bounds.MinY, direction.X, direction.Y),
                Project(bounds.MaxX, bounds.MaxY, direction.X, direction.Y)
            })
            .ToList();

        return values.Count == 0 ? null : (values.Min(), values.Max());
    }

    private static double DetermineSortKey(DimensionItem item, (double X, double Y)? direction)
    {
        if (!direction.HasValue)
            return item.ReferenceLine != null
                ? Project(item.ReferenceLine.StartX, item.ReferenceLine.StartY, 1, 1)
                : item.StartX + item.StartY;

        var normal = (-direction.Value.Y, direction.Value.X);
        if (item.ReferenceLine != null)
            return Project(item.ReferenceLine.StartX, item.ReferenceLine.StartY, normal.Item1, normal.Item2);

        return Project(item.StartX, item.StartY, normal.Item1, normal.Item2);
    }

    private static DrawingLineInfo? CopyLine(DrawingLineInfo? line)
    {
        return line == null
            ? null
            : TeklaDrawingDimensionsApi.CreateLineInfo(line.StartX, line.StartY, line.EndX, line.EndY);
    }

    private static (double X, double Y)? TryGetItemDirection(DimensionItem item)
    {
        return TeklaDrawingDimensionsApi.TryNormalizeDirection(item.DirectionX, item.DirectionY, out var direction)
            ? direction
            : null;
    }

    private static bool AreParallel(
        (double X, double Y) left,
        (double X, double Y) right,
        DimensionGroupingPolicy groupingPolicy)
    {
        var dot = System.Math.Abs((left.X * right.X) + (left.Y * right.Y));
        return dot >= groupingPolicy.ParallelDotTolerance;
    }

    private static bool AreCollinear(
        DrawingLineInfo left,
        DrawingLineInfo right,
        DimensionGroupingPolicy groupingPolicy)
    {
        if (!TeklaDrawingDimensionsApi.TryNormalizeDirection(left.EndX - left.StartX, left.EndY - left.StartY, out var leftDirection))
            return false;
        if (!TeklaDrawingDimensionsApi.TryNormalizeDirection(right.EndX - right.StartX, right.EndY - right.StartY, out var rightDirection))
            return false;
        if (!AreParallel(leftDirection, rightDirection, groupingPolicy))
            return false;

        return DistancePointToInfiniteLine(right.StartX, right.StartY, left) <= groupingPolicy.LineCollinearityTolerance &&
               DistancePointToInfiniteLine(right.EndX, right.EndY, left) <= groupingPolicy.LineCollinearityTolerance;
    }

    private static bool PointsEqual(
        double leftX,
        double leftY,
        double rightX,
        double rightY,
        DimensionGroupingPolicy groupingPolicy)
    {
        return System.Math.Abs(leftX - rightX) <= groupingPolicy.SharedPointTolerance &&
               System.Math.Abs(leftY - rightY) <= groupingPolicy.SharedPointTolerance;
    }

    private static double? TryGetLineOffset(DrawingLineInfo? line, (double X, double Y) direction)
    {
        if (line == null)
            return null;

        var normal = (-direction.Y, direction.X);
        return Project(line.StartX, line.StartY, normal.Item1, normal.Item2);
    }

    private static double DistancePointToInfiniteLine(double pointX, double pointY, DrawingLineInfo line)
    {
        var dx = line.EndX - line.StartX;
        var dy = line.EndY - line.StartY;
        var length = System.Math.Sqrt((dx * dx) + (dy * dy));
        if (length <= 1e-6)
            return double.MaxValue;

        return System.Math.Abs((dy * pointX) - (dx * pointY) + (line.EndX * line.StartY) - (line.EndY * line.StartX)) / length;
    }

    private static string DetermineGroupOrientation(DimensionItem item)
    {
        if (item.ReferenceLine != null)
            return TeklaDrawingDimensionsApi.DetermineDimensionOrientation(0, 0, item.ReferenceLine, []);

        return item.Dimension.Orientation ?? string.Empty;
    }

    private static DimensionType ResolveGroupDimensionType(IReadOnlyList<DimensionItem> items)
    {
        var types = items
            .Select(static item => item.DomainDimensionType)
            .Distinct()
            .ToList();

        return types.Count == 1 ? types[0] : DimensionType.Unknown;
    }

    private static DimensionSourceKind ResolveGroupSourceKind(IReadOnlyList<DimensionItem> items)
    {
        var kinds = items
            .Select(static item => item.SourceKind)
            .Distinct()
            .ToList();

        return kinds.Count == 1 ? kinds[0] : DimensionSourceKind.Unknown;
    }

    private static DimensionGeometryKind ResolveGroupGeometryKind(IReadOnlyList<DimensionItem> items)
    {
        var kinds = items
            .Select(static item => item.GeometryKind)
            .Distinct()
            .ToList();

        return kinds.Count == 1 ? kinds[0] : DimensionGeometryKind.Unknown;
    }

    private static string ResolveGroupTeklaDimensionType(IReadOnlyList<DimensionItem> items)
    {
        var types = items
            .Select(static item => item.TeklaDimensionType)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(System.StringComparer.Ordinal)
            .ToList();

        return types.Count == 1 ? types[0] : string.Empty;
    }

    private static double Project(double x, double y, double axisX, double axisY) => (x * axisX) + (y * axisY);

    internal static DimensionType MapDomainDimensionType(
        DimensionSourceKind sourceKind,
        DimensionGeometryKind geometryKind)
    {
        return geometryKind switch
        {
            DimensionGeometryKind.Horizontal => DimensionType.Horizontal,
            DimensionGeometryKind.Vertical => DimensionType.Vertical,
            DimensionGeometryKind.Free => DimensionType.Free,
            _ => DimensionType.Unknown
        };
    }

    private static DimensionItem CreateFallbackItem(DrawingDimensionInfo dimension)
    {
        var domainType = ResolveDomainDimensionType(dimension);
        var referenceLine = CopyLine(dimension.ReferenceLine);
        var direction = referenceLine != null &&
                        TeklaDrawingDimensionsApi.TryNormalizeDirection(referenceLine.EndX - referenceLine.StartX, referenceLine.EndY - referenceLine.StartY, out var lineDirection)
            ? lineDirection
            : (dimension.DirectionX, dimension.DirectionY);
        var pointList = dimension.MeasuredPoints
            .Select(static point => new DrawingPointInfo
            {
                X = point.X,
                Y = point.Y,
                Order = point.Order
            })
            .ToList();

        if (pointList.Count < 2)
        {
            pointList =
            [
                new DrawingPointInfo { X = referenceLine?.StartX ?? 0, Y = referenceLine?.StartY ?? 0, Order = 0 },
                new DrawingPointInfo { X = referenceLine?.EndX ?? 0, Y = referenceLine?.EndY ?? 0, Order = 1 }
            ];
        }

        var item = new DimensionItem
        {
            DimensionId = dimension.Id,
            ViewId = dimension.ViewId,
            ViewType = dimension.ViewType ?? string.Empty,
            ViewScale = dimension.ViewScale,
            DomainDimensionType = domainType,
            SourceKind = dimension.SourceKind,
            GeometryKind = ResolveGeometryKind(dimension),
            TeklaDimensionType = dimension.DimensionType,
            Distance = dimension.Distance,
            DirectionX = direction.Item1,
            DirectionY = direction.Item2,
            TopDirection = dimension.TopDirection,
            Bounds = dimension.Bounds ?? TeklaDrawingDimensionsApi.CombineBounds(dimension.Segments.Select(static s => s.Bounds)),
            ReferenceLine = referenceLine,
            LeadLineMain = CopyLine(dimension.Segments.FirstOrDefault(static s => s.LeadLineMain != null)?.LeadLineMain),
            LeadLineSecond = CopyLine(dimension.Segments.FirstOrDefault(static s => s.LeadLineSecond != null)?.LeadLineSecond),
            Dimension = dimension
        };

        item.SegmentIds.AddRange(dimension.Segments.Select(static segment => segment.Id));
        item.ReplacePointList(pointList);
        return item;
    }

    private static DimensionItem? CreateItem(DrawingDimensionInfo dimension, DimensionSegmentInfo segment)
    {
        var domainType = ResolveDomainDimensionType(dimension, segment);
        var item = new DimensionItem
        {
            DimensionId = dimension.Id,
            ViewId = dimension.ViewId,
            ViewType = dimension.ViewType ?? string.Empty,
            ViewScale = dimension.ViewScale,
            DomainDimensionType = domainType,
            SourceKind = dimension.SourceKind,
            GeometryKind = ResolveGeometryKind(dimension, segment),
            TeklaDimensionType = dimension.DimensionType,
            Distance = segment.Distance,
            DirectionX = segment.DirectionX != 0 || segment.DirectionY != 0 ? segment.DirectionX : dimension.DirectionX,
            DirectionY = segment.DirectionX != 0 || segment.DirectionY != 0 ? segment.DirectionY : dimension.DirectionY,
            TopDirection = segment.TopDirection != 0 ? segment.TopDirection : dimension.TopDirection,
            Bounds = segment.Bounds ?? (segment.DimensionLine != null ? TeklaDrawingDimensionsApi.CreateBoundsFromLine(segment.DimensionLine) : null),
            ReferenceLine = CopyLine(segment.DimensionLine ?? dimension.ReferenceLine),
            LeadLineMain = CopyLine(segment.LeadLineMain),
            LeadLineSecond = CopyLine(segment.LeadLineSecond),
            Dimension = dimension
        };

        item.SegmentIds.Add(segment.Id);

        var startOrder = FindMeasuredPointOrder(dimension.MeasuredPoints, segment.StartX, segment.StartY);
        var endOrder = FindMeasuredPointOrder(dimension.MeasuredPoints, segment.EndX, segment.EndY);
        var pointList = new List<DrawingPointInfo>
        {
            new() { X = segment.StartX, Y = segment.StartY, Order = startOrder >= 0 ? startOrder : 0 },
            new() { X = segment.EndX, Y = segment.EndY, Order = endOrder >= 0 ? endOrder : 1 }
        };

        item.ReplacePointList(pointList);
        return item;
    }

    private static int FindMeasuredPointOrder(IEnumerable<DrawingPointInfo> points, double x, double y)
    {
        var groupingPolicy = DimensionGroupingPolicy.Default;
        foreach (var point in points)
        {
            if (PointsEqual(point.X, point.Y, x, y, groupingPolicy))
                return point.Order;
        }

        return -1;
    }

    private static DimensionGeometryKind ResolveGeometryKind(DrawingDimensionInfo dimension, DimensionSegmentInfo? segment = null)
    {
        if (segment?.DimensionLine != null)
        {
            return TeklaDrawingDimensionsApi.ResolveDimensionGeometryKind(
                TeklaDrawingDimensionsApi.DetermineDimensionOrientation(
                    0,
                    0,
                    segment.DimensionLine,
                    []));
        }

        if (dimension.GeometryKind != DimensionGeometryKind.Unknown)
            return dimension.GeometryKind;

        return TeklaDrawingDimensionsApi.ResolveDimensionGeometryKind(
            !string.IsNullOrWhiteSpace(dimension.Orientation)
                ? dimension.Orientation
                : TeklaDrawingDimensionsApi.DetermineDimensionOrientation(
                    dimension.DirectionX,
                    dimension.DirectionY,
                    dimension.ReferenceLine,
                    dimension.Segments));
    }

    private static DimensionType ResolveDomainDimensionType(DrawingDimensionInfo dimension, DimensionSegmentInfo? segment = null)
    {
        if (dimension.ClassifiedDimensionType != DimensionType.Unknown)
            return dimension.ClassifiedDimensionType;

        if (System.Enum.TryParse<DimensionType>(dimension.DimensionType, ignoreCase: true, out var explicitDomainType))
            return explicitDomainType;

        return MapDomainDimensionType(dimension.SourceKind, ResolveGeometryKind(dimension, segment));
    }
}
