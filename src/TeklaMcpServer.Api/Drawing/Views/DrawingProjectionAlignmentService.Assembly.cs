using System;
using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using ModelAssembly = Tekla.Structures.Model.Assembly;
using ModelPart = Tekla.Structures.Model.Part;
using DrawingView = Tekla.Structures.Drawing.View;

namespace TeklaMcpServer.Api.Drawing;

internal sealed partial class DrawingProjectionAlignmentService
{
    private void ApplyAssemblyAlignment(
        ProjectionAlignmentResult result,
        AssemblyDrawing drawing,
        NeighborSet neighbors,
        IReadOnlyList<DrawingView> views,
        IReadOnlyDictionary<int, (double X, double Y)> frameOffsetsById,
        double sheetWidth,
        double sheetHeight,
        double margin,
        IReadOnlyList<ReservedRect> reservedAreas,
        IList<ArrangedView>? arrangedViews)
    {
        var baseView = neighbors.BaseView;
        if (!TryGetAssemblyMainPartId(drawing, out var mainPartId, out var reason))
        {
            TraceSkip(result, reason);
            return;
        }

        var posById = BuildPositionLookup(views, arrangedViews);
        posById.TryGetValue(baseView.GetIdentifier().ID, out var basePos);

        if (!TryGetPartAnchorSheet(baseView, mainPartId, basePos.X, basePos.Y, out var baseAnchorX, out var baseAnchorY, out reason))
        {
            TraceSkip(result, reason);
            return;
        }

        var allStates = views
            .Select(v => { posById.TryGetValue(v.GetIdentifier().ID, out var p); return BuildViewStateFromPos(v, p.X, p.Y, frameOffsetsById); })
            .ToList();

        var top = neighbors.TopNeighbor;

        // If top projected neighbor exists, ensure there is room for it above BaseView.
        // If BaseView is too high on the sheet, shift non-top views down first.
        if (top != null)
        {
            var topState = allStates.First(s => s.ViewId == top.GetIdentifier().ID);
            var baseState = allStates.First(s => s.ViewId == baseView.GetIdentifier().ID);
            var topFrameHeight = DrawingProjectionAlignmentMath.GetFrameRect(topState).MaxY - DrawingProjectionAlignmentMath.GetFrameRect(topState).MinY;
            var baseFrameMaxY = DrawingProjectionAlignmentMath.GetFrameRect(baseState).MaxY;
            var needed = topFrameHeight + ProjectionViewGap;
            var available = (sheetHeight - margin) - baseFrameMaxY;

            if (needed > available)
            {
                var shiftDown = needed - available;
                var nonTopViews = views.Where(v => v.GetIdentifier().ID != top.GetIdentifier().ID).ToList();
                var previewStates = allStates.ToDictionary(s => s.ViewId);
                var canShift = true;
                foreach (var v in nonTopViews)
                {
                    var viewId = v.GetIdentifier().ID;
                    posById.TryGetValue(viewId, out var p);
                    var others = previewStates.Values.Where(s => s.ViewId != viewId && s.ViewId != top.GetIdentifier().ID).ToList();
                    if (!CanMoveView(
                            null,
                            v,
                            0,
                            -shiftDown,
                            frameOffsetsById,
                            sheetWidth,
                            sheetHeight,
                            margin,
                            reservedAreas,
                            p.X,
                            p.Y,
                            boundsMarginOverride: 0,
                            otherViewStates: others))
                    {
                        canShift = false;
                        break;
                    }

                    previewStates[viewId] = BuildViewStateFromPos(v, p.X, p.Y - shiftDown, frameOffsetsById);
                }

                var shifted = true;
                var moved = new List<(DrawingView View, double X, double Y)>();
                if (canShift)
                {
                    foreach (var v in nonTopViews)
                    {
                        posById.TryGetValue(v.GetIdentifier().ID, out var p);
                        var others = allStates.Where(s => s.ViewId != v.GetIdentifier().ID && s.ViewId != top.GetIdentifier().ID).ToList();
                        if (!TryMoveView(result, v, 0, -shiftDown, frameOffsetsById, sheetWidth, sheetHeight, margin, reservedAreas, arrangedViews, p.X, p.Y, boundsMarginOverride: 0, otherViewStates: others))
                        {
                            shifted = false;
                            break;
                        }

                        moved.Add((v, p.X, p.Y));
                        posById[v.GetIdentifier().ID] = (p.X, p.Y - shiftDown);
                        // Keep allStates fresh so subsequent iterations check actual positions.
                        var vid = v.GetIdentifier().ID;
                        for (var si = 0; si < allStates.Count; si++)
                            if (allStates[si].ViewId == vid)
                            {
                                allStates[si] = BuildViewStateFromPos(v, p.X, p.Y - shiftDown, frameOffsetsById);
                                break;
                            }
                    }
                }
                else
                {
                    shifted = false;
                }

                if (!shifted)
                {
                    for (var i = moved.Count - 1; i >= 0; i--)
                    {
                        var item = moved[i];
                        RestoreViewOrigin(item.View, item.X, item.Y, arrangedViews);
                        posById[item.View.GetIdentifier().ID] = (item.X, item.Y);
                        var vid = item.View.GetIdentifier().ID;
                        for (var si = 0; si < allStates.Count; si++)
                            if (allStates[si].ViewId == vid)
                            {
                                allStates[si] = BuildViewStateFromPos(item.View, item.X, item.Y, frameOffsetsById);
                                break;
                            }
                    }
                }

                if (shifted)
                {
                    baseAnchorY -= shiftDown;
                    allStates = views
                        .Select(v => { posById.TryGetValue(v.GetIdentifier().ID, out var p); return BuildViewStateFromPos(v, p.X, p.Y, frameOffsetsById); })
                        .ToList();
                }
            }
        }

        if (top != null)
        {
            var topId = top.GetIdentifier().ID;
            var others = allStates.Where(s => s.ViewId != topId).ToList();
            ApplyAssemblyMove(result, top, mainPartId, baseAnchorX, baseAnchorY, alignX: true, frameOffsetsById, sheetWidth, sheetHeight, margin, reservedAreas, arrangedViews, posById, allStates, others);
        }

        foreach (var neighbor in new[]
                 {
                     (View: neighbors.BottomNeighbor, Role: NeighborRole.Bottom),
                     (View: neighbors.SideNeighborLeft, Role: NeighborRole.SideLeft),
                     (View: neighbors.SideNeighborRight, Role: NeighborRole.SideRight)
                 })
        {
            if (neighbor.View == null || !DrawingProjectionAlignmentMath.TryGetNeighborAlignmentAxis(neighbor.Role, out var alignNeighborX))
                continue;

            var viewId = neighbor.View.GetIdentifier().ID;
            var others = allStates.Where(s => s.ViewId != viewId).ToList();
            ApplyAssemblyMove(result, neighbor.View, mainPartId, baseAnchorX, baseAnchorY, alignNeighborX, frameOffsetsById, sheetWidth, sheetHeight, margin, reservedAreas, arrangedViews, posById, allStates, others);
        }

        foreach (var section in views.Where(v => v.ViewType == DrawingView.ViewTypes.SectionView))
        {
            if (!TryGetSectionAlignmentAxis(drawing, baseView, section, result, out var alignSectionX))
                continue;

            var sectionId = section.GetIdentifier().ID;
            var others = allStates.Where(s => s.ViewId != sectionId).ToList();
            ApplyAssemblyMove(result, section, mainPartId, baseAnchorX, baseAnchorY, alignSectionX, frameOffsetsById, sheetWidth, sheetHeight, margin, reservedAreas, arrangedViews, posById, allStates, others);
        }
    }

