using System.Collections.Generic;
using System.Linq;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;

namespace TeklaMcpServer.Api.Drawing;

public sealed partial class TeklaDrawingDimensionsApi
{
    internal const double DefaultArrangeTargetGapPaper = 10.0;

    internal DimensionArrangementDebugResult GetDimensionArrangementDebug(int? viewId, double targetGap)
    {
        if (targetGap < 0)
            throw new System.ArgumentOutOfRangeException(nameof(targetGap), "targetGap must be >= 0.");

        var rawGroups = GetArrangeGroups(viewId);
        var dedup = DimensionArrangementDedup.ReduceWithDebug(rawGroups);
        var groups = dedup.ReducedGroups;
        var stacks = DimensionGroupSpacingAnalyzer.BuildStacks(groups);
        var spacing = DimensionGroupSpacingAnalyzer.AnalyzeStacks(groups);
        var plans = stacks.Select(stack =>
        {
            var axisPlan = DimensionGroupArrangementPlanner.BuildPlan(stack, targetGap);
            return DimensionDistanceAdjustmentTranslator.BuildPlan(stack, axisPlan);
        }).ToList();

        var result = new DimensionArrangementDebugResult
        {
            RawViewFilteredTotal = rawGroups.Sum(static group => group.DimensionList.Count),
            ViewFilteredTotal = groups.Sum(static group => group.DimensionList.Count),
            RawGroupCount = rawGroups.Count,
            GroupCount = groups.Count,
            DedupRejectedCount = dedup.Groups.Sum(static g => g.Items.Count(static item => string.Equals(item.Status, "rejected", System.StringComparison.Ordinal))),
            TargetGapPaper = targetGap
        };

        foreach (var dedupGroup in dedup.Groups)
        {
            var info = new DimensionArrangementDebugDedupGroupInfo
            {
                ViewId = dedupGroup.RawGroup.ViewId,
                ViewType = dedupGroup.RawGroup.ViewType,
                DimensionType = dedupGroup.RawGroup.DimensionType,
                RawMemberCount = dedupGroup.RawGroup.DimensionList.Count,
                ReducedMemberCount = dedupGroup.ReducedGroup.DimensionList.Count
            };

            foreach (var item in dedupGroup.Items.OrderBy(static item => item.Item.SortKey).ThenBy(static item => item.Item.DimensionId))
            {
                if (string.Equals(item.Status, "rejected", System.StringComparison.Ordinal))
                    info.RejectedCount++;

                info.Items.Add(new DimensionArrangementDebugDedupItemInfo
                {
                    DimensionId = item.Item.DimensionId,
                    SourceKind = item.Item.SourceKind.ToString(),
                    GeometryKind = item.Item.GeometryKind.ToString(),
                    Status = item.Status,
                    Reason = item.Reason,
                    RepresentativeDimensionId = item.RepresentativeDimensionId
                });
            }

            result.Dedup.Add(info);
        }

        foreach (var group in groups)
        {
            var info = new DimensionArrangementDebugGroupInfo
            {
                ViewId = group.ViewId,
                ViewType = group.ViewType,
                DimensionType = group.DimensionType,
                Orientation = group.Orientation,
                DirectionX = group.Direction?.X,
                DirectionY = group.Direction?.Y,
                TopDirection = group.TopDirection,
                ReferenceLine = CopyLine(group.ReferenceLine),
                MemberCount = group.Members.Count,
                MaximumDistance = group.MaximumDistance,
                Bounds = group.Bounds
            };

            info.GroupingBasis.Add("same view");
            info.GroupingBasis.Add("same domain dimensionType");
            info.GroupingBasis.Add("parallel direction");
            info.GroupingBasis.Add("compatible topDirection");
            info.GroupingBasis.Add("compatible reference-line extents");

            foreach (var member in group.DimensionList)
            {
                info.Members.Add(new DimensionArrangementDebugMemberInfo
                {
                    DimensionId = member.DimensionId,
                    Distance = member.Distance,
                    SortKey = member.SortKey,
                    DirectionX = member.DirectionX,
                    DirectionY = member.DirectionY,
                    TopDirection = member.TopDirection,
                    Bounds = member.Bounds,
                    ReferenceLine = CopyLine(member.ReferenceLine),
                    LeadLineMain = CopyLine(member.LeadLineMain),
                    LeadLineSecond = CopyLine(member.LeadLineSecond)
                });
            }

            result.Groups.Add(info);
        }

        foreach (var stack in stacks)
        {
            var planningUnits = DimensionGroupSpacingAnalyzer.BuildPlanningUnits(stack);
            var stackUnitsById = planningUnits
                .SelectMany(static unit => unit.Units)
                .GroupBy(static unit => unit.DimensionId)
                .ToDictionary(static group => group.Key, static group => group.First());

            var info = new DimensionArrangementDebugStackInfo
            {
                ViewId = stack.ViewId,
                ViewType = stack.ViewType,
                DimensionType = ResolveStackDimensionType(stack),
                Orientation = stack.Orientation,
                DirectionX = stack.Direction?.X,
                DirectionY = stack.Direction?.Y,
                TopDirection = stack.TopDirection,
                ReferenceLine = CopyLine(stack.Groups.FirstOrDefault(static group => group.ReferenceLine != null)?.ReferenceLine),
                AlignmentApplied = planningUnits.Any(static unit => unit.Units.Count > 1 && string.Equals(unit.Status, "aligned", System.StringComparison.Ordinal))
            };

            info.GroupingBasis.Add("same view");
            info.GroupingBasis.Add("parallel direction");
            info.GroupingBasis.Add("compatible topDirection");
            info.GroupingBasis.Add("overlapping extents along dimension line");

            foreach (var group in stack.Groups)
            {
                foreach (var member in group.DimensionList
                             .GroupBy(static member => member.DimensionId)
                             .Select(static grouped => grouped.First())
                             .OrderBy(static member => member.SortKey))
                {
                    stackUnitsById.TryGetValue(member.DimensionId, out var planningUnit);
                    info.Members.Add(new DimensionArrangementDebugStackMemberInfo
                    {
                        DimensionId = member.DimensionId,
                        DimensionType = member.DimensionType,
                        Orientation = member.Dimension.Orientation,
                        Distance = member.Distance,
                        NormalizationDelta = planningUnit?.NormalizationDelta ?? 0,
                        NormalizationStatus = planningUnit?.NormalizationStatus ?? string.Empty,
                        NormalizationReason = planningUnit?.NormalizationReason ?? string.Empty,
                        ReferenceLine = CopyLine(member.ReferenceLine),
                        PlanningReferenceLine = CopyLine(planningUnit?.PlanningReferenceLine),
                        AlignmentClusterId = planningUnit?.AlignmentClusterId ?? 0,
                        AlignmentAnchorDimensionId = planningUnit?.AlignmentAnchorDimensionId,
                        AlignmentStatus = planningUnit?.AlignmentStatus ?? string.Empty,
                        AlignmentReason = planningUnit?.AlignmentReason ?? string.Empty
                    });
                }
            }

            foreach (var planningUnit in planningUnits)
            {
                var cluster = new DimensionArrangementDebugAlignmentClusterInfo
                {
                    ClusterId = planningUnit.ClusterId,
                    AnchorDimensionId = planningUnit.AnchorDimensionId,
                    AnchorReferenceLine = CopyLine(planningUnit.AnchorReferenceLine),
                    AnchorDistance = planningUnit.AnchorDistance,
                    DistanceSpread = planningUnit.DistanceSpread,
                    Applied = planningUnit.Units.Count > 1 && string.Equals(planningUnit.Status, "aligned", System.StringComparison.Ordinal),
                    NormalizationApplied = planningUnit.NormalizationApplied,
                    NormalizationThreshold = planningUnit.NormalizationThreshold,
                    NormalizationReason = planningUnit.NormalizationReason,
                    Status = planningUnit.Status,
                    Reason = planningUnit.Reason
                };

                foreach (var unit in planningUnit.Units.OrderBy(static unit => unit.DimensionId))
                {
                    cluster.DimensionIds.Add(unit.DimensionId);
                    cluster.Members.Add(new DimensionArrangementDebugAlignmentClusterMemberInfo
                    {
                        DimensionId = unit.DimensionId,
                        CurrentDistance = unit.Distance,
                        NormalizationDelta = unit.NormalizationDelta
                    });
                }

                info.AlignmentClusters.Add(cluster);
            }

            result.Stacks.Add(info);
        }

        foreach (var analysis in spacing)
        {
            var info = new DimensionArrangementDebugSpacingInfo
            {
                ViewId = analysis.ViewId,
                ViewType = analysis.ViewType,
                DimensionType = analysis.DimensionType,
                Orientation = analysis.Orientation,
                DirectionX = analysis.DirectionX,
                DirectionY = analysis.DirectionY,
                TopDirection = analysis.TopDirection,
                ReferenceLine = CopyLine(analysis.ReferenceLine),
                HasOverlaps = analysis.HasOverlaps,
                MinimumDistance = analysis.MinimumDistance
            };

            foreach (var spacingPair in analysis.Pairs)
            {
                info.Pairs.Add(new DimensionArrangementDebugSpacingPair
                {
                    FirstDimensionId = spacingPair.FirstDimensionId,
                    SecondDimensionId = spacingPair.SecondDimensionId,
                    Distance = spacingPair.Distance,
                    IsOverlap = spacingPair.IsOverlap
                });
            }

            result.Spacing.Add(info);
        }

        foreach (var pair in stacks.Zip(plans, (stack, plan) => (stack, plan)))
        {
            var stack = pair.stack;
            var plan = pair.plan;
            var info = new DimensionArrangementDebugPlanInfo
            {
                ViewId = plan.ViewId,
                ViewType = plan.ViewType,
                DimensionType = ResolveStackDimensionType(stack),
                Orientation = plan.Orientation,
                DirectionX = stack.Direction?.X,
                DirectionY = stack.Direction?.Y,
                TopDirection = stack.TopDirection,
                ReferenceLine = CopyLine(stack.Groups.FirstOrDefault(static group => group.ReferenceLine != null)?.ReferenceLine),
                TargetGapPaper = plan.TargetGapPaper,
                TargetGapDrawing = plan.TargetGapDrawing,
                ProposalCount = plan.Proposals.Count,
                HasApplicableChanges = plan.HasApplicableChanges
            };

            foreach (var proposal in plan.Proposals)
            {
                info.Proposals.Add(new DimensionArrangementDebugProposal
                {
                    DimensionId = proposal.DimensionId,
                    CurrentDistance = proposal.CurrentDistance,
                    AxisShift = proposal.AxisShift,
                    NormalizationDelta = proposal.NormalizationDelta,
                    SpacingDelta = proposal.SpacingDelta,
                    DistanceDelta = proposal.DistanceDelta,
                    TargetDistance = proposal.TargetDistance,
                    CanApply = proposal.CanApply,
                    Reason = proposal.Reason
                });
            }

            result.Plans.Add(info);
        }

        return result;
    }

