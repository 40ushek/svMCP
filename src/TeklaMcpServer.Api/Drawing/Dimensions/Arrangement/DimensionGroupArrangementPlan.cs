using System.Collections.Generic;
using System.Linq;

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
    public static DimensionGroupArrangementPlan BuildPlan(
        DimensionGroupLineStack stack,
        double targetGap,
        DimensionDecisionContext? decisionContext = null)
    {
        var targetGapDrawing = ResolveTargetGapDrawing(stack, targetGap, decisionContext);
        var plan = new DimensionGroupArrangementPlan
        {
            ViewId = stack.ViewId,
            ViewType = stack.ViewType,
            Orientation = stack.Orientation,
            TargetGapPaper = targetGap,
            TargetGapDrawing = targetGapDrawing
        };

        var units = DimensionGroupSpacingAnalyzer.BuildPlanningUnits(stack);
        var partsBoundsAxisShift = ResolvePartsBoundsAxisShift(stack, units, targetGap, decisionContext);
        if (System.Math.Abs(partsBoundsAxisShift) > 1e-9)
        {
            foreach (var unit in units.SelectMany(static unit => unit.Units))
                AddOrAccumulateProposal(plan, unit.DimensionId, partsBoundsAxisShift);
        }

        if (units.Count < 2)
            return plan;

        var previousMax = units[0].MaxOffset;

        for (var i = 1; i < units.Count; i++)
        {
            var current = units[i];
            var axisShift = System.Math.Round((previousMax + targetGapDrawing) - current.MinOffset, 3);
            var shiftedMax = current.MaxOffset + axisShift;

            if (System.Math.Abs(axisShift) > 1e-9)
            {
                foreach (var unit in current.Units)
                    AddOrAccumulateProposal(plan, unit.DimensionId, axisShift);
            }

            previousMax = shiftedMax;
        }

        return plan;
    }

    public static DimensionGroupArrangementPlan BuildPlan(
        DimensionGroup group,
        double targetGap,
        DimensionDecisionContext? decisionContext = null)
    {
        var targetGapDrawing = ResolveTargetGapDrawing(group, targetGap, decisionContext);
        var plan = new DimensionGroupArrangementPlan
        {
            ViewId = group.ViewId,
            ViewType = group.ViewType,
            Orientation = group.Orientation,
            TargetGapPaper = targetGap,
            TargetGapDrawing = targetGapDrawing
        };

        var intervals = DimensionGroupSpacingAnalyzer.GetOrderedIntervals(group);
        var partsBoundsAxisShift = ResolvePartsBoundsAxisShift(group, targetGap, decisionContext);
        if (System.Math.Abs(partsBoundsAxisShift) > 1e-9)
        {
            foreach (var interval in intervals)
                AddOrAccumulateProposal(plan, interval.Member.DimensionId, partsBoundsAxisShift);
        }

        if (intervals.Count < 2)
            return plan;

        var previousMax = intervals[0].Max;

        for (var i = 1; i < intervals.Count; i++)
        {
            var current = intervals[i];
            var axisShift = System.Math.Round((previousMax + targetGapDrawing) - current.Min, 3);
            var shiftedMax = current.Max + axisShift;

            if (System.Math.Abs(axisShift) > 1e-9)
                AddOrAccumulateProposal(plan, current.Member.DimensionId, axisShift);

            previousMax = shiftedMax;
        }

        return plan;
    }

    private static double ResolveTargetGapDrawing(
        DimensionGroupLineStack stack,
        double targetGapPaper,
        DimensionDecisionContext? decisionContext)
    {
        var scale = ResolveViewScale(decisionContext, stack.ViewId) ?? stack.Groups
            .SelectMany(static group => group.Members)
            .Select(static member => member.ViewScale)
            .FirstOrDefault(static scale => scale > 0);

        return System.Math.Round(targetGapPaper * (scale > 0 ? scale : 1.0), 3);
    }

    private static double ResolveTargetGapDrawing(
        DimensionGroup group,
        double targetGapPaper,
        DimensionDecisionContext? decisionContext)
    {
        var scale = ResolveViewScale(decisionContext, group.ViewId) ?? group.Members
            .Select(static member => member.ViewScale)
            .FirstOrDefault(static value => value > 0);

        return System.Math.Round(targetGapPaper * (scale > 0 ? scale : 1.0), 3);
    }

    private static double? ResolveViewScale(DimensionDecisionContext? decisionContext, int? viewId)
    {
        if (decisionContext?.View == null)
            return null;

        if (!viewId.HasValue || decisionContext.View.ViewId != viewId)
            return null;

        return decisionContext.View.ViewScale > 0 ? decisionContext.View.ViewScale : null;
    }

    private static void AddOrAccumulateProposal(DimensionGroupArrangementPlan plan, int dimensionId, double axisShift)
    {
        if (System.Math.Abs(axisShift) <= 1e-9)
            return;

        var existing = plan.Proposals.FirstOrDefault(proposal => proposal.DimensionId == dimensionId);
        if (existing != null)
        {
            existing.AxisShift = System.Math.Round(existing.AxisShift + axisShift, 3);
            return;
        }

        plan.Proposals.Add(new DimensionMoveProposal
        {
            DimensionId = dimensionId,
            AxisShift = System.Math.Round(axisShift, 3)
        });
    }

    private static double ResolvePartsBoundsAxisShift(
        DimensionGroupLineStack stack,
        IReadOnlyList<DimensionStackPlanningUnit> units,
        double targetGapPaper,
        DimensionDecisionContext? decisionContext)
    {
        var expectedDimensionIds = units
            .SelectMany(static unit => unit.Units)
            .Select(static unit => unit.DimensionId)
            .Distinct()
            .OrderBy(static id => id)
            .ToList();
        var sideAndDelta = units
            .SelectMany(static unit => unit.Units)
            .Select(unit => (unit.DimensionId, Gap: TryEvaluatePartsBoundsGap(unit.DimensionId, targetGapPaper, decisionContext)))
            .Where(static item => item.Gap != null && item.Gap.Value.CanEvaluate)
            .GroupBy(static item => item.DimensionId)
            .Select(static group => (DimensionId: group.Key, Gap: group.First().Gap!.Value))
            .Select(static item => (item.DimensionId, Side: item.Gap.Side, Delta: item.Gap.DeltaDrawing, CurrentGap: item.Gap.CurrentGapDrawing))
            .Where(static item => !string.IsNullOrWhiteSpace(item.Side))
            .OrderBy(static item => item.CurrentGap)
            .ToList();

        if (sideAndDelta.Count == 0)
            return 0;

        var evaluatedDimensionIds = sideAndDelta
            .Select(static item => item.DimensionId)
            .OrderBy(static id => id)
            .ToList();
        if (!expectedDimensionIds.SequenceEqual(evaluatedDimensionIds))
            return 0;

        var side = sideAndDelta[0].Side;
        if (sideAndDelta.Any(item => !string.Equals(item.Side, side, System.StringComparison.Ordinal)))
            return 0;

        var outwardDelta = sideAndDelta[0].Delta;
        if (System.Math.Abs(outwardDelta) <= 1e-9)
            return 0;

        var outwardSign = ResolveRawOutwardSign(side);
        if (!outwardSign.HasValue)
            return 0;

        var stackSign = stack.TopDirection == 0 ? 1 : stack.TopDirection;
        return System.Math.Round(outwardDelta * outwardSign.Value * stackSign, 3);
    }

    private static double ResolvePartsBoundsAxisShift(
        DimensionGroup group,
        double targetGapPaper,
        DimensionDecisionContext? decisionContext)
    {
        var expectedDimensionIds = group.Members
            .Select(static member => member.DimensionId)
            .Distinct()
            .OrderBy(static id => id)
            .ToList();
        var sideAndDelta = group.Members
            .Select(member => (member.DimensionId, Gap: TryEvaluatePartsBoundsGap(member.DimensionId, targetGapPaper, decisionContext)))
            .Where(static item => item.Gap != null && item.Gap.Value.CanEvaluate)
            .GroupBy(static item => item.DimensionId)
            .Select(static group => (DimensionId: group.Key, Gap: group.First().Gap!.Value))
            .Select(static item => (item.DimensionId, Side: item.Gap.Side, Delta: item.Gap.DeltaDrawing, CurrentGap: item.Gap.CurrentGapDrawing))
            .Where(static item => !string.IsNullOrWhiteSpace(item.Side))
            .OrderBy(static item => item.CurrentGap)
            .ToList();

        if (sideAndDelta.Count == 0)
            return 0;

        var evaluatedDimensionIds = sideAndDelta
            .Select(static item => item.DimensionId)
            .OrderBy(static id => id)
            .ToList();
        if (!expectedDimensionIds.SequenceEqual(evaluatedDimensionIds))
            return 0;

        var side = sideAndDelta[0].Side;
        if (sideAndDelta.Any(item => !string.Equals(item.Side, side, System.StringComparison.Ordinal)))
            return 0;

        var outwardDelta = sideAndDelta[0].Delta;
        if (System.Math.Abs(outwardDelta) <= 1e-9)
            return 0;

        var outwardSign = ResolveRawOutwardSign(side);
        return !outwardSign.HasValue ? 0 : System.Math.Round(outwardDelta * outwardSign.Value, 3);
    }

    private static (bool CanEvaluate, string Side, double CurrentGapDrawing, double DeltaDrawing)? TryEvaluatePartsBoundsGap(
        int dimensionId,
        double targetGapPaper,
        DimensionDecisionContext? decisionContext)
    {
        if (decisionContext?.View == null)
            return null;

        var context = decisionContext.FindDimension(dimensionId);
        if (context == null)
            return null;

        var placement = DimensionViewPlacementInfoBuilder.Build(context, decisionContext.View);
        var gap = DimensionPartsBoundsGapPolicy.Evaluate(placement, targetGapPaper);
        return (gap.CanEvaluate, placement.PartsBoundsSide, gap.CurrentGapDrawing, gap.SuggestedOutwardDeltaDrawing);
    }

    private static int? ResolveRawOutwardSign(string side)
    {
        return side switch
        {
            "top" => 1,
            "right" => 1,
            "bottom" => -1,
            "left" => -1,
            _ => null
        };
    }
}
