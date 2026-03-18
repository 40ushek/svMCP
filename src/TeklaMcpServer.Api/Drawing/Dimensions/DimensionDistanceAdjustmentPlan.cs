using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class DimensionDistanceAdjustmentProposal
{
    public int DimensionId { get; set; }
    public double AxisShift { get; set; }
    public double DistanceDelta { get; set; }
    public bool CanApply { get; set; }
    public string Reason { get; set; } = string.Empty;
}

internal sealed class DimensionDistanceAdjustmentPlan
{
    public int? ViewId { get; set; }
    public string ViewType { get; set; } = string.Empty;
    public string Orientation { get; set; } = string.Empty;
    public List<DimensionDistanceAdjustmentProposal> Proposals { get; } = [];
    public bool HasApplicableChanges => Proposals.Any(static proposal => proposal.CanApply);
}

internal static class DimensionDistanceAdjustmentTranslator
{
    public static DimensionDistanceAdjustmentPlan BuildPlan(DimensionGroupLineStack stack, DimensionGroupArrangementPlan axisPlan)
    {
        var plan = new DimensionDistanceAdjustmentPlan
        {
            ViewId = stack.ViewId,
            ViewType = stack.ViewType,
            Orientation = stack.Orientation
        };

        var unitsById = DimensionGroupSpacingAnalyzer.BuildMoveUnits(stack)
            .ToDictionary(static unit => unit.DimensionId);

        foreach (var proposal in axisPlan.Proposals)
        {
            if (IsSupportedForDistanceTranslation(stack, out var unsupportedReason))
            {
                if (!unitsById.TryGetValue(proposal.DimensionId, out var unit))
                {
                    plan.Proposals.Add(new DimensionDistanceAdjustmentProposal
                    {
                        DimensionId = proposal.DimensionId,
                        AxisShift = proposal.AxisShift,
                        DistanceDelta = 0,
                        CanApply = false,
                        Reason = "Dimension move unit was not found for distance translation."
                    });
                    continue;
                }

                if (unit.ReferenceLine == null)
                {
                    plan.Proposals.Add(new DimensionDistanceAdjustmentProposal
                    {
                        DimensionId = proposal.DimensionId,
                        AxisShift = proposal.AxisShift,
                        DistanceDelta = 0,
                        CanApply = false,
                        Reason = "Distance mapping requires a reference line."
                    });
                    continue;
                }

                if (System.Math.Abs(unit.Distance) <= 1e-9)
                {
                    plan.Proposals.Add(new DimensionDistanceAdjustmentProposal
                    {
                        DimensionId = proposal.DimensionId,
                        AxisShift = proposal.AxisShift,
                        DistanceDelta = 0,
                        CanApply = false,
                        Reason = "Distance mapping is ambiguous for zero-distance dimensions."
                    });
                    continue;
                }

                var sign = System.Math.Sign(unit.Distance);
                plan.Proposals.Add(new DimensionDistanceAdjustmentProposal
                {
                    DimensionId = proposal.DimensionId,
                    AxisShift = proposal.AxisShift,
                    DistanceDelta = proposal.AxisShift * sign,
                    CanApply = true
                });
                continue;
            }

            plan.Proposals.Add(new DimensionDistanceAdjustmentProposal
            {
                DimensionId = proposal.DimensionId,
                AxisShift = proposal.AxisShift,
                DistanceDelta = 0,
                CanApply = false,
                Reason = unsupportedReason
            });
        }

        return plan;
    }

