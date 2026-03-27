using System;
using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using DrawingView = Tekla.Structures.Drawing.View;

namespace TeklaMcpServer.Api.Drawing;

internal sealed partial class DrawingProjectionAlignmentService
{
    private void ApplyGaAlignment(
        ProjectionAlignmentResult result,
        GADrawing drawing,
        NeighborSet neighbors,
        IReadOnlyList<DrawingView> views,
        IReadOnlyDictionary<int, (double X, double Y)> frameOffsetsById,
        double sheetWidth,
        double sheetHeight,
        double margin,
        IReadOnlyList<ReservedRect> reservedAreas,
        IList<ArrangedView>? arrangedViews,
        IReadOnlyDictionary<int, IReadOnlyList<GridAxisInfo>>? preloadedAxes)
    {
        var baseView = neighbors.BaseView;
        var baseViewId = baseView.GetIdentifier().ID;
        IReadOnlyList<GridAxisInfo> baseAxes;
        if (preloadedAxes != null && preloadedAxes.TryGetValue(baseViewId, out var cached))
        {
            baseAxes = cached;
        }
        else
        {
            var baseAxesResult = _gridApi.GetGridAxes(baseViewId);
            if (!baseAxesResult.Success)
            {
                TraceSkip(result, $"projection-skip:base-grid-read-failed:view={baseViewId}");
                return;
            }

            baseAxes = baseAxesResult.Axes;
        }

        var posById = BuildPositionLookup(views, arrangedViews);
        var allStates = views
            .Select(v => { posById.TryGetValue(v.GetIdentifier().ID, out var p); return BuildViewStateFromPos(v, p.X, p.Y, frameOffsetsById); })
            .ToList();

        foreach (var neighbor in new[]
        {
            (View: neighbors.TopNeighbor, Role: NeighborRole.Top),
            (View: neighbors.BottomNeighbor, Role: NeighborRole.Bottom),
            (View: neighbors.SideNeighborLeft, Role: NeighborRole.SideLeft),
            (View: neighbors.SideNeighborRight, Role: NeighborRole.SideRight)
        })
        {
            if (neighbor.View == null || !DrawingProjectionAlignmentMath.TryGetNeighborAlignmentAxis(neighbor.Role, out var alignNeighborX))
                continue;

            var targetId = neighbor.View.GetIdentifier().ID;
            var others = allStates.Where(s => s.ViewId != targetId).ToList();
            ApplyGaMove(
                result,
                baseView,
                neighbor.View,
                baseAxes,
                requiredDirection: alignNeighborX ? "X" : "Y",
                alignX: alignNeighborX,
                frameOffsetsById,
                sheetWidth,
                sheetHeight,
                margin,
                reservedAreas,
                arrangedViews,
                preloadedAxes,
                posById,
                allStates,
                others);
        }

        foreach (var section in views.Where(v => v.ViewType == DrawingView.ViewTypes.SectionView))
        {
            var sectionId = section.GetIdentifier().ID;
            if (!TryGetSectionAlignmentAxis(drawing, baseView, sectionId, section, result, arrangedViews, out var alignSectionX))
                continue;

            var others = allStates.Where(s => s.ViewId != sectionId).ToList();
            ApplyGaMove(result, baseView, section, baseAxes, requiredDirection: alignSectionX ? "X" : "Y", alignX: alignSectionX, frameOffsetsById, sheetWidth, sheetHeight, margin, reservedAreas, arrangedViews, preloadedAxes, posById, allStates, others);
        }
    }

