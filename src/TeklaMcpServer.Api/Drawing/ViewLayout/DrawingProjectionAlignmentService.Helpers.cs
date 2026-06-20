using System;
using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using TeklaMcpServer.Api.Diagnostics;
using DrawingView = Tekla.Structures.Drawing.View;
using TeklaMcpServer.Api.Drawing;

namespace TeklaMcpServer.Api.Drawing.ViewLayout;

internal sealed partial class DrawingProjectionAlignmentService
{
    internal static ProjectionMoveRejectDecision CreateProjectionMoveRejectDecision(
        string stage,
        int viewId,
        double dx,
        double dy,
        ProjectionRect candidateRect,
        ViewPlacementValidationResult validation)
        => new(stage, viewId, dx, dy, candidateRect, validation.Reason, validation.Blockers);

    internal static string FormatProjectionMoveRejectDecision(ProjectionMoveRejectDecision decision)
    {
        var blockers = decision.Blockers.Select(blocker =>
            blocker.ViewId.HasValue
                ? $"{blocker.ViewId.Value}:[{blocker.Rect.MinX:F1},{blocker.Rect.MinY:F1},{blocker.Rect.MaxX:F1},{blocker.Rect.MaxY:F1}]"
                : $"[{blocker.Rect.MinX:F1},{blocker.Rect.MinY:F1},{blocker.Rect.MaxX:F1},{blocker.Rect.MaxY:F1}]");

        return string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "stage={0} view={1} reason={2} delta=({3:F2},{4:F2}) candidate=[{5:F1},{6:F1},{7:F1},{8:F1}] blockers={9}",
            decision.Stage,
            decision.ViewId,
            string.IsNullOrWhiteSpace(decision.Reason) ? "unknown" : decision.Reason,
            decision.Dx,
            decision.Dy,
            decision.CandidateRect.MinX,
            decision.CandidateRect.MinY,
            decision.CandidateRect.MaxX,
            decision.CandidateRect.MaxY,
            string.Join(";", blockers));
    }

    private static string FormatProjectionSkipReason(
        ProjectionMoveRejectDecision decision,
        double sheetWidth,
        double sheetHeight,
        double margin,
        ProjectionViewState state)
    {
        return decision.Reason == "out-of-bounds"
            ? string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "projection-skip:out-of-bounds:view={0}:rect=[{1:F1},{2:F1},{3:F1},{4:F1}]:sheet={5}x{6}:margin={7}:scale={8}:w={9:F1}:h={10:F1}:offX={11:F2}:offY={12:F2}",
                decision.ViewId,
                decision.CandidateRect.MinX,
                decision.CandidateRect.MinY,
                decision.CandidateRect.MaxX,
                decision.CandidateRect.MaxY,
                sheetWidth,
                sheetHeight,
                margin,
                state.Scale,
                state.Width,
                state.Height,
                state.FrameOffsetSheetX,
                state.FrameOffsetSheetY)
            : $"projection-skip:{decision.Reason}:view={decision.ViewId}";
    }

    internal static void Log(string message)
        => PerfTrace.Write("api-view", "layout_internal", 0, message);

    private static void TraceProjectionMoveReject(
        ProjectionAlignmentResult? result,
        ProjectionMoveRejectDecision decision,
        double sheetWidth,
        double sheetHeight,
        double margin,
        ProjectionViewState state)
    {
        if (result == null)
            return;

        result.RecordValidatorReject(decision.Reason);
        PerfTrace.Write("api-view", "projection_move_reject", 0, FormatProjectionMoveRejectDecision(decision));
        TraceSkip(result, FormatProjectionSkipReason(decision, sheetWidth, sheetHeight, margin, state));
    }

    private static Dictionary<int, (double X, double Y)> BuildPositionLookup(
        IReadOnlyList<DrawingView> views,
        IList<ArrangedView>? arrangedViews)
        => ProjectionAlignmentMoveHelper.BuildPositionLookup(views, arrangedViews);

    private static ProjectionViewState BuildViewStateFromPos(
        DrawingView view,
        double originX,
        double originY,
        IReadOnlyDictionary<int, (double X, double Y)> frameOffsetsById)
        => ProjectionAlignmentMoveHelper.BuildViewStateFromPos(view, originX, originY, frameOffsetsById);

    private bool TryGetPartAnchorSheet(DrawingView view, int modelId, double originX, double originY, out double anchorX, out double anchorY, out string reason)
    {
        anchorX = 0;
        anchorY = 0;

        var geometry = _partGeometryApi.GetPartGeometryInView(view.GetIdentifier().ID, modelId);
        if (!geometry.Success)
        {
            reason = $"projection-skip:part-geometry-failed:view={view.GetIdentifier().ID}:model={modelId}";
            return false;
        }

        var anchor = geometry.CoordinateSystemOrigin.Length >= 2
            ? geometry.CoordinateSystemOrigin
            : geometry.StartPoint;
        if (anchor.Length < 2)
        {
            reason = $"projection-skip:part-anchor-missing:view={view.GetIdentifier().ID}:model={modelId}";
            return false;
        }

        var state = new ProjectionViewState(
            view.GetIdentifier().ID,
            originX,
            originY,
            view.Attributes.Scale > 0 ? view.Attributes.Scale : 1.0,
            view.Width,
            view.Height,
            0,
            0);
        var sheet = DrawingProjectionAlignmentMath.LocalToSheet(state, anchor[0], anchor[1]);
        anchorX = sheet.X;
        anchorY = sheet.Y;
        reason = string.Empty;
        return true;
    }

    private bool TryMoveView(
        ProjectionAlignmentResult result,
        DrawingView view,
        double dx,
        double dy,
        IReadOnlyDictionary<int, (double X, double Y)> frameOffsetsById,
        double sheetWidth,
        double sheetHeight,
        double margin,
        IReadOnlyList<ReservedRect> reservedAreas,
        IList<ArrangedView>? arrangedViews,
        double? knownOriginX = null,
        double? knownOriginY = null,
        double boundsMarginOverride = double.NaN,
        IReadOnlyList<ProjectionViewState>? otherViewStates = null)
    {
        if (!CanMoveView(
                result,
                view,
                dx,
                dy,
                frameOffsetsById,
                sheetWidth,
                sheetHeight,
                margin,
                reservedAreas,
                knownOriginX,
                knownOriginY,
                boundsMarginOverride,
                otherViewStates))
            return false;

        if (!ProjectionAlignmentMoveHelper.TryApplyMove(view, dx, dy, arrangedViews, out var reason))
        {
            TraceSkip(result, reason);
            return false;
        }

        result.AppliedMoves++;
        return true;
    }

    private bool CanMoveView(
        ProjectionAlignmentResult? result,
        DrawingView view,
        double dx,
        double dy,
        IReadOnlyDictionary<int, (double X, double Y)> frameOffsetsById,
        double sheetWidth,
        double sheetHeight,
        double margin,
        IReadOnlyList<ReservedRect> reservedAreas,
        double? knownOriginX = null,
        double? knownOriginY = null,
        double boundsMarginOverride = double.NaN,
        IReadOnlyList<ProjectionViewState>? otherViewStates = null)
    {
        if (Math.Abs(dx) < MoveEpsilon && Math.Abs(dy) < MoveEpsilon)
            return true;

        if (!ProjectionAlignmentMoveHelper.CanMoveView(
                view,
                dx,
                dy,
                frameOffsetsById,
                sheetWidth,
                sheetHeight,
                margin,
                reservedAreas,
                knownOriginX,
                knownOriginY,
                boundsMarginOverride,
                otherViewStates,
                out var decision,
                out var state))
        {
            TraceProjectionMoveReject(result, decision, sheetWidth, sheetHeight, margin, state);
            return false;
        }

        return true;
    }

    private static ProjectionViewState BuildViewState(
        DrawingView view,
        IReadOnlyDictionary<int, (double X, double Y)> frameOffsetsById)
        => ProjectionAlignmentMoveHelper.BuildViewState(view, frameOffsetsById);

    private static void UpdateArrangedView(IList<ArrangedView>? arrangedViews, int viewId, double originX, double originY)
        => ProjectionAlignmentMoveHelper.UpdateArrangedView(arrangedViews, viewId, originX, originY);

    private static void RestoreViewOrigin(DrawingView view, double originX, double originY, IList<ArrangedView>? arrangedViews)
        => ProjectionAlignmentMoveHelper.RestoreViewOrigin(view, originX, originY, arrangedViews);

    private static void TraceSkip(ProjectionAlignmentResult result, string reason)
    {
        result.SkippedMoves++;
        result.Diagnostics.Add(reason);
        PerfTrace.Write("api-view", "fit_views_projection_skip", 0, reason);
    }
}

