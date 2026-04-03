using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class DimensionDistanceAdjustmentProposal
{
    public int DimensionId { get; set; }
    public double CurrentDistance { get; set; }
    public double AxisShift { get; set; }
    public double NormalizationDelta { get; set; }
    public double SpacingDelta { get; set; }
    public double DistanceDelta { get; set; }
    public double TargetDistance { get; set; }
    public bool CanApply { get; set; }
    public string Reason { get; set; } = string.Empty;
}

internal sealed class DimensionDistanceAdjustmentPlan
{
    public int? ViewId { get; set; }
    public string ViewType { get; set; } = string.Empty;
    public string Orientation { get; set; } = string.Empty;
    public double TargetGapPaper { get; set; }
    public double TargetGapDrawing { get; set; }
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
            Orientation = stack.Orientation,
            TargetGapPaper = axisPlan.TargetGapPaper,
            TargetGapDrawing = axisPlan.TargetGapDrawing
        };

        var planningUnits = DimensionGroupSpacingAnalyzer.BuildPlanningUnits(stack);
        var unitsById = planningUnits
            .SelectMany(static unit => unit.Units)
            .GroupBy(static unit => unit.DimensionId)
            .ToDictionary(static group => group.Key, static group => group.First());
        var axisShiftById = axisPlan.Proposals
            .GroupBy(static proposal => proposal.DimensionId)
            .ToDictionary(
                static group => group.Key,
                static group => System.Math.Round(group.Sum(static proposal => proposal.AxisShift), 3));
        var proposalIds = unitsById.Values
            .Where(static unit => System.Math.Abs(unit.NormalizationDelta) > 1e-9)
            .Select(static unit => unit.DimensionId)
            .Concat(axisShiftById.Keys)
            .Distinct()
            .OrderBy(static id => id)
            .ToList();

        foreach (var dimensionId in proposalIds)
        {
            axisShiftById.TryGetValue(dimensionId, out var axisShift);

            if (!unitsById.TryGetValue(dimensionId, out var unit))
            {
                plan.Proposals.Add(new DimensionDistanceAdjustmentProposal
                {
                    DimensionId = dimensionId,
                    AxisShift = axisShift,
                    DistanceDelta = 0,
                    CanApply = false,
                    Reason = "Dimension move unit was not found for distance translation."
                });
                continue;
            }

            var normalizationDelta = unit.NormalizationDelta;
            var spacingDelta = 0.0;
            var currentDistance = unit.Distance;
            var targetDistanceBeforeSpacing = currentDistance + normalizationDelta;

            if (System.Math.Abs(axisShift) > 1e-9)
            {
                if (IsSupportedForDistanceTranslation(stack, out var unsupportedReason))
                {
                    if (unit.ReferenceLine == null)
                    {
                        plan.Proposals.Add(new DimensionDistanceAdjustmentProposal
                        {
                            DimensionId = dimensionId,
                            CurrentDistance = currentDistance,
                            AxisShift = axisShift,
                            NormalizationDelta = normalizationDelta,
                            SpacingDelta = 0,
                            DistanceDelta = 0,
                            TargetDistance = currentDistance + normalizationDelta,
                            CanApply = false,
                            Reason = "Distance mapping requires a reference line."
                        });
                        continue;
                    }

                    if (System.Math.Abs(targetDistanceBeforeSpacing) <= 1e-9)
                    {
                        plan.Proposals.Add(new DimensionDistanceAdjustmentProposal
                        {
                            DimensionId = dimensionId,
                            CurrentDistance = currentDistance,
                            AxisShift = axisShift,
                            NormalizationDelta = normalizationDelta,
                            SpacingDelta = 0,
                            DistanceDelta = 0,
                            TargetDistance = currentDistance + normalizationDelta,
                            CanApply = false,
                            Reason = "Distance mapping is ambiguous for zero-distance dimensions after normalization."
                        });
                        continue;
                    }

                    spacingDelta = axisShift * System.Math.Sign(targetDistanceBeforeSpacing);
                }
                else
                {
                    plan.Proposals.Add(new DimensionDistanceAdjustmentProposal
                    {
                        DimensionId = dimensionId,
                        CurrentDistance = currentDistance,
                        AxisShift = axisShift,
                        NormalizationDelta = normalizationDelta,
                        SpacingDelta = 0,
                        DistanceDelta = 0,
                        TargetDistance = currentDistance + normalizationDelta,
                        CanApply = false,
                        Reason = unsupportedReason
                    });
                    continue;
                }
            }

            var distanceDelta = System.Math.Round(normalizationDelta + spacingDelta, 3);
            plan.Proposals.Add(new DimensionDistanceAdjustmentProposal
            {
                DimensionId = dimensionId,
                CurrentDistance = currentDistance,
                AxisShift = axisShift,
                NormalizationDelta = normalizationDelta,
                SpacingDelta = spacingDelta,
                DistanceDelta = distanceDelta,
                TargetDistance = System.Math.Round(currentDistance + distanceDelta, 3),
                CanApply = true
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
            Orientation = group.Orientation,
            TargetGapPaper = axisPlan.TargetGapPaper,
            TargetGapDrawing = axisPlan.TargetGapDrawing
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
            reason = "Distance mapping requires reference line geometry.";
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
            reason = "Distance mapping requires reference line geometry.";
            return false;
        }

        reason = string.Empty;
        return true;
    }
}
