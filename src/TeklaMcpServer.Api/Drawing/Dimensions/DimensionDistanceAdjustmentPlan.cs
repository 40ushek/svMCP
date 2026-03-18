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
            if (group.Orientation is "horizontal" or "vertical")
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
                Reason = "Distance mapping is not defined for angled dimensions yet."
            });
        }

        return plan;
    }
}