    private static DrawingLineInfo? CopyLine(DrawingLineInfo? line)
    {
        if (line == null)
            return null;

        return CreateLineInfo(line.StartX, line.StartY, line.EndX, line.EndY);
    }

    private static string ResolveStackDimensionType(DimensionGroupLineStack stack)
    {
        var types = stack.Groups
            .Select(static group => group.DimensionType)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(System.StringComparer.Ordinal)
            .ToList();

        return types.Count == 1 ? types[0] : string.Empty;
    }

    public ArrangeDimensionsResult ArrangeDimensions(int? viewId, double targetGap)
    {
        if (targetGap < 0)
            throw new System.ArgumentOutOfRangeException(nameof(targetGap), "targetGap must be >= 0.");

        return ApplyDimensionDistanceAdjustments(viewId, targetGap);
    }

    internal List<DimensionGroupSpacingAnalysis> AnalyzeDimensionGroupSpacing(int? viewId)
    {
        return DimensionGroupSpacingAnalyzer.AnalyzeStacks(GetArrangeGroupsDeduped(viewId));
    }

    internal List<DimensionGroupArrangementPlan> PlanDimensionGroupSpacing(int? viewId, double targetGap)
    {
        return DimensionGroupSpacingAnalyzer.BuildStacks(GetArrangeGroupsDeduped(viewId))
            .Select(stack => DimensionGroupArrangementPlanner.BuildPlan(stack, targetGap))
            .ToList();
    }

