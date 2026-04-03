using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class DimensionGroupLineStack
{
    public int? ViewId { get; set; }
    public string ViewType { get; set; } = string.Empty;
    public string Orientation { get; set; } = string.Empty;
    public int TopDirection { get; set; }
    public (double X, double Y)? Direction { get; set; }
    public List<DimensionGroup> Groups { get; } = [];
}

internal sealed class DimensionStackMoveUnit
{
    public int DimensionId { get; set; }
    public double Distance { get; set; }
    public DrawingLineInfo? ReferenceLine { get; set; }
    public DrawingLineInfo? PlanningReferenceLine { get; set; }
    public double? LeadLineLength { get; set; }
    public (double X, double Y)? Direction { get; set; }
    public int TopDirection { get; set; }
    public int AlignmentClusterId { get; set; }
    public int? AlignmentAnchorDimensionId { get; set; }
    public string AlignmentStatus { get; set; } = string.Empty;
    public string AlignmentReason { get; set; } = string.Empty;
    public double MinOffset { get; set; }
    public double MaxOffset { get; set; }
}

internal sealed class DimensionGroupPairSpacing
{
    public int FirstDimensionId { get; set; }
    public int SecondDimensionId { get; set; }
    public double Distance { get; set; }
    public bool IsOverlap => Distance < 0;
}

internal sealed class DimensionGroupSpacingAnalysis
{
    public int? ViewId { get; set; }
    public string ViewType { get; set; } = string.Empty;
    public string DimensionType { get; set; } = string.Empty;
    public string Orientation { get; set; } = string.Empty;
    public double? DirectionX { get; set; }
    public double? DirectionY { get; set; }
    public int TopDirection { get; set; }
    public DrawingLineInfo? ReferenceLine { get; set; }
    public int GroupCount { get; set; }
    public bool HasOverlaps { get; set; }
    public double? MinimumDistance { get; set; }
    public List<DimensionGroupPairSpacing> Pairs { get; } = [];
}

internal static class DimensionGroupSpacingAnalyzer
{
    public static List<DimensionGroupLineStack> BuildStacks(IEnumerable<DimensionGroup> groups)
    {
        var groupList = groups.ToList();
        var stacks = new List<DimensionGroupLineStack>();
        var visited = new bool[groupList.Count];

        for (var i = 0; i < groupList.Count; i++)
        {
            if (visited[i])
                continue;

            var stack = new DimensionGroupLineStack
            {
                ViewId = groupList[i].ViewId,
                ViewType = groupList[i].ViewType,
                Orientation = groupList[i].Orientation,
                TopDirection = groupList[i].TopDirection,
                Direction = groupList[i].Direction
            };

            var queue = new Queue<int>();
            queue.Enqueue(i);
            visited[i] = true;

            while (queue.Count > 0)
            {
                var index = queue.Dequeue();
                var current = groupList[index];
                stack.Groups.Add(current);

                for (var candidateIndex = 0; candidateIndex < groupList.Count; candidateIndex++)
                {
                    if (visited[candidateIndex])
                        continue;

                    if (!CanStackGroupsTogether(current, groupList[candidateIndex]))
                        continue;

                    visited[candidateIndex] = true;
                    queue.Enqueue(candidateIndex);
                }
            }

            stack.Groups.Sort(static (left, right) => CompareOffsets(left, right));
            stacks.Add(stack);
        }

        return stacks;
    }

    public static List<DimensionGroupSpacingAnalysis> AnalyzeStacks(IEnumerable<DimensionGroup> groups)
    {
        return BuildStacks(groups)
            .Select(AnalyzeStack)
            .ToList();
    }

    public static List<DimensionStackPlanningUnit> BuildPlanningUnits(DimensionGroupLineStack stack)
    {
        return DimensionStackAlignmentNormalizer.BuildPlanningUnits(stack);
    }

