using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

internal static class DimensionGroupFactory
{
    private const double ParallelDotTolerance = 0.995;
    private const double ExtentOverlapTolerance = 3.0;
    private const double LineCollinearityTolerance = 3.0;
    private const double LineBandTolerance = 3.0;
    private const double ChainBandTolerance = 250.0;
    private const double ChainExtentGapTolerance = 80.0;
    private const double SharedPointTolerance = 0.5;

    public static List<DimensionGroup> BuildGroups(GetDimensionsResult result) => BuildGroups(result.Dimensions);

    public static List<DimensionGroup> BuildGroups(IEnumerable<DrawingDimensionInfo> dimensions)
    {
        var members = dimensions
            .SelectMany(CreateMembers)
            .ToList();

        var groups = BuildConnectedGroups(members);

        foreach (var group in groups)
        {
            group.SortMembers();
            group.RefreshMetrics();
        }

        return groups;
    }

    private static List<DimensionGroup> BuildConnectedGroups(IReadOnlyList<DimensionGroupMember> members)
    {
        var groups = new List<DimensionGroup>();
        var visited = new bool[members.Count];

        for (var i = 0; i < members.Count; i++)
        {
            if (visited[i])
                continue;

            var component = new List<DimensionGroupMember>();
            var queue = new Queue<int>();
            queue.Enqueue(i);
            visited[i] = true;

            while (queue.Count > 0)
            {
                var index = queue.Dequeue();
                var current = members[index];
                component.Add(current);

                for (var candidateIndex = 0; candidateIndex < members.Count; candidateIndex++)
                {
                    if (visited[candidateIndex])
                        continue;

                    if (!CanGroupMembersTogether(current, members[candidateIndex]))
                        continue;

                    visited[candidateIndex] = true;
                    queue.Enqueue(candidateIndex);
                }
            }

            groups.Add(CreateGroup(component));
        }

        return groups;
    }

    private static IEnumerable<DimensionGroupMember> CreateMembers(DrawingDimensionInfo dimension)
    {
        var segmentMembers = dimension.Segments
            .Select(segment => CreateMember(dimension, segment))
            .Where(static member => member != null)
            .Cast<DimensionGroupMember>()
            .ToList();

        if (segmentMembers.Count > 0)
            return segmentMembers;

        return new[] { CreateFallbackMember(dimension) };
    }

    private static DimensionGroup CreateGroup(IReadOnlyList<DimensionGroupMember> members)
    {
        var member = members[0];
        var dimension = member.Dimension;
        var group = new DimensionGroup
        {
            ViewId = dimension.ViewId,
            ViewType = dimension.ViewType ?? string.Empty,
            DimensionType = ResolveGroupDimensionType(members),
            Orientation = DetermineGroupOrientation(member),
            TopDirection = member.TopDirection,
            Direction = TryGetMemberDirection(member),
            ReferenceLine = CopyLine(member.ReferenceLine)
        };

        foreach (var groupMember in members)
            AddMember(group, groupMember);

        return group;
    }

    private static void AddMember(DimensionGroup group, DimensionGroupMember member)
    {
        member.SortKey = DetermineSortKey(member, group.Direction);
        group.Members.Add(member);

        if (group.Direction == null)
            group.Direction = TryGetMemberDirection(member);

        if (group.ReferenceLine == null && member.ReferenceLine != null)
            group.ReferenceLine = CopyLine(member.ReferenceLine);
    }