    private void ApplyGaMove(
        ProjectionAlignmentResult result,
        DrawingView front,
        DrawingView target,
        IReadOnlyList<GridAxisInfo> frontAxes,
        string requiredDirection,
        bool alignX,
        IReadOnlyDictionary<int, (double X, double Y)> frameOffsetsById,
        double sheetWidth,
        double sheetHeight,
        double margin,
        IReadOnlyList<ReservedRect> reservedAreas,
        IList<ArrangedView>? arrangedViews,
        IReadOnlyDictionary<int, IReadOnlyList<GridAxisInfo>>? preloadedAxes,
        Dictionary<int, (double X, double Y)> posById,
        List<ProjectionViewState> allStates,
        IReadOnlyList<ProjectionViewState>? otherViewStates)
    {
        var targetId = target.GetIdentifier().ID;
        IReadOnlyList<GridAxisInfo> targetAxes;
        if (preloadedAxes != null && preloadedAxes.TryGetValue(targetId, out var cached))
        {
            targetAxes = cached;
        }
        else
        {
            var targetAxesResult = _gridApi.GetGridAxes(targetId);
            if (!targetAxesResult.Success)
            {
                TraceSkip(result, $"projection-skip:grid-read-failed:view={targetId}");
                return;
            }

            targetAxes = targetAxesResult.Axes;
        }

        if (!DrawingProjectionAlignmentMath.TrySelectCommonAxis(frontAxes, targetAxes, requiredDirection, out var frontAxis, out var targetAxis))
        {
            TraceSkip(result, $"projection-skip:no-common-axis:view={targetId}:direction={requiredDirection}");
            return;
        }

        var frontId = front.GetIdentifier().ID;
        posById.TryGetValue(frontId, out var frontPos);
        posById.TryGetValue(targetId, out var targetPos);
        var frontState = BuildViewStateFromPos(front, frontPos.X, frontPos.Y, frameOffsetsById);
        var targetState = BuildViewStateFromPos(target, targetPos.X, targetPos.Y, frameOffsetsById);
        var frontCoordinate = DrawingProjectionAlignmentMath.LocalCoordinateToSheet(frontState, frontAxis.Coordinate, requiredDirection);
        var targetCoordinate = DrawingProjectionAlignmentMath.LocalCoordinateToSheet(targetState, targetAxis.Coordinate, requiredDirection);
        var delta = frontCoordinate - targetCoordinate;
        var dx = alignX ? delta : 0.0;
        var dy = alignX ? 0.0 : delta;
        if (TryMoveView(result, target, dx, dy, frameOffsetsById, sheetWidth, sheetHeight, margin, reservedAreas, arrangedViews, targetPos.X, targetPos.Y, boundsMarginOverride: 0, otherViewStates: otherViewStates))
        {
            posById[targetId] = (targetPos.X + dx, targetPos.Y + dy);
            for (var i = 0; i < allStates.Count; i++)
                if (allStates[i].ViewId == targetId)
                {
                    allStates[i] = BuildViewStateFromPos(target, targetPos.X + dx, targetPos.Y + dy, frameOffsetsById);
                    break;
                }
        }
    }

