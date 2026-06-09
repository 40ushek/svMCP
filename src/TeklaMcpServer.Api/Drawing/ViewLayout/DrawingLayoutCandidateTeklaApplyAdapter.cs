using System;
using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.Drawing;
using Tekla.Structures.Geometry3d;
using TeklaMcpServer.Api.Drawing;

namespace TeklaMcpServer.Api.Drawing.ViewLayout;

internal sealed class DrawingLayoutCandidateTeklaApplyAdapter
{
    private readonly DrawingLayoutCandidateApplyService applyService;

    public DrawingLayoutCandidateTeklaApplyAdapter()
        : this(new DrawingLayoutCandidateApplyService())
    {
    }

    public DrawingLayoutCandidateTeklaApplyAdapter(DrawingLayoutCandidateApplyService applyService)
    {
        this.applyService = applyService ?? throw new ArgumentNullException(nameof(applyService));
    }

    public DrawingLayoutCandidateApplyExecutionResult Execute(
        DrawingLayoutCandidateApplyPlan plan,
        IReadOnlyDictionary<int, View> runtimeViewsById,
        DrawingLayoutCandidateApplyExecutionMode mode,
        Tekla.Structures.Drawing.Drawing? drawing = null)
    {
        if (plan == null)
            throw new ArgumentNullException(nameof(plan));
        if (runtimeViewsById == null)
            throw new ArgumentNullException(nameof(runtimeViewsById));

        if (mode == DrawingLayoutCandidateApplyExecutionMode.Apply
            && plan.CanApply
            && drawing != null)
        {
            // Pre-validate: all move targets must exist before any scale commit.
            var runtimeIds = runtimeViewsById.Keys.ToHashSet();
            var missingIds = plan.Moves
                .Select(static m => m.ViewId)
                .Where(id => !runtimeIds.Contains(id))
                .Distinct()
                .OrderBy(static id => id)
                .ToList();
            if (missingIds.Count > 0)
            {
                return new DrawingLayoutCandidateApplyExecutionResult
                {
                    CandidateName = plan.CandidateName,
                    Mode = mode,
                    RequestedMoveCount = plan.Moves.Count,
                    Reason = DrawingLayoutCandidateApplyExecutionReason.MissingRuntimeView,
                    MissingRuntimeViewCount = missingIds.Count,
                    MissingRuntimeViewIds = missingIds
                };
            }

            // Phase 1: commit scale changes before origins so Tekla does not recompute
            // the origin as a side-effect of the scale change when both are set in one Modify().
            var anyScaleChanged = false;
            foreach (var move in plan.Moves)
            {
                if (!runtimeViewsById.TryGetValue(move.ViewId, out var scaleView)) continue;
                if (move.Scale <= 0 || Math.Abs(scaleView.Attributes.Scale - move.Scale) < DrawingLayoutCandidateApplyTolerances.Scale) continue;
                scaleView.Attributes.Scale = move.Scale;
                if (!scaleView.Modify())
                {
                    return new DrawingLayoutCandidateApplyExecutionResult
                    {
                        CandidateName = plan.CandidateName,
                        Mode = mode,
                        RequestedMoveCount = plan.Moves.Count,
                        Reason = DrawingLayoutCandidateApplyExecutionReason.ApplyFailed
                    };
                }
                anyScaleChanged = true;
            }
            if (anyScaleChanged)
                drawing.CommitChanges();
        }

        return applyService.Execute(
            plan,
            runtimeViewsById.Keys.ToList(),
            mode,
            mode == DrawingLayoutCandidateApplyExecutionMode.Apply
                ? move => ApplyMove(runtimeViewsById[move.ViewId], move)
                : null);
    }

    private static bool ApplyMove(View view, DrawingLayoutCandidateApplyMove move)
    {
        if (view == null)
            return false;

        var origin = view.Origin ?? new Point();
        origin.X = move.TargetOriginX;
        origin.Y = move.TargetOriginY;
        view.Origin = origin;

        if (move.Scale > 0 && Math.Abs(view.Attributes.Scale - move.Scale) >= DrawingLayoutCandidateApplyTolerances.Scale)
            view.Attributes.Scale = move.Scale;

        return view.Modify();
    }
}