    private static bool CanGroupMembersTogether(DimensionGroupMember left, DimensionGroupMember right)
    {
        if (left.Dimension.ViewId != right.Dimension.ViewId)
            return false;

        if (!string.Equals(left.Dimension.ViewType ?? string.Empty, right.Dimension.ViewType ?? string.Empty, System.StringComparison.Ordinal))
            return false;

        if (left.TopDirection != 0 && right.TopDirection != 0 && left.TopDirection != right.TopDirection)
            return false;

        var leftDirection = TryGetMemberDirection(left);
        var rightDirection = TryGetMemberDirection(right);
        if (leftDirection.HasValue && rightDirection.HasValue && !AreParallel(leftDirection.Value, rightDirection.Value))
            return false;

        var sharedMeasuredPoint = HaveSharedMeasuredPoint(left, right);
        if (sharedMeasuredPoint && HaveCompatibleChainTransition(left, right, leftDirection ?? rightDirection))
            return true;

        if (HaveCompatibleSameLineChain(left, right, leftDirection ?? rightDirection))
            return true;

        if (!HaveCompatibleLineBand(left, right, leftDirection ?? rightDirection))
            return false;

        if (sharedMeasuredPoint)
            return true;

        if (!HaveCompatibleLeadLines(left, right))
            return false;

        return HaveCompatibleExtents(left, right, leftDirection ?? rightDirection);
    }

    private static bool HaveCompatibleChainTransition(
        DimensionGroupMember left,
        DimensionGroupMember right,
        (double X, double Y)? direction)
    {
        if (left.DimensionId != right.DimensionId)
            return false;

        if (!direction.HasValue)
            return false;

        var leftOffset = TryGetLineOffset(left.ReferenceLine, direction.Value);
        var rightOffset = TryGetLineOffset(right.ReferenceLine, direction.Value);
        if (!leftOffset.HasValue || !rightOffset.HasValue)
            return true;

        return System.Math.Abs(leftOffset.Value - rightOffset.Value) <= ChainBandTolerance;
    }

    private static bool HaveCompatibleSameLineChain(
        DimensionGroupMember left,
        DimensionGroupMember right,
        (double X, double Y)? direction)
    {
        if (left.DimensionId != right.DimensionId)
            return false;

        if (!direction.HasValue)
            return false;

        if (!HaveCompatibleLineBand(left, right, direction))
            return false;

        var leftExtent = TryGetExtent(
            left.ReferenceLine == null ? [] : new[] { left.ReferenceLine },
            direction.Value);
        var rightExtent = TryGetExtent(
            right.ReferenceLine == null ? [] : new[] { right.ReferenceLine },
            direction.Value);
        if (!leftExtent.HasValue || !rightExtent.HasValue)
            return false;

        return leftExtent.Value.Max + ChainExtentGapTolerance >= rightExtent.Value.Min
            && rightExtent.Value.Max + ChainExtentGapTolerance >= leftExtent.Value.Min;
    }

    private static bool HaveCompatibleLineBand(
        DimensionGroupMember left,
        DimensionGroupMember right,
        (double X, double Y)? direction)
    {
        if (!direction.HasValue)
            return true;

        var leftOffset = TryGetLineOffset(left.ReferenceLine, direction.Value);
        var rightOffset = TryGetLineOffset(right.ReferenceLine, direction.Value);
        if (!leftOffset.HasValue || !rightOffset.HasValue)
            return true;

        return System.Math.Abs(leftOffset.Value - rightOffset.Value) <= LineBandTolerance;
    }

    private static bool HaveSharedMeasuredPoint(DimensionGroupMember left, DimensionGroupMember right)
    {
        return PointsEqual(left.StartX, left.StartY, right.StartX, right.StartY) ||
               PointsEqual(left.StartX, left.StartY, right.EndX, right.EndY) ||
               PointsEqual(left.EndX, left.EndY, right.StartX, right.StartY) ||
               PointsEqual(left.EndX, left.EndY, right.EndX, right.EndY);
    }

    private static bool HaveCompatibleLeadLines(DimensionGroupMember left, DimensionGroupMember right)
    {
        var leftLeadLines = new[] { CopyLine(left.LeadLineMain), CopyLine(left.LeadLineSecond) }
            .Where(static line => line != null)
            .Cast<DrawingLineInfo>()
            .ToList();
        var rightLeadLines = new[] { CopyLine(right.LeadLineMain), CopyLine(right.LeadLineSecond) }
            .Where(static line => line != null)
            .Cast<DrawingLineInfo>()
            .ToList();

        if (leftLeadLines.Count == 0 || rightLeadLines.Count == 0)
            return true;

        return leftLeadLines.Any(leftLine => rightLeadLines.Any(rightLine => AreCollinear(leftLine, rightLine)));
    }

