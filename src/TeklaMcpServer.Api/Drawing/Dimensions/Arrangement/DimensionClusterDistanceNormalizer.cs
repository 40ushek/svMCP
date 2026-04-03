using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

internal static class DimensionClusterDistanceNormalizer
{
    internal const double NormalizationDistanceTolerance = 3.0;

    public static void Annotate(IReadOnlyList<DimensionStackPlanningUnit> planningUnits)
    {
        foreach (var planningUnit in planningUnits)
        {
            ResetPlanningUnit(planningUnit);

            if (!string.Equals(planningUnit.Status, "aligned", System.StringComparison.Ordinal) || planningUnit.Units.Count < 2)
            {
                var reason = string.IsNullOrEmpty(planningUnit.Reason)
                    ? "Runtime normalization is only enabled for aligned clusters."
                    : planningUnit.Reason;
                MarkSkipped(planningUnit, "not_applicable", reason);
                continue;
            }

            var anchor = planningUnit.Units.FirstOrDefault(unit => unit.DimensionId == planningUnit.AnchorDimensionId);
            if (anchor == null)
            {
                MarkSkipped(planningUnit, "skipped", "Anchor distance is not available for normalization.");
                continue;
            }

            planningUnit.AnchorDistance = anchor.Distance;

            var distances = planningUnit.Units
                .Select(static unit => unit.Distance)
                .OrderBy(static value => value)
                .ToList();

            planningUnit.DistanceSpread = distances.Count == 0
                ? null
                : System.Math.Round(distances[distances.Count - 1] - distances[0], 3);

            if (!planningUnit.DistanceSpread.HasValue)
            {
                MarkSkipped(planningUnit, "skipped", "Cluster distances are not available for normalization.");
                continue;
            }

            if (planningUnit.DistanceSpread.Value > NormalizationDistanceTolerance + 1e-9)
            {
                MarkSkipped(
                    planningUnit,
                    "skipped",
                    $"Distance spread {planningUnit.DistanceSpread.Value:0.###} exceeds normalization threshold {NormalizationDistanceTolerance:0.###}.");
                continue;
            }

            planningUnit.NormalizationApplied = true;
            planningUnit.NormalizationReason = string.Empty;

            foreach (var unit in planningUnit.Units)
            {
                unit.NormalizationDelta = System.Math.Round(anchor.Distance - unit.Distance, 3);
                unit.NormalizationStatus = unit.DimensionId == anchor.DimensionId ? "anchor" : "normalized";
                unit.NormalizationReason = string.Empty;
            }
        }
    }

    private static void ResetPlanningUnit(DimensionStackPlanningUnit planningUnit)
    {
        planningUnit.NormalizationThreshold = NormalizationDistanceTolerance;
        planningUnit.AnchorDistance = null;
        planningUnit.DistanceSpread = null;
        planningUnit.NormalizationApplied = false;
        planningUnit.NormalizationReason = string.Empty;

        foreach (var unit in planningUnit.Units)
        {
            unit.NormalizationDelta = 0;
            unit.NormalizationStatus = string.Empty;
            unit.NormalizationReason = string.Empty;
        }
    }

    private static void MarkSkipped(DimensionStackPlanningUnit planningUnit, string status, string reason)
    {
        planningUnit.NormalizationApplied = false;
        planningUnit.NormalizationReason = reason;

        foreach (var unit in planningUnit.Units)
        {
            unit.NormalizationDelta = 0;
            unit.NormalizationStatus = status;
            unit.NormalizationReason = reason;
        }
    }
}