    private void ApplyGaNeighborAlignment(
        ProjectionAlignmentResult result,
        IReadOnlyList<DrawingView> views,
        IReadOnlyDictionary<int, (double X, double Y)> frameOffsetsById,
        double sheetWidth,
        double sheetHeight,
        double margin,
        IReadOnlyList<ReservedRect> reservedAreas,
        IList<ArrangedView>? arrangedViews,
        IReadOnlyDictionary<int, IReadOnlyList<GridAxisInfo>>? preloadedAxes)
    {
        const double RowGroupThreshold = 60.0;
        const double ColGroupThreshold = 80.0;
        const double MaxAllowedMove = 30.0;

        var posById = BuildPositionLookup(views, arrangedViews);
        var axesCache = new Dictionary<int, IReadOnlyList<GridAxisInfo>>();
        foreach (var view in views)
        {
            var id = view.GetIdentifier().ID;
            if (preloadedAxes != null && preloadedAxes.TryGetValue(id, out var cached))
            {
                axesCache[id] = cached;
            }
            else
            {
                var r = _gridApi.GetGridAxes(id);
                if (r.Success)
                    axesCache[id] = r.Axes;
            }
        }

        var frameCenterById = new Dictionary<int, (double X, double Y)>();
        foreach (var v in views)
        {
            var id = v.GetIdentifier().ID;
            if (!posById.TryGetValue(id, out var pos))
                continue;

            var state = BuildViewStateFromPos(v, pos.X, pos.Y, frameOffsetsById);
            frameCenterById[id] = (state.FrameCenterX, state.FrameCenterY);
        }

        var byX = views.OrderBy(v => frameCenterById.TryGetValue(v.GetIdentifier().ID, out var p) ? p.X : 0).ToList();
        for (var i = 0; i < byX.Count; i++)
        {
            var refView = byX[i];
            var refId = refView.GetIdentifier().ID;
            if (!axesCache.TryGetValue(refId, out var refAxes))
                continue;
            if (!posById.TryGetValue(refId, out var refPos))
                continue;
            frameCenterById.TryGetValue(refId, out var refCenter);

            for (var j = i + 1; j < byX.Count; j++)
            {
                var target = byX[j];
                var targetId = target.GetIdentifier().ID;
                if (!axesCache.TryGetValue(targetId, out var targetAxes))
                    continue;
                if (!posById.TryGetValue(targetId, out var targetPos))
                    continue;
                frameCenterById.TryGetValue(targetId, out var targetCenter);

                if (Math.Abs(refCenter.Y - targetCenter.Y) > RowGroupThreshold)
                    continue;

                if (!DrawingProjectionAlignmentMath.TrySelectCommonAxis(refAxes, targetAxes, "Y", out var refAxis, out var targetAxis))
                {
                    TraceSkip(result, $"projection-skip:ga-neighbor-no-common-y-axis:views={refId}/{targetId}");
                    continue;
                }

                var refState = BuildViewStateFromPos(refView, refPos.X, refPos.Y, frameOffsetsById);
                var targetState = BuildViewStateFromPos(target, targetPos.X, targetPos.Y, frameOffsetsById);
                var refSheetY = DrawingProjectionAlignmentMath.LocalCoordinateToSheet(refState, refAxis.Coordinate, "Y");
                var targetSheetY = DrawingProjectionAlignmentMath.LocalCoordinateToSheet(targetState, targetAxis.Coordinate, "Y");
                var dy = refSheetY - targetSheetY;

                if (Math.Abs(dy) > MaxAllowedMove)
                {
                    TraceSkip(result, $"projection-skip:ga-neighbor-y-move-too-large:view={targetId}:dy={dy:F1}");
                    continue;
                }

                if (TryMoveView(result, target, 0, dy, frameOffsetsById, sheetWidth, sheetHeight, margin, reservedAreas, arrangedViews, targetPos.X, targetPos.Y, boundsMarginOverride: 0))
                {
                    posById[targetId] = (targetPos.X, targetPos.Y + dy);
                    var movedState = BuildViewStateFromPos(target, targetPos.X, targetPos.Y + dy, frameOffsetsById);
                    frameCenterById[targetId] = (movedState.FrameCenterX, movedState.FrameCenterY);
                }
            }
        }

        var byY = views.OrderBy(v => frameCenterById.TryGetValue(v.GetIdentifier().ID, out var p) ? p.Y : 0).ToList();
        for (var i = 0; i < byY.Count; i++)
        {
            var refView = byY[i];
            var refId = refView.GetIdentifier().ID;
            if (!axesCache.TryGetValue(refId, out var refAxes))
                continue;
            if (!posById.TryGetValue(refId, out var refPos))
                continue;
            frameCenterById.TryGetValue(refId, out var refCenter2);

            for (var j = i + 1; j < byY.Count; j++)
            {
                var target = byY[j];
                var targetId = target.GetIdentifier().ID;
                if (!axesCache.TryGetValue(targetId, out var targetAxes))
                    continue;
                if (!posById.TryGetValue(targetId, out var targetPos))
                    continue;
                frameCenterById.TryGetValue(targetId, out var targetCenter2);

                if (Math.Abs(refCenter2.X - targetCenter2.X) > ColGroupThreshold)
                    continue;

                if (!DrawingProjectionAlignmentMath.TrySelectCommonAxis(refAxes, targetAxes, "X", out var refAxis, out var targetAxis))
                {
                    TraceSkip(result, $"projection-skip:ga-neighbor-no-common-x-axis:views={refId}/{targetId}");
                    continue;
                }

                var refState = BuildViewStateFromPos(refView, refPos.X, refPos.Y, frameOffsetsById);
                var targetState = BuildViewStateFromPos(target, targetPos.X, targetPos.Y, frameOffsetsById);
                var refSheetX = DrawingProjectionAlignmentMath.LocalCoordinateToSheet(refState, refAxis.Coordinate, "X");
                var targetSheetX = DrawingProjectionAlignmentMath.LocalCoordinateToSheet(targetState, targetAxis.Coordinate, "X");
                var dx = refSheetX - targetSheetX;

                if (Math.Abs(dx) > MaxAllowedMove)
                {
                    TraceSkip(result, $"projection-skip:ga-neighbor-x-move-too-large:view={targetId}:dx={dx:F1}");
                    continue;
                }

                if (TryMoveView(result, target, dx, 0, frameOffsetsById, sheetWidth, sheetHeight, margin, reservedAreas, arrangedViews, targetPos.X, targetPos.Y, boundsMarginOverride: 0))
                {
                    posById[targetId] = (targetPos.X + dx, targetPos.Y);
                    var movedState = BuildViewStateFromPos(target, targetPos.X + dx, targetPos.Y, frameOffsetsById);
                    frameCenterById[targetId] = (movedState.FrameCenterX, movedState.FrameCenterY);
                }
            }
        }
    }
}