    private static bool HaveCompatibleExtents(
        DimensionGroupMember left,
        DimensionGroupMember right,
        (double X, double Y)? direction)
    {
        if (!direction.HasValue)
            return true;

        var leftExtent = TryGetExtent(
            left.ReferenceLine == null ? [] : new[] { left.ReferenceLine },
            direction.Value)
            ?? TryGetExtent(
                left.Bounds == null ? [] : new[] { left.Bounds },
                direction.Value);
        var rightExtent = TryGetExtent(
            right.ReferenceLine == null ? [] : new[] { right.ReferenceLine },
            direction.Value)
            ?? TryGetExtent(
                right.Bounds == null ? [] : new[] { right.Bounds },
                direction.Value);

        if (leftExtent == null || rightExtent == null)
            return true;

        return leftExtent.Value.Max + ExtentOverlapTolerance >= rightExtent.Value.Min
            && rightExtent.Value.Max + ExtentOverlapTolerance >= leftExtent.Value.Min;
    }

    private static (double Min, double Max)? TryGetExtent(IEnumerable<DrawingLineInfo?> lines, (double X, double Y) direction)
    {
        var values = lines
            .Where(static line => line != null)
            .SelectMany(static line => new[] { (line!.StartX, line.StartY), (line.EndX, line.EndY) })
            .Select(point => Project(point.Item1, point.Item2, direction.X, direction.Y))
            .ToList();

        if (values.Count == 0)
            return null;

        return (values.Min(), values.Max());
    }

    private static (double Min, double Max)? TryGetExtent(IEnumerable<DrawingBoundsInfo?> bounds, (double X, double Y) direction)
    {
        var values = bounds
            .Where(static bounds => bounds != null)
            .SelectMany(static bounds => new[]
            {
                (bounds!.MinX, bounds.MinY),
                (bounds.MinX, bounds.MaxY),
                (bounds.MaxX, bounds.MinY),
                (bounds.MaxX, bounds.MaxY)
            })
            .Select(point => Project(point.Item1, point.Item2, direction.X, direction.Y))
            .ToList();

        if (values.Count == 0)
            return null;

        return (values.Min(), values.Max());
    }

    private static double DetermineSortKey(DimensionGroupMember member, (double X, double Y)? direction)
    {
        if (member.ReferenceLine != null && direction.HasValue)
        {
            var normal = (-direction.Value.Y, direction.Value.X);
            return System.Math.Round(Project(member.ReferenceLine.StartX, member.ReferenceLine.StartY, normal.Item1, normal.Item2), 3);
        }

        var bounds = member.Bounds;
        if (bounds == null)
            return double.MaxValue;

        return System.Math.Round(bounds.MinX + bounds.MinY, 3);
    }

    private static DrawingLineInfo? CopyLine(DrawingLineInfo? line)
    {
        if (line == null)
            return null;

        return TeklaDrawingDimensionsApi.CreateLineInfo(line.StartX, line.StartY, line.EndX, line.EndY);
    }

    private static (double X, double Y)? TryGetMemberDirection(DimensionGroupMember member)
    {
        if (!TeklaDrawingDimensionsApi.TryNormalizeDirection(member.DirectionX, member.DirectionY, out var direction))
            return null;

        return TeklaDrawingDimensionsApi.CanonicalizeDirection(direction.X, direction.Y);
    }

    private static bool AreParallel((double X, double Y) left, (double X, double Y) right)
    {
        var dot = System.Math.Abs((left.X * right.X) + (left.Y * right.Y));
        return dot >= ParallelDotTolerance;
    }

    private static bool AreCollinear(DrawingLineInfo left, DrawingLineInfo right)
    {
        if (!TeklaDrawingDimensionsApi.TryNormalizeDirection(left.EndX - left.StartX, left.EndY - left.StartY, out var leftDirection) ||
            !TeklaDrawingDimensionsApi.TryNormalizeDirection(right.EndX - right.StartX, right.EndY - right.StartY, out var rightDirection))
        {
            return false;
        }

        if (!AreParallel(leftDirection, rightDirection))
            return false;

        return DistancePointToInfiniteLine(right.StartX, right.StartY, left) <= LineCollinearityTolerance
            && DistancePointToInfiniteLine(right.EndX, right.EndY, left) <= LineCollinearityTolerance;
    }

