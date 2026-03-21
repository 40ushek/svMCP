using System;
using System.Collections.Generic;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using TeklaMcpServer.Api.Diagnostics;
using DrawingView = Tekla.Structures.Drawing.View;

namespace TeklaMcpServer.Api.Drawing;

internal sealed partial class DrawingProjectionAlignmentService
{
    private static Dictionary<int, (double X, double Y)> BuildPositionLookup(
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

    private static ProjectionViewState BuildViewStateFromPos(
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

        var origin = view.Origin;
        if (origin == null)
        {
            TraceSkip(result, $"projection-skip:view-origin-missing:view={view.GetIdentifier().ID}");
            return false;
        }

        origin.X += dx;
        origin.Y += dy;
        view.Origin = origin;
        if (!view.Modify())
        {
            TraceSkip(result, $"projection-skip:modify-failed:view={view.GetIdentifier().ID}");
            return false;
        }

        UpdateArrangedView(arrangedViews, view.GetIdentifier().ID, origin.X, origin.Y);
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

        var effectiveMargin = double.IsNaN(boundsMarginOverride) ? margin : boundsMarginOverride;
        var state = knownOriginX.HasValue
            ? BuildViewStateFromPos(view, knownOriginX.Value, knownOriginY ?? (view.Origin?.Y ?? 0), frameOffsetsById)
            : BuildViewState(view, frameOffsetsById);
        var candidateState = DrawingProjectionAlignmentMath.TranslateOrigin(state, dx, dy);
        var candidateRect = DrawingProjectionAlignmentMath.GetFrameRect(candidateState);

        if (!DrawingProjectionAlignmentMath.IsWithinUsableArea(candidateRect, effectiveMargin, sheetWidth, sheetHeight))
        {
            if (result != null)
                TraceSkip(result, $"projection-skip:out-of-bounds:view={view.GetIdentifier().ID}:rect=[{candidateRect.MinX:F1},{candidateRect.MinY:F1},{candidateRect.MaxX:F1},{candidateRect.MaxY:F1}]:sheet={sheetWidth}x{sheetHeight}:margin={margin}:scale={state.Scale}:w={state.Width:F1}:h={state.Height:F1}:offX={state.FrameOffsetSheetX:F2}:offY={state.FrameOffsetSheetY:F2}");
            return false;
        }

        if (DrawingProjectionAlignmentMath.IntersectsAnyReserved(candidateRect, reservedAreas))
        {
            if (result != null)
                TraceSkip(result, $"projection-skip:reserved-overlap:view={view.GetIdentifier().ID}");
            return false;
        }

        if (DrawingProjectionAlignmentMath.IntersectsAnyView(candidateRect, otherViewStates))
        {
            if (result != null)
                TraceSkip(result, $"projection-skip:view-overlap:view={view.GetIdentifier().ID}");
            return false;
        }

        return true;
    }

    private static ProjectionViewState BuildViewState(
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

        return new ProjectionViewState(
            view.GetIdentifier().ID,
            view.Origin?.X ?? 0,
            view.Origin?.Y ?? 0,
            scale,
            view.Width,
            view.Height,
            frameOffsetSheetX,
            frameOffsetSheetY);
    }

    private static void UpdateArrangedView(IList<ArrangedView>? arrangedViews, int viewId, double originX, double originY)
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
                PlacementFallbackUsed = arrangedViews[i].PlacementFallbackUsed
            };
            return;
        }
    }

    private static void RestoreViewOrigin(DrawingView view, double originX, double originY, IList<ArrangedView>? arrangedViews)
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

    private static void TraceSkip(ProjectionAlignmentResult result, string reason)
    {
        result.SkippedMoves++;
        result.Diagnostics.Add(reason);
        PerfTrace.Write("api-view", "fit_views_projection_skip", 0, reason);
    }
}