    public static DimensionGroupSpacingAnalysis Analyze(DimensionGroup group)
    {
        var analysis = new DimensionGroupSpacingAnalysis
        {
            ViewId = group.ViewId,
            ViewType = group.ViewType,
            Orientation = group.Orientation
        };

        var intervals = GetOrderedIntervals(group);

        for (var i = 0; i < intervals.Count - 1; i++)
        {
            var current = intervals[i];
            var next = intervals[i + 1];
            var distance = System.Math.Round(next.Min - current.Max, 3);

            analysis.Pairs.Add(new DimensionGroupPairSpacing
            {
                FirstDimensionId = current.Member.DimensionId,
                SecondDimensionId = next.Member.DimensionId,
                Distance = distance
            });
        }

        if (analysis.Pairs.Count > 0)
        {
            analysis.MinimumDistance = analysis.Pairs.Min(static pair => pair.Distance);
            analysis.HasOverlaps = analysis.Pairs.Any(static pair => pair.IsOverlap);
        }

        return analysis;
    }

    internal static DimensionGroupSpacingAnalysis AnalyzeStack(DimensionGroupLineStack stack)
    {
        var units = BuildPlanningUnits(stack);
        var analysis = new DimensionGroupSpacingAnalysis
        {
            ViewId = stack.ViewId,
            ViewType = stack.ViewType,
            DimensionType = ResolveStackDimensionType(stack),
            Orientation = stack.Orientation,
            DirectionX = stack.Direction?.X,
            DirectionY = stack.Direction?.Y,
            TopDirection = stack.TopDirection,
            ReferenceLine = CopyLine(stack.Groups.FirstOrDefault(static group => group.ReferenceLine != null)?.ReferenceLine),
            GroupCount = stack.Groups.Count
        };

        for (var i = 0; i < units.Count - 1; i++)
        {
            var current = units[i];
            var next = units[i + 1];
            var distance = System.Math.Round(next.MinOffset - current.MaxOffset, 3);

            analysis.Pairs.Add(new DimensionGroupPairSpacing
            {
                FirstDimensionId = current.AnchorDimensionId,
                SecondDimensionId = next.AnchorDimensionId,
                Distance = distance
            });
        }

        if (analysis.Pairs.Count > 0)
        {
            analysis.MinimumDistance = analysis.Pairs.Min(static pair => pair.Distance);
            analysis.HasOverlaps = analysis.Pairs.Any(static pair => pair.IsOverlap);
        }

        return analysis;
    }

    public static List<DimensionStackMoveUnit> BuildMoveUnits(DimensionGroupLineStack stack)
    {
        return stack.Groups
            .SelectMany(static group => group.Members)
            .GroupBy(static member => member.DimensionId)
            .Select(group =>
            {
                var members = group.ToList();
                var offsets = members
                    .Select(member => TryGetMemberOffset(member, stack.Direction, stack.TopDirection))
                    .Where(static value => value.HasValue)
                    .Select(static value => value!.Value)
                    .ToList();

                if (offsets.Count == 0)
                    return null;

                var representative = members.FirstOrDefault(static member => member.ReferenceLine != null) ?? members[0];
                var leadLineLength = members
                    .Select(TryGetShortestLeadLineLength)
                    .Where(static value => value.HasValue)
                    .Select(static value => value!.Value)
                    .DefaultIfEmpty()
                    .Min();

                return new DimensionStackMoveUnit
                {
                    DimensionId = group.Key,
                    Distance = representative.Distance,
                    ReferenceLine = CopyLine(representative.ReferenceLine),
                    PlanningReferenceLine = CopyLine(representative.ReferenceLine),
                    LeadLineLength = leadLineLength > 1e-9 ? leadLineLength : null,
                    Direction = stack.Direction,
                    TopDirection = stack.TopDirection,
                    AlignmentStatus = "standalone",
                    MinOffset = offsets.Min(),
                    MaxOffset = offsets.Max()
                };
            })
            .Where(static unit => unit != null)
            .Cast<DimensionStackMoveUnit>()
            .OrderBy(static unit => unit.MinOffset)
            .ToList();
    }