    private void ApplyAssemblyMove(
        ProjectionAlignmentResult result,
        DrawingView target,
        int mainPartId,
        double frontAnchorX,
        double frontAnchorY,
        bool alignX,
        IReadOnlyDictionary<int, (double X, double Y)> frameOffsetsById,
        double sheetWidth,
        double sheetHeight,
        double margin,
        IReadOnlyList<ReservedRect> reservedAreas,
        IList<ArrangedView>? arrangedViews,
        IReadOnlyDictionary<int, (double X, double Y)> posById,
        List<ProjectionViewState>? allStates = null,
        IReadOnlyList<ProjectionViewState>? otherViewStates = null)
    {
        var targetId = target.GetIdentifier().ID;
        posById.TryGetValue(targetId, out var targetPos);

        if (!TryGetPartAnchorSheet(target, mainPartId, targetPos.X, targetPos.Y, out var targetAnchorX, out var targetAnchorY, out var reason))
        {
            TraceSkip(result, reason);
            return;
        }

        var dx = alignX ? frontAnchorX - targetAnchorX : 0.0;
        var dy = alignX ? 0.0 : frontAnchorY - targetAnchorY;
        if (TryMoveView(result, target, dx, dy, frameOffsetsById, sheetWidth, sheetHeight, margin, reservedAreas, arrangedViews, targetPos.X, targetPos.Y, boundsMarginOverride: 0, otherViewStates: otherViewStates)
            && allStates != null)
        {
            // Keep allStates fresh so subsequent moves use the actual new position.
            var newX = targetPos.X + dx;
            var newY = targetPos.Y + dy;
            for (var si = 0; si < allStates.Count; si++)
                if (allStates[si].ViewId == targetId)
                {
                    allStates[si] = BuildViewStateFromPos(target, newX, newY, frameOffsetsById);
                    break;
                }
        }
    }

    private bool TryGetAssemblyMainPartId(AssemblyDrawing drawing, out int mainPartId, out string reason)
    {
        mainPartId = 0;
        var assemblyIdentifier = drawing.AssemblyIdentifier;
        if (assemblyIdentifier == null || (assemblyIdentifier.ID == 0 && assemblyIdentifier.GUID == Guid.Empty))
        {
            reason = "projection-skip:assembly-id-missing";
            return false;
        }

        if (_model.SelectModelObject(assemblyIdentifier) is not ModelAssembly assembly)
        {
            reason = "projection-skip:assembly-model-object-not-found";
            return false;
        }

        if (assembly.GetMainPart() is not ModelPart mainPart)
        {
            reason = "projection-skip:assembly-main-part-not-found";
            return false;
        }

        mainPartId = mainPart.Identifier.ID;
        reason = string.Empty;
        return true;
    }
}
