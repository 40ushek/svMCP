using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class DimensionMoveProposal
{
    public int DimensionId { get; set; }
    public double AxisShift { get; set; }
}

internal sealed class DimensionGroupArrangementPlan
{
    public int? ViewId { get; set; }
    public string ViewType { get; set; } = string.Empty;
    public string Orientation { get; set; } = string.Empty;
    public double TargetGapPaper { get; set; }
    public double TargetGapDrawing { get; set; }
    public List<DimensionMoveProposal> Proposals { get; } = [];
    public bool HasChanges => Proposals.Count > 0;
}

internal static class DimensionGroupArrangementPlanner
{
    public static DimensionGroupArrangementPlan BuildPlan(DimensionGroupLineStack stack, double targetGap)
    {
        var targetGapDrawing = ResolveTargetGapDrawing(stack, targetGap);
        var plan = new DimensionGroupArrangementPlan
        {
            ViewId = stack.ViewId,
            ViewType = stack.ViewType,
            Orientation = stack.Orientation,
            TargetGapPaper = targetGap,
            TargetGapDrawing = targetGapDrawing
        };

        var units = DimensionGroupSpacingAnalyzer.BuildPlanningUnits(stack);
        if (units.Count < 2)
            return plan;

        var previousMax = units[0].MaxOffset;
        var cumulativeShift = 0.0;

        for (var i = 1; i < units.Count; i++)
        {
            var current = units[i];
            var shiftedMin = current.MinOffset + cumulativeShift;
            var shiftedMax = current.MaxOffset + cumulativeShift;
            var requiredMin = previousMax + targetGapDrawing;

            if (shiftedMin < requiredMin)
            {
                var extraShift = System.Math.Round(requiredMin - shiftedMin, 3);
                cumulativeShift += extraShift;
                shiftedMin += extraShift;
                shiftedMax += extraShift;
            }

            if (cumulativeShift > 1e-9)
            {
                foreach (var unit in current.Units)
                {
                    plan.Proposals.Add(new DimensionMoveProposal
                    {
                        DimensionId = unit.DimensionId,
                        AxisShift = cumulativeShift
                    });
                }
            }

            previousMax = shiftedMax;
        }

        return plan;
    }

    public static DimensionGroupArrangementPlan BuildPlan(DimensionGroup group, double targetGap)
    {
        var targetGapDrawing = ResolveTargetGapDrawing(group, targetGap);
        var plan = new DimensionGroupArrangementPlan
        {
            ViewId = group.ViewId,
            ViewType = group.ViewType,
            Orientation = group.Orientation,
            TargetGapPaper = targetGap,
            TargetGapDrawing = targetGapDrawing
        };

        var intervals = DimensionGroupSpacingAnalyzer.GetOrderedIntervals(group);
        if (intervals.Count < 2)
            return plan;

        var previousMax = intervals[0].Max;
        var cumulativeShift = 0.0;

        for (var i = 1; i < intervals.Count; i++)
        {
            var current = intervals[i];
            var shiftedMin = current.Min + cumulativeShift;
            var shiftedMax = current.Max + cumulativeShift;
            var requiredMin = previousMax + targetGapDrawing;

            if (shiftedMin < requiredMin)
            {
                var extraShift = System.Math.Round(requiredMin - shiftedMin, 3);
                cumulativeShift += extraShift;
                shiftedMin += extraShift;
                shiftedMax += extraShift;
            }

            if (cumulativeShift > 1e-9)
            {
                plan.Proposals.Add(new DimensionMoveProposal
                {
                    DimensionId = current.Member.DimensionId,
                    AxisShift = cumulativeShift
                });
            }

            previousMax = shiftedMax;
        }

        return plan;
    }

    private static double ResolveTargetGapDrawing(DimensionGroupLineStack stack, double targetGapPaper)
    {
        var scale = stack.Groups
            .SelectMany(static group => group.Members)
            .Select(static member => member.Dimension.ViewScale)
            .FirstOrDefault(static scale => scale > 0);

        return System.Math.Round(targetGapPaper * (scale > 0 ? scale : 1.0), 3);
    }

    private static double ResolveTargetGapDrawing(DimensionGroup group, double targetGapPaper)
    {
        var scale = group.Members
            .Select(static member => member.Dimension.ViewScale)
            .FirstOrDefault(static value => value > 0);

        return System.Math.Round(targetGapPaper * (scale > 0 ? scale : 1.0), 3);
    }
}
