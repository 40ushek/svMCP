using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class DimensionStackPlanningUnit
{
    public int ClusterId { get; set; }
    public int AnchorDimensionId { get; set; }
    public DrawingLineInfo? AnchorReferenceLine { get; set; }
    public double? AnchorDistance { get; set; }
    public double? DistanceSpread { get; set; }
    public double MinOffset { get; set; }
    public double MaxOffset { get; set; }
    public double NormalizationThreshold { get; set; }
    public bool NormalizationApplied { get; set; }
    public string NormalizationReason { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public List<DimensionStackMoveUnit> Units { get; } = [];
}

internal static class DimensionStackAlignmentNormalizer
{
    private const double AlignDistanceTolerance = 3.0;
    private const double ExtentOverlapTolerance = 3.0;

    public static List<DimensionStackPlanningUnit> BuildPlanningUnits(DimensionGroupLineStack stack)
    {
        var units = DimensionGroupSpacingAnalyzer.BuildMoveUnits(stack);
        if (units.Count == 0)
            return [];

        if (!IsSupported(stack, out var unsupportedReason))
            return BuildStandalonePlanningUnits(units, unsupportedReason);

        var result = new List<DimensionStackPlanningUnit>();
        var visited = new bool[units.Count];
        var clusterId = 1;

        for (var i = 0; i < units.Count; i++)
        {
            if (visited[i])
                continue;

            var component = CollectComponent(units, visited, i, stack.Direction!.Value, stack.TopDirection);
            var planningUnits = BuildPlanningUnitsForComponent(component, clusterId);
            result.AddRange(planningUnits);
            clusterId += planningUnits.Count;
        }

        return result
            .OrderBy(static unit => unit.MinOffset)
            .ThenBy(static unit => unit.AnchorDimensionId)
            .ToList();
    }

    private static List<DimensionStackPlanningUnit> BuildStandalonePlanningUnits(
        IEnumerable<DimensionStackMoveUnit> units,
        string reason)
    {
        var clusterId = 1;
        return units
            .OrderBy(static unit => unit.MinOffset)
            .Select(unit =>
            {
                PopulateStandaloneAlignment(unit, reason);
                return CreateStandalonePlanningUnit(unit, clusterId++, reason);
            })
            .ToList();
    }

    private static List<DimensionStackPlanningUnit> BuildPlanningUnitsForComponent(
        List<DimensionStackMoveUnit> component,
        int startingClusterId)
    {
        if (component.Count == 1)
        {
            PopulateStandaloneAlignment(component[0], string.Empty);
            return [CreateStandalonePlanningUnit(component[0], startingClusterId, string.Empty)];
        }

        var candidates = component
            .Where(static unit => unit.ReferenceLine != null && unit.LeadLineLength.HasValue)
            .OrderBy(static unit => unit.LeadLineLength!.Value)
            .ThenBy(static unit => unit.DimensionId)
            .ToList();

        if (candidates.Count == 0)
            return BuildFallbackPlanningUnits(component, startingClusterId, "No lead-line length available for anchor selection.");

        var anchor = candidates[0];
        if (candidates.Count > 1 && System.Math.Abs(candidates[1].LeadLineLength!.Value - anchor.LeadLineLength!.Value) <= 1e-6)
            return BuildFallbackPlanningUnits(component, startingClusterId, "Anchor selection is ambiguous.");

        var anchorLine = CopyLine(anchor.ReferenceLine);
        if (anchorLine == null)
            return BuildFallbackPlanningUnits(component, startingClusterId, "Anchor reference line is not available.");

        var planningUnit = new DimensionStackPlanningUnit
        {
            ClusterId = startingClusterId,
            AnchorDimensionId = anchor.DimensionId,
            AnchorReferenceLine = anchorLine,
            Status = "aligned"
        };

        foreach (var unit in component.OrderBy(static value => value.MinOffset).ThenBy(static value => value.DimensionId))
        {
            unit.AlignmentClusterId = startingClusterId;
            unit.AlignmentAnchorDimensionId = anchor.DimensionId;
            unit.AlignmentReason = string.Empty;

            if (unit.DimensionId == anchor.DimensionId)
            {
                unit.AlignmentStatus = "anchor";
                unit.PlanningReferenceLine = CopyLine(anchorLine);
            }
            else
            {
                unit.AlignmentStatus = "aligned";
                unit.PlanningReferenceLine = ProjectLineToAnchor(unit.ReferenceLine, anchorLine);
            }

            planningUnit.Units.Add(unit);
        }

        var offsets = planningUnit.Units
            .Select(static unit => TryGetPlanningOffset(unit))
            .Where(static value => value.HasValue)
            .Select(static value => value!.Value)
            .ToList();

        if (offsets.Count == 0)
            return BuildFallbackPlanningUnits(component, startingClusterId, "Aligned planning line could not be resolved.");

        planningUnit.MinOffset = offsets.Min();
        planningUnit.MaxOffset = offsets.Max();

        return [planningUnit];
    }

    private static List<DimensionStackPlanningUnit> BuildFallbackPlanningUnits(
        IEnumerable<DimensionStackMoveUnit> units,
        int startingClusterId,
        string reason)
    {
        var clusterId = startingClusterId;
        return units
            .OrderBy(static unit => unit.MinOffset)
            .ThenBy(static unit => unit.DimensionId)
            .Select(unit =>
            {
                PopulateStandaloneAlignment(unit, reason);
                return CreateStandalonePlanningUnit(unit, clusterId++, reason);
            })
            .ToList();
    }

    private static void PopulateStandaloneAlignment(DimensionStackMoveUnit unit, string reason)
    {
        unit.PlanningReferenceLine = CopyLine(unit.ReferenceLine);
        unit.AlignmentClusterId = 0;
        unit.AlignmentAnchorDimensionId = null;
        unit.AlignmentStatus = string.IsNullOrEmpty(reason) ? "standalone" : "fallback";
        unit.AlignmentReason = reason;
    }

    private static DimensionStackPlanningUnit CreateStandalonePlanningUnit(
        DimensionStackMoveUnit unit,
        int clusterId,
        string reason)
    {
        return new DimensionStackPlanningUnit
        {
            ClusterId = clusterId,
            AnchorDimensionId = unit.DimensionId,
            AnchorReferenceLine = CopyLine(unit.PlanningReferenceLine ?? unit.ReferenceLine),
            MinOffset = unit.MinOffset,
            MaxOffset = unit.MaxOffset,
            Status = string.IsNullOrEmpty(reason) ? "standalone" : "fallback",
            Reason = reason,
            Units = { unit }
        };
    }

    private static List<DimensionStackMoveUnit> CollectComponent(
        IReadOnlyList<DimensionStackMoveUnit> units,
        bool[] visited,
        int startIndex,
        (double X, double Y) direction,
        int topDirection)
    {
        var component = new List<DimensionStackMoveUnit>();
        var queue = new Queue<int>();
        queue.Enqueue(startIndex);
        visited[startIndex] = true;

        while (queue.Count > 0)
        {
            var index = queue.Dequeue();
            var current = units[index];
            component.Add(current);

            for (var candidateIndex = 0; candidateIndex < units.Count; candidateIndex++)
            {
                if (visited[candidateIndex])
                    continue;

                if (!AreAlignNeighbours(current, units[candidateIndex], direction, topDirection))
                    continue;

                visited[candidateIndex] = true;
                queue.Enqueue(candidateIndex);
            }
        }

        return component;
    }

    private static bool IsSupported(DimensionGroupLineStack stack, out string reason)
    {
        if (!stack.Direction.HasValue)
        {
            reason = "Align normalization requires a normalized stack direction.";
            return false;
        }

        var direction = stack.Direction.Value;
        var isHorizontal = System.Math.Abs(direction.Y) <= System.Math.Abs(direction.X) * 0.01;
        var isVertical = System.Math.Abs(direction.X) <= System.Math.Abs(direction.Y) * 0.01;
        if (!isHorizontal && !isVertical)
        {
            reason = "Align normalization is only enabled for axis-aligned parallel stacks.";
            return false;
        }

        if (stack.TopDirection == 0)
        {
            reason = "Align normalization requires a consistent top direction.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static bool AreAlignNeighbours(
        DimensionStackMoveUnit left,
        DimensionStackMoveUnit right,
        (double X, double Y) direction,
        int topDirection)
    {
        if (left.ReferenceLine == null || right.ReferenceLine == null)
            return false;

        var leftOffset = TryGetOffset(left.ReferenceLine, direction, topDirection);
        var rightOffset = TryGetOffset(right.ReferenceLine, direction, topDirection);
        if (!leftOffset.HasValue || !rightOffset.HasValue)
            return false;

        if (System.Math.Abs(leftOffset.Value - rightOffset.Value) > AlignDistanceTolerance)
            return false;

        var leftExtent = TryGetAlongExtent(left.ReferenceLine, direction);
        var rightExtent = TryGetAlongExtent(right.ReferenceLine, direction);
        if (!leftExtent.HasValue || !rightExtent.HasValue)
            return false;

        return leftExtent.Value.Max + ExtentOverlapTolerance >= rightExtent.Value.Min
            && rightExtent.Value.Max + ExtentOverlapTolerance >= leftExtent.Value.Min;
    }

    private static DrawingLineInfo? ProjectLineToAnchor(DrawingLineInfo? source, DrawingLineInfo anchor)
    {
        if (source == null)
            return null;

        if (!TeklaDrawingDimensionsApi.TryNormalizeDirection(
                anchor.EndX - anchor.StartX,
                anchor.EndY - anchor.StartY,
                out var direction))
            return null;

        var start = DimensionProjectionHelper.ProjectPointToReferenceLine(
            source.StartX,
            source.StartY,
            anchor.StartX,
            anchor.StartY,
            direction.X,
            direction.Y);

        var end = DimensionProjectionHelper.ProjectPointToReferenceLine(
            source.EndX,
            source.EndY,
            anchor.StartX,
            anchor.StartY,
            direction.X,
            direction.Y);

        return TeklaDrawingDimensionsApi.CreateLineInfo(start.X, start.Y, end.X, end.Y);
    }

    private static double? TryGetPlanningOffset(DimensionStackMoveUnit unit)
    {
        var line = unit.PlanningReferenceLine ?? unit.ReferenceLine;
        if (line == null || !unit.Direction.HasValue)
            return null;

        return TryGetOffset(line, unit.Direction.Value, unit.TopDirection);
    }

    private static double? TryGetOffset(DrawingLineInfo? line, (double X, double Y) direction, int topDirection)
    {
        if (line == null)
            return null;

        var normalX = -direction.Y;
        var normalY = direction.X;
        var rawOffset = Project(line.StartX, line.StartY, normalX, normalY);
        var sign = topDirection == 0 ? 1 : topDirection;
        return System.Math.Round(rawOffset * sign, 3);
    }

    private static (double Min, double Max)? TryGetAlongExtent(DrawingLineInfo? line, (double X, double Y) direction)
    {
        if (line == null)
            return null;

        var start = Project(line.StartX, line.StartY, direction.X, direction.Y);
        var end = Project(line.EndX, line.EndY, direction.X, direction.Y);
        return (System.Math.Min(start, end), System.Math.Max(start, end));
    }

    private static DrawingLineInfo? CopyLine(DrawingLineInfo? line)
    {
        if (line == null)
            return null;

        return TeklaDrawingDimensionsApi.CreateLineInfo(line.StartX, line.StartY, line.EndX, line.EndY);
    }

    private static double Project(double x, double y, double axisX, double axisY) => (x * axisX) + (y * axisY);
}
