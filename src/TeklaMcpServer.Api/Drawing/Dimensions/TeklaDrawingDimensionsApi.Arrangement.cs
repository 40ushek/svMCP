using System.Collections.Generic;
using System.Linq;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;

namespace TeklaMcpServer.Api.Drawing;

public sealed partial class TeklaDrawingDimensionsApi
{
    public DimensionArrangementDebugResult GetDimensionArrangementDebug(int? viewId, double targetGap)
    {
        if (targetGap < 0)
            throw new System.ArgumentOutOfRangeException(nameof(targetGap), "targetGap must be >= 0.");

        var dimensions = GetDimensions(viewId);
        var groups = DimensionGroupFactory.BuildGroups(dimensions);
        var spacing = groups.Select(DimensionGroupSpacingAnalyzer.Analyze).ToList();
        var plans = groups.Select(group =>
        {
            var axisPlan = DimensionGroupArrangementPlanner.BuildPlan(group, targetGap);
            return DimensionDistanceAdjustmentTranslator.BuildPlan(group, axisPlan);
        }).ToList();

        var result = new DimensionArrangementDebugResult
        {
            ViewFilteredTotal = dimensions.Total,
            GroupCount = groups.Count,
            TargetGap = targetGap
        };

        foreach (var group in groups)
        {
            result.Groups.Add(new DimensionArrangementDebugGroupInfo
            {
                ViewId = group.ViewId,
                ViewType = group.ViewType,
                Orientation = group.Orientation,
                MemberCount = group.Members.Count,
                MaximumDistance = group.MaximumDistance,
                Bounds = group.Bounds
            });
        }

        foreach (var analysis in spacing)
        {
            var info = new DimensionArrangementDebugSpacingInfo
            {
                ViewId = analysis.ViewId,
                ViewType = analysis.ViewType,
                Orientation = analysis.Orientation,
                HasOverlaps = analysis.HasOverlaps,
                MinimumDistance = analysis.MinimumDistance
            };

            foreach (var pair in analysis.Pairs)
            {
                info.Pairs.Add(new DimensionArrangementDebugSpacingPair
                {
                    FirstDimensionId = pair.FirstDimensionId,
                    SecondDimensionId = pair.SecondDimensionId,
                    Distance = pair.Distance,
                    IsOverlap = pair.IsOverlap
                });
            }

            result.Spacing.Add(info);
        }

        foreach (var plan in plans)
        {
            var info = new DimensionArrangementDebugPlanInfo
            {
                ViewId = plan.ViewId,
                ViewType = plan.ViewType,
                Orientation = plan.Orientation,
                ProposalCount = plan.Proposals.Count,
                HasApplicableChanges = plan.HasApplicableChanges
            };

            foreach (var proposal in plan.Proposals)
            {
                info.Proposals.Add(new DimensionArrangementDebugProposal
                {
                    DimensionId = proposal.DimensionId,
                    AxisShift = proposal.AxisShift,
                    DistanceDelta = proposal.DistanceDelta,
                    CanApply = proposal.CanApply,
                    Reason = proposal.Reason
                });
            }

            result.Plans.Add(info);
        }

        return result;
    }

    public ArrangeDimensionsResult ArrangeDimensions(int? viewId, double targetGap)
    {
        if (targetGap < 0)
            throw new System.ArgumentOutOfRangeException(nameof(targetGap), "targetGap must be >= 0.");

        return ApplyDimensionDistanceAdjustments(viewId, targetGap);
    }

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
