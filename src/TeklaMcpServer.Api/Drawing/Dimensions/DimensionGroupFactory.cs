using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

internal static class DimensionGroupFactory
{
    private const double ParallelDotTolerance = 0.995;
    private const double ExtentOverlapTolerance = 3.0;

    public static List<DimensionGroup> BuildGroups(GetDimensionsResult result) => BuildGroups(result.Dimensions);

    public static List<DimensionGroup> BuildGroups(IEnumerable<DrawingDimensionInfo> dimensions)
    {
        var groups = new List<DimensionGroup>();

        foreach (var dimension in dimensions)
        {
            var group = groups.FirstOrDefault(existing => CanAddToGroup(existing, dimension));
            if (group == null)
            {
                group = CreateGroup(dimension);
                groups.Add(group);
                continue;
            }

            AddDimension(group, dimension);
        }

        foreach (var group in groups)
        {
            group.SortMembers();
            group.RefreshMetrics();
        }

        return groups;
    }

    private static DimensionGroup CreateGroup(DrawingDimensionInfo dimension)
    {
        var group = new DimensionGroup
        {
            ViewId = dimension.ViewId,
            ViewType = dimension.ViewType ?? string.Empty,
            DimensionType = dimension.DimensionType ?? string.Empty,
            Orientation = DetermineGroupOrientation(dimension),
            TopDirection = dimension.TopDirection,
            Direction = TryGetDimensionDirection(dimension),
            ReferenceLine = dimension.ReferenceLine == null
                ? null
                : TeklaDrawingDimensionsApi.CreateLineInfo(
                    dimension.ReferenceLine.StartX,
                    dimension.ReferenceLine.StartY,
                    dimension.ReferenceLine.EndX,
                    dimension.ReferenceLine.EndY)
        };

        AddDimension(group, dimension);
        return group;
    }

    private static void AddDimension(DimensionGroup group, DrawingDimensionInfo dimension)
    {
        var member = new DimensionGroupMember
        {
            DimensionId = dimension.Id,
            Distance = dimension.Distance,
            DirectionX = dimension.DirectionX,
            DirectionY = dimension.DirectionY,
            TopDirection = dimension.TopDirection,
            Bounds = dimension.Bounds ?? TeklaDrawingDimensionsApi.CombineBounds(dimension.Segments.Select(static s => s.Bounds)),
            ReferenceLine = CopyLine(dimension.ReferenceLine),
            LeadLineMain = CopyPrimaryLeadLine(dimension),
            LeadLineSecond = CopySecondaryLeadLine(dimension),
            Dimension = dimension
        };

        member.SortKey = DetermineSortKey(member, group.Direction);
        group.Members.Add(member);

        if (group.Direction == null)
            group.Direction = TryGetDimensionDirection(dimension);

        if (group.ReferenceLine == null && member.ReferenceLine != null)
            group.ReferenceLine = CopyLine(member.ReferenceLine);
    }

    private static bool CanAddToGroup(DimensionGroup group, DrawingDimensionInfo dimension)
    {
        if (group.ViewId != dimension.ViewId)
            return false;

        if (!string.Equals(group.ViewType, dimension.ViewType ?? string.Empty, System.StringComparison.Ordinal))
            return false;

        if (!string.Equals(group.DimensionType, dimension.DimensionType ?? string.Empty, System.StringComparison.Ordinal))
            return false;

        if (group.TopDirection != 0 && dimension.TopDirection != 0 && group.TopDirection != dimension.TopDirection)
            return false;

        var groupDirection = group.Direction;
        var dimensionDirection = TryGetDimensionDirection(dimension);
        if (groupDirection.HasValue && dimensionDirection.HasValue && !AreParallel(groupDirection.Value, dimensionDirection.Value))
            return false;

        return HaveCompatibleExtents(group, dimension, groupDirection ?? dimensionDirection);
    }

    private static bool HaveCompatibleExtents(
        DimensionGroup group,
        DrawingDimensionInfo dimension,
        (double X, double Y)? direction)
    {
        if (!direction.HasValue)
            return true;

        var groupExtent = TryGetExtent(group.Members.Select(static m => m.ReferenceLine), direction.Value)
            ?? TryGetExtent(group.Members.Select(static m => m.Bounds), direction.Value);
        var dimensionExtent = TryGetExtent(
            dimension.ReferenceLine == null ? [] : new[] { dimension.ReferenceLine },
            direction.Value)
            ?? TryGetExtent(
                dimension.Bounds == null ? [] : new[] { dimension.Bounds },
                direction.Value);

        if (groupExtent == null || dimensionExtent == null)
            return true;

        return groupExtent.Value.Max + ExtentOverlapTolerance >= dimensionExtent.Value.Min
            && dimensionExtent.Value.Max + ExtentOverlapTolerance >= groupExtent.Value.Min;
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

    private static DrawingLineInfo? CopyPrimaryLeadLine(DrawingDimensionInfo dimension)
    {
        return CopyLine(dimension.Segments.FirstOrDefault(static s => s.LeadLineMain != null)?.LeadLineMain);
    }

    private static DrawingLineInfo? CopySecondaryLeadLine(DrawingDimensionInfo dimension)
    {
        return CopyLine(dimension.Segments.FirstOrDefault(static s => s.LeadLineSecond != null)?.LeadLineSecond);
    }

    private static DrawingLineInfo? CopyLine(DrawingLineInfo? line)
    {
        if (line == null)
            return null;

        return TeklaDrawingDimensionsApi.CreateLineInfo(line.StartX, line.StartY, line.EndX, line.EndY);
    }

    private static (double X, double Y)? TryGetDimensionDirection(DrawingDimensionInfo dimension)
    {
        if (!TeklaDrawingDimensionsApi.TryNormalizeDirection(dimension.DirectionX, dimension.DirectionY, out var direction))
            return null;

        return TeklaDrawingDimensionsApi.CanonicalizeDirection(direction.X, direction.Y);
    }

    private static bool AreParallel((double X, double Y) left, (double X, double Y) right)
    {
        var dot = System.Math.Abs((left.X * right.X) + (left.Y * right.Y));
        return dot >= ParallelDotTolerance;
    }

    private static string DetermineGroupOrientation(DrawingDimensionInfo dimension)
    {
        if (TeklaDrawingDimensionsApi.TryNormalizeDirection(dimension.DirectionX, dimension.DirectionY, out var direction))
        {
            if (System.Math.Abs(direction.Y) <= System.Math.Abs(direction.X) * 0.01)
                return "horizontal";

            if (System.Math.Abs(direction.X) <= System.Math.Abs(direction.Y) * 0.01)
                return "vertical";
        }

        return string.IsNullOrWhiteSpace(dimension.Orientation) ? string.Empty : dimension.Orientation;
    }

    private static double Project(double x, double y, double axisX, double axisY) => (x * axisX) + (y * axisY);
}
