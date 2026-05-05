using System;
using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

internal enum DrawingLayoutCandidateApplyExecutionMode
{
    DryRun,
    Apply
}

internal enum DrawingLayoutCandidateApplyExecutionReason
{
    DryRun,
    Applied,
    PlanNotApplicable,
    MissingRuntimeView,
    MissingApplyHandler,
    ApplyFailed
}

internal sealed class DrawingLayoutCandidateApplyExecutionResult
{
    public string CandidateName { get; set; } = string.Empty;

    public DrawingLayoutCandidateApplyExecutionMode Mode { get; set; }

    public DrawingLayoutCandidateApplyExecutionReason Reason { get; set; }

    public bool Success { get; set; }

    public int RequestedMoveCount { get; set; }

    public int AppliedMoveCount { get; set; }

    public int MissingRuntimeViewCount { get; set; }

    public List<int> MissingRuntimeViewIds { get; set; } = new();
}

internal static class DrawingLayoutCandidateApplyExecutionReasonFormatter
{
    public static string ToTraceString(DrawingLayoutCandidateApplyExecutionReason reason)
        => reason switch
        {
            DrawingLayoutCandidateApplyExecutionReason.DryRun => "dry-run",
            DrawingLayoutCandidateApplyExecutionReason.Applied => "applied",
            DrawingLayoutCandidateApplyExecutionReason.PlanNotApplicable => "plan-not-applicable",
            DrawingLayoutCandidateApplyExecutionReason.MissingRuntimeView => "missing-runtime-view",
            DrawingLayoutCandidateApplyExecutionReason.MissingApplyHandler => "missing-apply-handler",
            DrawingLayoutCandidateApplyExecutionReason.ApplyFailed => "apply-failed",
            _ => "unknown"
        };
}

internal sealed class DrawingLayoutCandidateApplyService
{
    public DrawingLayoutCandidateApplyExecutionResult Execute(
        DrawingLayoutCandidateApplyPlan plan,
        IReadOnlyCollection<int> runtimeViewIds,
        DrawingLayoutCandidateApplyExecutionMode mode,
        Func<DrawingLayoutCandidateApplyMove, bool>? applyMove = null)
    {
        if (plan == null)
            throw new ArgumentNullException(nameof(plan));
        if (runtimeViewIds == null)
            throw new ArgumentNullException(nameof(runtimeViewIds));

        var result = new DrawingLayoutCandidateApplyExecutionResult
        {
            CandidateName = plan.CandidateName,
            Mode = mode,
            RequestedMoveCount = plan.Moves.Count
        };

        if (!plan.CanApply)
        {
            result.Reason = DrawingLayoutCandidateApplyExecutionReason.PlanNotApplicable;
            return result;
        }

        var runtimeIds = runtimeViewIds.ToHashSet();
        result.MissingRuntimeViewIds = plan.Moves
            .Select(static move => move.ViewId)
            .Where(viewId => !runtimeIds.Contains(viewId))
            .Distinct()
            .OrderBy(static viewId => viewId)
            .ToList();
        result.MissingRuntimeViewCount = result.MissingRuntimeViewIds.Count;
        if (result.MissingRuntimeViewCount > 0)
        {
            result.Reason = DrawingLayoutCandidateApplyExecutionReason.MissingRuntimeView;
            return result;
        }

        if (mode == DrawingLayoutCandidateApplyExecutionMode.DryRun)
        {
            result.Success = true;
            result.Reason = DrawingLayoutCandidateApplyExecutionReason.DryRun;
            return result;
        }

        if (applyMove == null)
        {
            result.Reason = DrawingLayoutCandidateApplyExecutionReason.MissingApplyHandler;
            return result;
        }

        foreach (var move in plan.Moves)
        {
            if (!applyMove(move))
            {
                result.Reason = DrawingLayoutCandidateApplyExecutionReason.ApplyFailed;
                return result;
            }

            result.AppliedMoveCount++;
        }

        result.Success = true;
        result.Reason = DrawingLayoutCandidateApplyExecutionReason.Applied;
        return result;
    }
}