    internal static List<(DimensionItem Member, double Min, double Max)> GetOrderedIntervals(DimensionGroup group)
    {
        return group.Members
            .Select(member => (Member: member, Interval: TryGetInterval(member, group)))
            .Where(static item => item.Interval != null)
            .Select(static item => (item.Member, item.Interval!.Value.Min, item.Interval.Value.Max))
            .OrderBy(static item => item.Min)
            .ToList();
    }

    private static bool CanStackGroupsTogether(DimensionGroup left, DimensionGroup right)
    {
        if (left.ViewId != right.ViewId)
            return false;

        if (!string.Equals(left.ViewType, right.ViewType, System.StringComparison.Ordinal))
            return false;

        if (left.TopDirection != 0 && right.TopDirection != 0 && left.TopDirection != right.TopDirection)
            return false;

        if (!left.Direction.HasValue || !right.Direction.HasValue)
            return false;

        if (!AreParallel(left.Direction.Value, right.Direction.Value))
            return false;

        return HaveCompatibleDirectionExtents(left, right, left.Direction.Value);
    }

    private static AxisInterval? TryGetInterval(DimensionItem member, DimensionGroup group)
    {
        var referenceInterval = TryGetReferenceLineInterval(member, group.Direction);
        if (referenceInterval != null)
            return referenceInterval;

        var bounds = member.Bounds;
        if (bounds == null)
            return null;

        return TryGetProjectedInterval(bounds, group.Direction);
    }

    private static bool HaveCompatibleDirectionExtents(DimensionGroup left, DimensionGroup right, (double X, double Y) direction)
    {
        var leftExtent = TryGetGroupExtent(left, direction);
        var rightExtent = TryGetGroupExtent(right, direction);
        if (!leftExtent.HasValue || !rightExtent.HasValue)
            return false;

        const double overlapTolerance = 3.0;
        return leftExtent.Value.Max + overlapTolerance >= rightExtent.Value.Min
            && rightExtent.Value.Max + overlapTolerance >= leftExtent.Value.Min;
    }

    private static (double Min, double Max)? TryGetGroupExtent(DimensionGroup group, (double X, double Y) direction)
    {
        if (group.ReferenceLine != null)
        {
            var projections = new[]
            {
                Project(group.ReferenceLine.StartX, group.ReferenceLine.StartY, direction.X, direction.Y),
                Project(group.ReferenceLine.EndX, group.ReferenceLine.EndY, direction.X, direction.Y)
            };

            return (projections.Min(), projections.Max());
        }

        var bounds = group.Bounds;
        if (bounds == null)
            return null;

        var values = new[]
        {
            Project(bounds.MinX, bounds.MinY, direction.X, direction.Y),
            Project(bounds.MinX, bounds.MaxY, direction.X, direction.Y),
            Project(bounds.MaxX, bounds.MinY, direction.X, direction.Y),
            Project(bounds.MaxX, bounds.MaxY, direction.X, direction.Y)
        };

        return (values.Min(), values.Max());
    }

    private static double? TryGetGroupOffset(DimensionGroup group)
    {
        if (!group.Direction.HasValue || group.ReferenceLine == null)
            return null;

        var normalX = -group.Direction.Value.Y;
        var normalY = group.Direction.Value.X;
        var rawOffset = Project(group.ReferenceLine.StartX, group.ReferenceLine.StartY, normalX, normalY);
        return System.Math.Round(rawOffset * GetOffsetSign(group.TopDirection), 3);
    }

    private static double? TryGetMemberOffset(DimensionItem member, (double X, double Y)? direction, int stackTopDirection)
    {
        if (!direction.HasValue || member.ReferenceLine == null)
            return null;

        var normalX = -direction.Value.Y;
        var normalY = direction.Value.X;
        var rawOffset = Project(member.ReferenceLine.StartX, member.ReferenceLine.StartY, normalX, normalY);
        return System.Math.Round(rawOffset * GetOffsetSign(stackTopDirection), 3);
    }

