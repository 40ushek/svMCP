using System.Collections.Generic;
using System.Linq;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;

namespace TeklaMcpServer.Api.Drawing;

public sealed partial class TeklaDrawingDimensionsApi
{
    internal List<DimensionGroupSpacingAnalysis> AnalyzeDimensionGroupSpacing(int? viewId)
    {
        return GetDimensionGroups(viewId)
            .Select(DimensionGroupSpacingAnalyzer.Analyze)
            .ToList();
    }

    internal List<DimensionGroupArrangementPlan> PlanDimensionGroupSpacing(int? viewId, double targetGap)
    {
        return GetDimensionGroups(viewId)
            .Select(group => DimensionGroupArrangementPlanner.BuildPlan(group, targetGap))
            .ToList();
    }

    internal List<DimensionDistanceAdjustmentPlan> PlanDimensionDistanceAdjustments(int? viewId, double targetGap)
    {
        return GetDimensionGroups(viewId)
            .Select(group =>
            {
                var axisPlan = DimensionGroupArrangementPlanner.BuildPlan(group, targetGap);
                return DimensionDistanceAdjustmentTranslator.BuildPlan(group, axisPlan);
            })
            .ToList();
    }

    internal ArrangeDimensionsResult ApplyDimensionDistanceAdjustments(int? viewId, double targetGap)
    {
        var result = new ArrangeDimensionsResult();
        var activeDrawing = new DrawingHandler().GetActiveDrawing();
        if (activeDrawing == null)
            throw new DrawingNotOpenException();

        var plans = PlanDimensionDistanceAdjustments(viewId, targetGap);

        var deltas = new Dictionary<int, double>();
        foreach (var plan in plans)
        {
            foreach (var proposal in plan.Proposals)
            {
                if (!proposal.CanApply)
                {
                    result.SkippedCount++;
                    if (!string.IsNullOrEmpty(proposal.Reason))
                        result.SkipReasons.Add($"dim {proposal.DimensionId}: {proposal.Reason}");
                    continue;
                }

                if (System.Math.Abs(proposal.DistanceDelta) < 1e-9)
                {
                    result.SkippedCount++;
                    continue;
                }

                if (deltas.TryGetValue(proposal.DimensionId, out var existing))
                    deltas[proposal.DimensionId] = existing + proposal.DistanceDelta;
                else
                    deltas[proposal.DimensionId] = proposal.DistanceDelta;
            }
        }

        if (deltas.Count == 0)
            return result;

        var previousAutoFetch = DrawingEnumeratorBase.AutoFetch;
        DrawingEnumeratorBase.AutoFetch = false;
        try
        {
            var targets = new Dictionary<int, StraightDimensionSet>();
            var allDims = activeDrawing.GetSheet().GetAllObjects(typeof(StraightDimensionSet));
            while (allDims.MoveNext())
            {
                if (allDims.Current is not StraightDimensionSet ds)
                    continue;

                var id = ds.GetIdentifier().ID;
                if (!deltas.ContainsKey(id))
                    continue;

                targets[id] = ds;
            }

            foreach (var id in deltas.Keys)
            {
                if (!targets.ContainsKey(id))
                {
                    result.SkippedCount++;
                    result.SkipReasons.Add($"dim {id}: not found on sheet");
                }
            }

            if (targets.Count == 0)
                return result;

            var originalDistances = new Dictionary<int, double>();
            var modifiedIds = new List<int>();

            try
            {
                foreach (var pair in targets)
                {
                    var id = pair.Key;
                    var ds = pair.Value;
                    var delta = deltas[id];

                    originalDistances[id] = ds.Distance;
                    ds.Distance += delta;
                    ds.Modify();
                    modifiedIds.Add(id);
                }

                activeDrawing.CommitChanges("(MCP) ArrangeDimensions");
            }
            catch
            {
                foreach (var id in modifiedIds)
                {
                    if (!targets.TryGetValue(id, out var ds) || !originalDistances.TryGetValue(id, out var original))
                        continue;

                    ds.Distance = original;
                    ds.Modify();
                }

                if (modifiedIds.Count > 0)
                {
                    try
                    {
                        activeDrawing.CommitChanges("(MCP) RollbackArrangeDimensions");
                    }
                    catch
                    {
                    }
                }

                throw;
            }

            foreach (var pair in targets)
            {
                var id = pair.Key;
                var ds = pair.Value;
                result.Applied.Add(new ArrangeDimensionApplied
                {
                    DimensionId = id,
                    DistanceDelta = deltas[id],
                    NewDistance = ds.Distance
                });
            }

            result.AppliedCount = result.Applied.Count;
            return result;
        }
        finally
        {
            DrawingEnumeratorBase.AutoFetch = previousAutoFetch;
        }
    }
}