    internal List<DimensionDistanceAdjustmentPlan> PlanDimensionDistanceAdjustments(int? viewId, double targetGap)
    {
        return DimensionGroupSpacingAnalyzer.BuildStacks(GetArrangeGroupsDeduped(viewId))
            .Select(stack =>
            {
                var axisPlan = DimensionGroupArrangementPlanner.BuildPlan(stack, targetGap);
                return DimensionDistanceAdjustmentTranslator.BuildPlan(stack, axisPlan);
            })
            .ToList();
    }

    private List<DimensionGroup> GetArrangeGroups(int? viewId)
    {
        var noReductionPolicy = new DimensionReductionPolicy
        {
            EnableCoverageReduction = false,
            EnableEquivalentSimpleReduction = false,
            EnableRepresentativeSelection = false
        };
        return DimensionGroupFactory.BuildGroups(
            ProjectDimensionSnapshotsToReadModels(GetDimensionSnapshots(viewId)),
            reductionPolicy: noReductionPolicy);
    }

    private List<DimensionGroup> GetArrangeGroupsDeduped(int? viewId)
        => DimensionArrangementDedup.Reduce(GetArrangeGroups(viewId));

    internal ArrangeDimensionsResult ApplyDimensionDistanceAdjustments(int? viewId, double targetGap)
    {
        var activeDrawing = new DrawingHandler().GetActiveDrawing();
        if (activeDrawing == null)
            throw new DrawingNotOpenException();

        var plans = PlanDimensionDistanceAdjustments(viewId, targetGap);
        return ApplyDimensionDistanceAdjustments(activeDrawing, plans, "(MCP) ArrangeDimensions", "(MCP) RollbackArrangeDimensions");
    }