    private static double? TryGetShortestLeadLineLength(DimensionItem member)
    {
        var lengths = new[]
            {
                member.GetLeadLineMainLength(),
                member.GetLeadLineSecondLength()
            }
            .Where(static value => value > 1e-9)
            .ToList();

        if (lengths.Count == 0)
            return null;

        return lengths.Min();
    }

    private static int GetRepresentativeDimensionId(DimensionGroup group)
    {
        return group.Members.Count == 0 ? 0 : group.Members[0].DimensionId;
    }

    private static string ResolveStackDimensionType(DimensionGroupLineStack stack)
    {
        var types = stack.Groups
            .Select(static group => group.DimensionType)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(System.StringComparer.Ordinal)
            .ToList();

        return types.Count == 1 ? types[0] : string.Empty;
    }

    private static int CompareOffsets(DimensionGroup left, DimensionGroup right)
    {
        var leftOffset = TryGetGroupOffset(left);
        var rightOffset = TryGetGroupOffset(right);

        if (leftOffset.HasValue && rightOffset.HasValue)
            return leftOffset.Value.CompareTo(rightOffset.Value);

        return 0;
    }

    private static int GetOffsetSign(int topDirection) => topDirection == 0 ? 1 : topDirection;

    private static bool AreParallel((double X, double Y) left, (double X, double Y) right)
    {
        var dot = System.Math.Abs((left.X * right.X) + (left.Y * right.Y));
        return dot >= 0.995;
    }

    private static DrawingLineInfo? CopyLine(DrawingLineInfo? line)
    {
        if (line == null)
            return null;

        return TeklaDrawingDimensionsApi.CreateLineInfo(line.StartX, line.StartY, line.EndX, line.EndY);
    }

    private static AxisInterval? TryGetReferenceLineInterval(DimensionItem member, (double X, double Y)? groupDirection)
    {
        var line = member.ReferenceLine;
        if (line == null)
            return null;

        var direction = groupDirection;
        if (!direction.HasValue)
        {
            var dx = line.EndX - line.StartX;
            var dy = line.EndY - line.StartY;
            if (!TeklaDrawingDimensionsApi.TryNormalizeDirection(dx, dy, out var lineDirection))
                return null;

            direction = TeklaDrawingDimensionsApi.CanonicalizeDirection(lineDirection.X, lineDirection.Y);
        }

        var normal = (-direction.Value.Y, direction.Value.X);
        var start = Project(line.StartX, line.StartY, normal.Item1, normal.Item2);
        var end = Project(line.EndX, line.EndY, normal.Item1, normal.Item2);
        return new AxisInterval(System.Math.Min(start, end), System.Math.Max(start, end));
    }

    private static AxisInterval? TryGetProjectedInterval(DrawingBoundsInfo bounds, (double X, double Y)? direction)
    {
        if (!direction.HasValue)
            return new AxisInterval(bounds.MinX + bounds.MinY, bounds.MaxX + bounds.MaxY);

        var normalX = -direction.Value.Y;
        var normalY = direction.Value.X;
        var normalLength = System.Math.Sqrt((normalX * normalX) + (normalY * normalY));
        if (normalLength <= 1e-6)
            return null;

        normalX /= normalLength;
        normalY /= normalLength;

        var projections = new[]
        {
            Project(bounds.MinX, bounds.MinY, normalX, normalY),
            Project(bounds.MinX, bounds.MaxY, normalX, normalY),
            Project(bounds.MaxX, bounds.MinY, normalX, normalY),
            Project(bounds.MaxX, bounds.MaxY, normalX, normalY)
        };

        return new AxisInterval(projections.Min(), projections.Max());
    }

    private static double Project(double x, double y, double axisX, double axisY) => (x * axisX) + (y * axisY);

    private readonly struct AxisInterval
    {
        public AxisInterval(double min, double max)
        {
            Min = min;
            Max = max;
        }

        public double Min { get; }
        public double Max { get; }
    }
}
