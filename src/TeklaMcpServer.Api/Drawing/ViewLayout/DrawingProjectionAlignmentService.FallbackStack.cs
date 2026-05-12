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
        ViewTopologyGraph topology,
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
        var sectionRelationTargets = topology.SemanticViews.Sections
            .Concat(topology.SemanticViews.Details.Where(view => view.ViewType == DrawingView.ViewTypes.SectionView))
            .ToList();
        var sectionRelations = DetailRelationResolver.BuildSectionMarkRelations(views, sectionRelationTargets);

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
            if (!Enum.TryParse<SectionPlacementSide>(group.Key.ActualPlacementSide, ignoreCase: true, out var actualSide))
                actualSide = SectionPlacementSide.Unknown;

            var stack = group
                .Where(view => statesById.ContainsKey(view.Id))
                .OrderBy(view => GetCurrentStackOrderKey(statesById[view.Id], actualSide))
                .ThenBy(view => view.Id)
                .ToList();
            if (stack.Count < 2)
                continue;

            stack = OrderFallbackStack(stack, statesById, sectionRelations, actualSide);
            var anchor = stack[0];
            PerfTrace.Write(
                "api-view",
                "fallback_stack_alignment_group",
                0,
                $"preferred={group.Key.PreferredPlacementSide} actual={group.Key.ActualPlacementSide} viewType={group.Key.ViewType} anchor={anchor.Id} views={string.Join(",", stack.Select(view => view.Id))}");

            if (TryApplyOrderedFallbackStack(
                    result,
                    stack,
                    viewsById,
                    statesById,
                    frameOffsetsById,
                    sheetWidth,
                    sheetHeight,
                    margin,
                    reservedAreas,
                    arrangedViews,
                    actualSide,
                    alignX))
                continue;

            var anchorState = statesById[anchor.Id];
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

    private bool TryApplyOrderedFallbackStack(
        ProjectionAlignmentResult result,
        IReadOnlyList<ArrangedView> stack,
        IReadOnlyDictionary<int, DrawingView> viewsById,
        Dictionary<int, ProjectionViewState> statesById,
        IReadOnlyDictionary<int, (double X, double Y)> frameOffsetsById,
        double sheetWidth,
        double sheetHeight,
        double margin,
        IReadOnlyList<ReservedRect> reservedAreas,
        IList<ArrangedView>? arrangedViews,
        SectionPlacementSide actualSide,
        bool alignX)
    {
        if (!TryGetStackOrientation(actualSide, out var verticalStack))
            return false;

        if ((verticalStack && !alignX) || (!verticalStack && alignX))
        {
            PerfTrace.Write(
                "api-view",
                "fallback_stack_order_result",
                0,
                $"result=skipped reason=alignment-axis-conflicts-with-stack-axis actual={actualSide} alignAxis={(alignX ? "X" : "Y")}");
            return false;
        }

        var anchorState = statesById[stack[0].Id];
        var gap = stack
            .Select(view => view.LayoutGap)
            .Where(value => value > 0)
            .DefaultIfEmpty(ProjectionViewGap)
            .Max();
        var currentRects = stack.ToDictionary(
            view => view.Id,
            view => DrawingProjectionAlignmentMath.GetFrameRect(statesById[view.Id]));
        var plannedStates = statesById.ToDictionary(item => item.Key, item => item.Value);
        var plannedMoves = new List<(ArrangedView Arranged, DrawingView View, double Dx, double Dy, ProjectionViewState State)>();

        if (verticalStack)
        {
            var nextMaxY = currentRects.Values.Max(rect => rect.MaxY);
            foreach (var item in stack)
            {
                if (!viewsById.TryGetValue(item.Id, out var view))
                    return false;

                var state = statesById[item.Id];
                var rect = currentRects[item.Id];
                var height = rect.MaxY - rect.MinY;
                var targetMinY = nextMaxY - height;
                var dx = anchorState.FrameCenterX - state.FrameCenterX;
                var dy = targetMinY - rect.MinY;
                var candidateState = DrawingProjectionAlignmentMath.TranslateOrigin(state, dx, dy);
                plannedStates[item.Id] = candidateState;
                plannedMoves.Add((item, view, dx, dy, candidateState));
                nextMaxY = targetMinY - gap;
            }
        }
        else
        {
            var nextMinX = currentRects.Values.Min(rect => rect.MinX);
            foreach (var item in stack)
            {
                if (!viewsById.TryGetValue(item.Id, out var view))
                    return false;

                var state = statesById[item.Id];
                var rect = currentRects[item.Id];
                var width = rect.MaxX - rect.MinX;
                var dx = nextMinX - rect.MinX;
                var dy = anchorState.FrameCenterY - state.FrameCenterY;
                var candidateState = DrawingProjectionAlignmentMath.TranslateOrigin(state, dx, dy);
                plannedStates[item.Id] = candidateState;
                plannedMoves.Add((item, view, dx, dy, candidateState));
                nextMinX += width + gap;
            }
        }

        foreach (var move in plannedMoves)
        {
            var candidateRect = DrawingProjectionAlignmentMath.GetFrameRect(move.State);
            PerfTrace.Write(
                "api-view",
                "fallback_stack_order_attempt",
                0,
                $"view={move.Arranged.Id} axis={(verticalStack ? "Y" : "X")} delta=({move.Dx:F2},{move.Dy:F2}) candidate=[{candidateRect.MinX:F1},{candidateRect.MinY:F1},{candidateRect.MaxX:F1},{candidateRect.MaxY:F1}]");

            var otherStates = plannedStates.Values
                .Where(state => state.ViewId != move.Arranged.Id)
                .ToList();
            if (!ProjectionAlignmentMoveHelper.CanMoveView(
                    move.View,
                    move.Dx,
                    move.Dy,
                    frameOffsetsById,
                    sheetWidth,
                    sheetHeight,
                    margin,
                    reservedAreas,
                    statesById[move.Arranged.Id].OriginX,
                    statesById[move.Arranged.Id].OriginY,
                    boundsMarginOverride: double.NaN,
                    otherViewStates: otherStates,
                    out var rejectDecision,
                    out var validationState))
            {
                TraceProjectionMoveReject(result, rejectDecision, sheetWidth, sheetHeight, margin, validationState);
                PerfTrace.Write(
                    "api-view",
                    "fallback_stack_order_result",
                    0,
                    $"result=rejected view={move.Arranged.Id} reason={rejectDecision.Reason}");
                return false;
            }
        }

        var applied = false;
        foreach (var move in plannedMoves)
        {
            if (Math.Abs(move.Dx) < MoveEpsilon && Math.Abs(move.Dy) < MoveEpsilon)
                continue;

            if (!ProjectionAlignmentMoveHelper.TryApplyMove(move.View, move.Dx, move.Dy, arrangedViews, out var reason))
            {
                TraceSkip(result, reason);
                PerfTrace.Write(
                    "api-view",
                    "fallback_stack_order_result",
                    0,
                    $"result=rejected view={move.Arranged.Id} reason={reason}");
                return false;
            }

            result.AppliedMoves++;
            statesById[move.Arranged.Id] = move.State;
            applied = true;
        }

        PerfTrace.Write(
            "api-view",
            "fallback_stack_order_result",
            0,
            $"result={(applied ? "applied" : "skipped")} reason={(applied ? "ordered" : "already-ordered")} views={string.Join(",", stack.Select(view => view.Id))}");
        return applied;
    }

    private static List<ArrangedView> OrderFallbackStack(
        IReadOnlyList<ArrangedView> stack,
        IReadOnlyDictionary<int, ProjectionViewState> statesById,
        DetailRelationSet sectionRelations,
        SectionPlacementSide actualSide)
    {
        return stack
            .Select(view =>
            {
                var hasSourceKey = TryGetSectionSourceOrderKey(view.Id, sectionRelations, actualSide, out var sourceKey);
                return new
                {
                    View = view,
                    HasSourceKey = hasSourceKey,
                    SourceKey = sourceKey,
                    CurrentKey = GetCurrentStackOrderKey(statesById[view.Id], actualSide)
                };
            })
            .OrderBy(item => item.HasSourceKey ? 0 : 1)
            .ThenBy(item => item.HasSourceKey ? item.SourceKey : item.CurrentKey)
            .ThenBy(item => item.View.Id)
            .Select(item => item.View)
            .ToList();
    }

    private static bool TryGetSectionSourceOrderKey(
        int viewId,
        DetailRelationSet sectionRelations,
        SectionPlacementSide actualSide,
        out double orderKey)
    {
        orderKey = 0;
        if (!sectionRelations.TryGet(viewId, out var relation))
            return false;

        if (actualSide is SectionPlacementSide.Left or SectionPlacementSide.Right)
        {
            if (!relation.AnchorY.HasValue)
                return false;

            orderKey = -relation.AnchorY.Value;
            return true;
        }

        if (actualSide is SectionPlacementSide.Top or SectionPlacementSide.Bottom)
        {
            if (!relation.AnchorX.HasValue)
                return false;

            orderKey = relation.AnchorX.Value;
            return true;
        }

        return false;
    }

    private static bool TryGetStackOrientation(SectionPlacementSide actualSide, out bool verticalStack)
    {
        switch (actualSide)
        {
            case SectionPlacementSide.Left:
            case SectionPlacementSide.Right:
                verticalStack = true;
                return true;
            case SectionPlacementSide.Top:
            case SectionPlacementSide.Bottom:
                verticalStack = false;
                return true;
            default:
                verticalStack = false;
                return false;
        }
    }

    private static double GetCurrentStackOrderKey(ProjectionViewState state, SectionPlacementSide actualSide)
    {
        var rect = DrawingProjectionAlignmentMath.GetFrameRect(state);
        return actualSide switch
        {
            SectionPlacementSide.Left or SectionPlacementSide.Right => -rect.MaxY,
            SectionPlacementSide.Top or SectionPlacementSide.Bottom => rect.MinX,
            _ => -rect.MaxY
        };
    }
}