    internal DimensionArrangeHandoffResult TryApplyLocalArrangeHandoff(
        Tekla.Structures.Drawing.Drawing activeDrawing,
        int viewId,
        int anchorDimensionId,
        double targetGap = DefaultArrangeTargetGapPaper)
    {
        var stacks = DimensionGroupSpacingAnalyzer.BuildStacks(GetArrangeGroupsDeduped(viewId));
        var stack = stacks.FirstOrDefault(candidate => candidate.Groups.Any(group => group.Members.Any(member => member.DimensionId == anchorDimensionId)));
        if (stack == null)
        {
            return new DimensionArrangeHandoffResult
            {
                Reason = "local_stack_not_found"
            };
        }

        var axisPlan = DimensionGroupArrangementPlanner.BuildPlan(stack, targetGap);
        var plan = DimensionDistanceAdjustmentTranslator.BuildPlan(stack, axisPlan);
        var applicableProposals = plan.Proposals
            .Where(static proposal => proposal.CanApply && System.Math.Abs(proposal.DistanceDelta) >= 1e-9)
            .ToList();
        if (applicableProposals.Count == 0)
        {
            var reason = plan.Proposals
                .Select(static proposal => proposal.Reason)
                .FirstOrDefault(static reason => !string.IsNullOrWhiteSpace(reason))
                ?? "arrange_handoff_no_changes";

            return new DimensionArrangeHandoffResult
            {
                Reason = reason
            };
        }

        var arrangeResult = ApplyDimensionDistanceAdjustments(
            activeDrawing,
            [plan],
            "(MCP) CombineDimensionsArrangeHandoff",
            "(MCP) RollbackCombineDimensionsArrangeHandoff");
        if (arrangeResult.AppliedCount == 0)
        {
            var reason = arrangeResult.SkipReasons.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))
                ?? "arrange_handoff_no_changes";
            return new DimensionArrangeHandoffResult
            {
                Reason = reason
            };
        }

        var result = new DimensionArrangeHandoffResult
        {
            Attempted = true,
            Succeeded = true
        };
        result.AppliedDimensionIds.AddRange(arrangeResult.Applied.Select(static item => item.DimensionId));
        return result;
    }

    private static ArrangeDimensionsResult ApplyDimensionDistanceAdjustments(
        Tekla.Structures.Drawing.Drawing activeDrawing,
        IReadOnlyList<DimensionDistanceAdjustmentPlan> plans,
        string commitMessage,
        string rollbackCommitMessage)
    {
        var result = new ArrangeDimensionsResult();

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

                activeDrawing.CommitChanges(commitMessage);
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
                        activeDrawing.CommitChanges(rollbackCommitMessage);
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
