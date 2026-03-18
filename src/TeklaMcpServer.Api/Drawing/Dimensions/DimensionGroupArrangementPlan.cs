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
    public double TargetGap { get; set; }
    public List<DimensionMoveProposal> Proposals { get; } = [];
    public bool HasChanges => Proposals.Count > 0;
}

internal static class DimensionGroupArrangementPlanner
{
    public static DimensionGroupArrangementPlan BuildPlan(DimensionGroup group, double targetGap)
    {
        var plan = new DimensionGroupArrangementPlan
        {
            ViewId = group.ViewId,
            ViewType = group.ViewType,
            Orientation = group.Orientation,
            TargetGap = targetGap
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
            var requiredMin = previousMax + targetGap;

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
}