    private static bool PointsEqual(double leftX, double leftY, double rightX, double rightY)
    {
        return System.Math.Abs(leftX - rightX) <= SharedPointTolerance
            && System.Math.Abs(leftY - rightY) <= SharedPointTolerance;
    }

    private static double? TryGetLineOffset(DrawingLineInfo? line, (double X, double Y) direction)
    {
        if (line == null)
            return null;

        var normalX = -direction.Y;
        var normalY = direction.X;
        return System.Math.Round(Project(line.StartX, line.StartY, normalX, normalY), 3);
    }

    private static double DistancePointToInfiniteLine(double pointX, double pointY, DrawingLineInfo line)
    {
        var dx = line.EndX - line.StartX;
        var dy = line.EndY - line.StartY;
        var length = System.Math.Sqrt((dx * dx) + (dy * dy));
        if (length <= 1e-6)
            return double.MaxValue;

        var cross = System.Math.Abs(((pointX - line.StartX) * dy) - ((pointY - line.StartY) * dx));
        return cross / length;
    }

    private static string DetermineGroupOrientation(DimensionGroupMember member)
    {
        if (TeklaDrawingDimensionsApi.TryNormalizeDirection(member.DirectionX, member.DirectionY, out var direction))
        {
            if (System.Math.Abs(direction.Y) <= System.Math.Abs(direction.X) * 0.01)
                return "horizontal";

            if (System.Math.Abs(direction.X) <= System.Math.Abs(direction.Y) * 0.01)
                return "vertical";
        }

        return string.IsNullOrWhiteSpace(member.Dimension.Orientation) ? string.Empty : member.Dimension.Orientation;
    }

    private static string ResolveGroupDimensionType(IReadOnlyList<DimensionGroupMember> members)
    {
        var types = members
            .Select(static member => member.Dimension.DimensionType)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(System.StringComparer.Ordinal)
            .ToList();

        return types.Count == 1 ? types[0] : string.Empty;
    }

    private static double Project(double x, double y, double axisX, double axisY) => (x * axisX) + (y * axisY);

    private static DimensionGroupMember CreateFallbackMember(DrawingDimensionInfo dimension)
    {
        return new DimensionGroupMember
        {
            DimensionId = dimension.Id,
            SegmentId = 0,
            StartX = 0,
            StartY = 0,
            EndX = 0,
            EndY = 0,
            Distance = dimension.Distance,
            DirectionX = dimension.DirectionX,
            DirectionY = dimension.DirectionY,
            TopDirection = dimension.TopDirection,
            Bounds = dimension.Bounds ?? TeklaDrawingDimensionsApi.CombineBounds(dimension.Segments.Select(static s => s.Bounds)),
            ReferenceLine = CopyLine(dimension.ReferenceLine),
            LeadLineMain = null,
            LeadLineSecond = null,
            Dimension = dimension
        };
    }

    private static DimensionGroupMember? CreateMember(DrawingDimensionInfo dimension, DimensionSegmentInfo segment)
    {
        if (segment.DimensionLine == null)
            return null;

        return new DimensionGroupMember
        {
            DimensionId = dimension.Id,
            SegmentId = segment.Id,
            StartX = segment.StartX,
            StartY = segment.StartY,
            EndX = segment.EndX,
            EndY = segment.EndY,
            Distance = segment.Distance,
            DirectionX = segment.DirectionX,
            DirectionY = segment.DirectionY,
            TopDirection = segment.TopDirection,
            Bounds = segment.Bounds ?? (segment.DimensionLine != null ? TeklaDrawingDimensionsApi.CreateBoundsFromLine(segment.DimensionLine) : null),
            ReferenceLine = CopyLine(segment.DimensionLine),
            LeadLineMain = CopyLine(segment.LeadLineMain),
            LeadLineSecond = CopyLine(segment.LeadLineSecond),
            Dimension = dimension
        };
    }
}
