using System;
using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.DrawingInternal;
using DrawingView = Tekla.Structures.Drawing.View;

namespace TeklaMcpServer.Api.Drawing.ViewLayout;

internal static class ProjectionAlignmentMoveHelper
{
    public static Dictionary<int, (double X, double Y)> BuildPositionLookup(
        IReadOnlyList<DrawingView> views,
        IList<ArrangedView>? arrangedViews)
    {
        var dict = new Dictionary<int, (double X, double Y)>();
        if (arrangedViews != null)
        {
            foreach (var a in arrangedViews)
                dict[a.Id] = (a.OriginX, a.OriginY);
        }

        foreach (var v in views)
        {
            var id = v.GetIdentifier().ID;
            if (!dict.ContainsKey(id))
                dict[id] = (v.Origin?.X ?? 0, v.Origin?.Y ?? 0);
        }

        return dict;
    }

    public static ProjectionViewState BuildViewStateFromPos(
        DrawingView view,
        double originX,
        double originY,
        IReadOnlyDictionary<int, (double X, double Y)> frameOffsetsById)
    {
        var scale = view.Attributes.Scale > 0 ? view.Attributes.Scale : 1.0;
        var frameOffsetSheetX = 0.0;
        var frameOffsetSheetY = 0.0;
        if (frameOffsetsById.TryGetValue(view.GetIdentifier().ID, out var frameOffset))
        {
            frameOffsetSheetX = frameOffset.X / scale;
            frameOffsetSheetY = frameOffset.Y / scale;
        }

        return new ProjectionViewState(
            view.GetIdentifier().ID,
            originX,
            originY,
            scale,
            view.Width,
            view.Height,
            frameOffsetSheetX,
            frameOffsetSheetY);
    }

    public static ProjectionViewState BuildViewState(
        DrawingView view,
        IReadOnlyDictionary<int, (double X, double Y)> frameOffsetsById)
    {
        var scale = view.Attributes.Scale > 0 ? view.Attributes.Scale : 1.0;
        var frameOffsetSheetX = 0.0;
        var frameOffsetSheetY = 0.0;
        if (frameOffsetsById.TryGetValue(view.GetIdentifier().ID, out var frameOffset))
        {
            frameOffsetSheetX = frameOffset.X / scale;
            frameOffsetSheetY = frameOffset.Y / scale;
        }

        var originX = view.Origin?.X ?? 0;
        var originY = view.Origin?.Y ?? 0;
        if (DrawingViewFrameGeometry.TryGetCenter(view, out var centerX, out var centerY))
        {
            originX = centerX - frameOffsetSheetX;
            originY = centerY - frameOffsetSheetY;
        }

        return new ProjectionViewState(
            view.GetIdentifier().ID,
            originX,
            originY,
            scale,
            view.Width,
            view.Height,
            frameOffsetSheetX,
            frameOffsetSheetY);
    }

    public static bool CanMoveView(
        DrawingView view,
        double dx,
        double dy,
        IReadOnlyDictionary<int, (double X, double Y)> frameOffsetsById,
        double sheetWidth,
        double sheetHeight,
        double margin,
        IReadOnlyList<ReservedRect> reservedAreas,
        double? knownOriginX,
        double? knownOriginY,
        double boundsMarginOverride,
        IReadOnlyList<ProjectionViewState>? otherViewStates,
        out ProjectionMoveRejectDecision rejectDecision,
        out ProjectionViewState state)
    {
        state = knownOriginX.HasValue
            ? BuildViewStateFromPos(view, knownOriginX.Value, knownOriginY ?? (view.Origin?.Y ?? 0), frameOffsetsById)
            : BuildViewState(view, frameOffsetsById);

        if (Math.Abs(dx) < 0.01 && Math.Abs(dy) < 0.01)
        {
            rejectDecision = default;
            return true;
        }

        var effectiveMargin = double.IsNaN(boundsMarginOverride) ? margin : boundsMarginOverride;
        var candidateState = DrawingProjectionAlignmentMath.TranslateOrigin(state, dx, dy);
        var candidateRect = DrawingProjectionAlignmentMath.GetFrameRect(candidateState);
        var candidateReservedRect = ViewPlacementGeometryService.FromProjectionRect(candidateRect);
        var otherViewRects = otherViewStates?
            .ToDictionary(
                otherState => otherState.ViewId,
                otherState => ViewPlacementGeometryService.FromProjectionRect(DrawingProjectionAlignmentMath.GetFrameRect(otherState)));

        var validation = ViewPlacementValidator.Validate(
            candidateReservedRect,
            effectiveMargin,
            sheetWidth - effectiveMargin,
            effectiveMargin,
            sheetHeight - effectiveMargin,
            reservedAreas,
            otherViewRects);

        if (validation.Fits)
        {
            rejectDecision = default;
            return true;
        }

        rejectDecision = DrawingProjectionAlignmentService.CreateProjectionMoveRejectDecision(
            "projection-can-move",
            view.GetIdentifier().ID,
            dx,
            dy,
            candidateRect,
            validation);
        return false;
    }

    public static bool TryApplyMove(DrawingView view, double dx, double dy, IList<ArrangedView>? arrangedViews, out string reason)
    {
        var origin = view.Origin;
        if (origin == null)
        {
            reason = $"projection-skip:view-origin-missing:view={view.GetIdentifier().ID}";
            return false;
        }

        origin.X += dx;
        origin.Y += dy;
        view.Origin = origin;
        if (!view.Modify())
        {
            reason = $"projection-skip:modify-failed:view={view.GetIdentifier().ID}";
            return false;
        }

        UpdateArrangedView(arrangedViews, view.GetIdentifier().ID, origin.X, origin.Y);
        reason = string.Empty;
        return true;
    }

    public static void UpdateArrangedView(IList<ArrangedView>? arrangedViews, int viewId, double originX, double originY)
    {
        if (arrangedViews == null)
            return;

        for (var i = 0; i < arrangedViews.Count; i++)
        {
            if (arrangedViews[i].Id != viewId)
                continue;

            arrangedViews[i] = new ArrangedView
            {
                Id = arrangedViews[i].Id,
                ViewType = arrangedViews[i].ViewType,
                OriginX = originX,
                OriginY = originY,
                PreferredPlacementSide = arrangedViews[i].PreferredPlacementSide,
                ActualPlacementSide = arrangedViews[i].ActualPlacementSide,
                PlacementFallbackUsed = arrangedViews[i].PlacementFallbackUsed,
                LayoutMargin = arrangedViews[i].LayoutMargin,
                LayoutGap = arrangedViews[i].LayoutGap
            };
            return;
        }
    }

    public static void RestoreViewOrigin(DrawingView view, double originX, double originY, IList<ArrangedView>? arrangedViews)
    {
        var origin = view.Origin;
        if (origin == null)
            return;

        origin.X = originX;
        origin.Y = originY;
        view.Origin = origin;
        if (view.Modify())
            UpdateArrangedView(arrangedViews, view.GetIdentifier().ID, originX, originY);
    }
}