    public static DimensionDistanceAdjustmentPlan BuildPlan(DimensionGroup group, DimensionGroupArrangementPlan axisPlan)
    {
        var plan = new DimensionDistanceAdjustmentPlan
        {
            ViewId = group.ViewId,
            ViewType = group.ViewType,
            Orientation = group.Orientation
        };
        var membersById = group.Members.ToDictionary(static member => member.DimensionId);

        foreach (var proposal in axisPlan.Proposals)
        {
            if (IsSupportedForDistanceTranslation(group, out var unsupportedReason))
            {
                if (!membersById.TryGetValue(proposal.DimensionId, out var member))
                {
                    plan.Proposals.Add(new DimensionDistanceAdjustmentProposal
                    {
                        DimensionId = proposal.DimensionId,
                        AxisShift = proposal.AxisShift,
                        DistanceDelta = 0,
                        CanApply = false,
                        Reason = "Dimension group member was not found for distance translation."
                    });
                    continue;
                }

                if (member.ReferenceLine == null)
                {
                    plan.Proposals.Add(new DimensionDistanceAdjustmentProposal
                    {
                        DimensionId = proposal.DimensionId,
                        AxisShift = proposal.AxisShift,
                        DistanceDelta = 0,
                        CanApply = false,
                        Reason = "Distance mapping requires a reference line."
                    });
                    continue;
                }

                if (System.Math.Abs(member.Distance) <= 1e-9)
                {
                    plan.Proposals.Add(new DimensionDistanceAdjustmentProposal
                    {
                        DimensionId = proposal.DimensionId,
                        AxisShift = proposal.AxisShift,
                        DistanceDelta = 0,
                        CanApply = false,
                        Reason = "Distance mapping is ambiguous for zero-distance dimensions."
                    });
                    continue;
                }

                var sign = System.Math.Sign(member.Distance);
                plan.Proposals.Add(new DimensionDistanceAdjustmentProposal
                {
                    DimensionId = proposal.DimensionId,
                    AxisShift = proposal.AxisShift,
                    DistanceDelta = proposal.AxisShift * sign,
                    CanApply = true
                });
                continue;
            }

            plan.Proposals.Add(new DimensionDistanceAdjustmentProposal
            {
                DimensionId = proposal.DimensionId,
                AxisShift = proposal.AxisShift,
                DistanceDelta = 0,
                CanApply = false,
                Reason = unsupportedReason
            });
        }

        return plan;
    }

    private static bool IsSupportedForDistanceTranslation(DimensionGroupLineStack stack, out string reason)
    {
        if (!stack.Direction.HasValue)
        {
            reason = "Distance mapping requires a normalized group direction.";
            return false;
        }

        var direction = stack.Direction.Value;
        var isHorizontal = System.Math.Abs(direction.Y) <= System.Math.Abs(direction.X) * 0.01;
        var isVertical = System.Math.Abs(direction.X) <= System.Math.Abs(direction.Y) * 0.01;
        if (!isHorizontal && !isVertical)
        {
            reason = "Distance mapping is only defined for axis-aligned parallel groups.";
            return false;
        }

        if (stack.TopDirection == 0)
        {
            reason = "Distance mapping requires a consistent top direction.";
            return false;
        }

        if (!stack.Groups.SelectMany(static group => group.Members).Any(static member => member.ReferenceLine != null))
        {
            reason = "Distance mapping requires reference-line geometry.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static bool IsSupportedForDistanceTranslation(DimensionGroup group, out string reason)
    {
        if (!group.Direction.HasValue)
        {
            reason = "Distance mapping requires a normalized group direction.";
            return false;
        }

        var direction = group.Direction.Value;
        var isHorizontal = System.Math.Abs(direction.Y) <= System.Math.Abs(direction.X) * 0.01;
        var isVertical = System.Math.Abs(direction.X) <= System.Math.Abs(direction.Y) * 0.01;
        if (!isHorizontal && !isVertical)
        {
            reason = "Distance mapping is only defined for axis-aligned parallel groups.";
            return false;
        }

        if (group.TopDirection == 0)
        {
            reason = "Distance mapping requires a consistent top direction.";
            return false;
        }

        if (!group.Members.Any(static member => member.ReferenceLine != null))
        {
            reason = "Distance mapping requires reference-line geometry.";
            return false;
        }

        reason = string.Empty;
        return true;
    }
}
