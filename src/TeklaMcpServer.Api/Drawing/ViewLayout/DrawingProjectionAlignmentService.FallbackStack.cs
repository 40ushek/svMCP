using System;
using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.DrawingInternal;
using TeklaMcpServer.Api.Diagnostics;
using DrawingView = Tekla.Structures.Drawing.View;

namespace TeklaMcpServer.Api.Drawing.ViewLayout;

internal sealed partial class DrawingProjectionAlignmentService
{
    private void ApplyFallbackStackAlignment(
        ProjectionAlignmentResult result,
        IReadOnlyList<DrawingView> views,
        IReadOnlyDictionary<int, (double X, double Y)> frameOffsetsById,
        double sheetWidth,
        double sheetHeight,
        double margin,
        IReadOnlyList<ReservedRect> reservedAreas,
        IList<ArrangedView>? arrangedViews)
    {
        if (arrangedViews == null || arrangedViews.Count == 0)
            return;

        var viewsById = views.ToDictionary(view => view.GetIdentifier().ID);
        var posById = ProjectionAlignmentMoveHelper.BuildPositionLookup(views, arrangedViews);
        var statesById = views
            .Select(view =>
            {
                var id = view.GetIdentifier().ID;
                posById.TryGetValue(id, out var pos);
                return ProjectionAlignmentMoveHelper.BuildViewStateFromPos(view, pos.X, pos.Y, frameOffsetsById);
            })
            .ToDictionary(state => state.ViewId);

        var groups = arrangedViews
            .Where(view => view.PlacementFallbackUsed)
            .Where(view => !string.IsNullOrWhiteSpace(view.PreferredPlacementSide))
            .Where(view => !string.IsNullOrWhiteSpace(view.ActualPlacementSide))
            .Where(view => viewsById.ContainsKey(view.Id))
            .GroupBy(view => new
            {
                view.PreferredPlacementSide,
                view.ActualPlacementSide,
                view.ViewType
            })
            .Where(group => group.Count() >= 2);

        foreach (var group in groups)
        {
            if (!Enum.TryParse<SectionPlacementSide>(group.Key.PreferredPlacementSide, ignoreCase: true, out var preferredSide)
                || !DrawingProjectionAlignmentMath.TryGetSectionAlignmentAxis(preferredSide, out var alignX))
                continue;

            var stack = group
                .Where(view => statesById.ContainsKey(view.Id))
                .OrderByDescending(view => GetStackAnchorSortKey(statesById[view.Id], preferredSide))
                .ThenBy(view => view.Id)
                .ToList();
            if (stack.Count < 2)
                continue;

            var anchor = stack[0];
            var anchorState = statesById[anchor.Id];
            PerfTrace.Write(
                "api-view",
                "fallback_stack_alignment_group",
                0,
                $"preferred={group.Key.PreferredPlacementSide} actual={group.Key.ActualPlacementSide} viewType={group.Key.ViewType} anchor={anchor.Id} views={string.Join(",", stack.Select(view => view.Id))}");

            foreach (var target in stack.Skip(1))
            {
                var targetState = statesById[target.Id];
                var dx = alignX ? anchorState.FrameCenterX - targetState.FrameCenterX : 0.0;
                var dy = alignX ? 0.0 : anchorState.FrameCenterY - targetState.FrameCenterY;
                var candidateState = DrawingProjectionAlignmentMath.TranslateOrigin(targetState, dx, dy);
                var candidateRect = DrawingProjectionAlignmentMath.GetFrameRect(candidateState);
                PerfTrace.Write(
                    "api-view",
                    "fallback_stack_alignment_attempt",
                    0,
                    $"view={target.Id} anchor={anchor.Id} axis={(alignX ? "X" : "Y")} delta=({dx:F2},{dy:F2}) candidate=[{candidateRect.MinX:F1},{candidateRect.MinY:F1},{candidateRect.MaxX:F1},{candidateRect.MaxY:F1}]");

                if (Math.Abs(dx) < MoveEpsilon && Math.Abs(dy) < MoveEpsilon)
                {
                    PerfTrace.Write(
                        "api-view",
                        "fallback_stack_alignment_result",
                        0,
                        $"view={target.Id} anchor={anchor.Id} result=skipped reason=already-aligned");
                    continue;
                }

                if (!viewsById.TryGetValue(target.Id, out var targetView))
                    continue;

                var otherStates = statesById.Values
                    .Where(state => state.ViewId != target.Id)
                    .ToList();
                if (!ProjectionAlignmentMoveHelper.CanMoveView(
                        targetView,
                        dx,
                        dy,
                        frameOffsetsById,
                        sheetWidth,
                        sheetHeight,
                        margin,
                        reservedAreas,
                        targetState.OriginX,
                        targetState.OriginY,
                        boundsMarginOverride: double.NaN,
                        otherViewStates: otherStates,
                        out var rejectDecision,
                        out var validationState))
                {
                    TraceProjectionMoveReject(result, rejectDecision, sheetWidth, sheetHeight, margin, validationState);
                    PerfTrace.Write(
                        "api-view",
                        "fallback_stack_alignment_result",
                        0,
                        $"view={target.Id} anchor={anchor.Id} result=rejected reason={rejectDecision.Reason}");
                    continue;
                }

                if (!ProjectionAlignmentMoveHelper.TryApplyMove(targetView, dx, dy, arrangedViews, out var reason))
                {
                    TraceSkip(result, reason);
                    PerfTrace.Write(
                        "api-view",
                        "fallback_stack_alignment_result",
                        0,
                        $"view={target.Id} anchor={anchor.Id} result=rejected reason={reason}");
                    continue;
                }

                result.AppliedMoves++;
                posById[target.Id] = (targetState.OriginX + dx, targetState.OriginY + dy);
                statesById[target.Id] = ProjectionAlignmentMoveHelper.BuildViewStateFromPos(
                    targetView,
                    targetState.OriginX + dx,
                    targetState.OriginY + dy,
                    frameOffsetsById);
                PerfTrace.Write(
                    "api-view",
                    "fallback_stack_alignment_result",
                    0,
                    $"view={target.Id} anchor={anchor.Id} result=applied");
            }
        }
    }

    private static double GetStackAnchorSortKey(ProjectionViewState state, SectionPlacementSide preferredSide)
    {
        var rect = DrawingProjectionAlignmentMath.GetFrameRect(state);
        return preferredSide switch
        {
            SectionPlacementSide.Top => rect.MaxY,
            SectionPlacementSide.Bottom => -rect.MinY,
            SectionPlacementSide.Left => -rect.MinX,
            SectionPlacementSide.Right => rect.MaxX,
            _ => rect.MaxY
        };
    }
}
