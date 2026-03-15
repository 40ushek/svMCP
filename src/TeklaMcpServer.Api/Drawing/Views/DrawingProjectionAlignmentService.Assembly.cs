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
        DrawingView front,
        IReadOnlyList<DrawingView> views,
        IReadOnlyDictionary<int, (double X, double Y)> frameOffsetsById,
        double sheetWidth,
        double sheetHeight,
        double margin,
        IReadOnlyList<ReservedRect> reservedAreas,
        IList<ArrangedView>? arrangedViews)
    {
        if (!TryGetAssemblyMainPartId(drawing, out var mainPartId, out var reason))
        {
            TraceSkip(result, reason);
            return;
        }

        var posById = BuildPositionLookup(views, arrangedViews);
        posById.TryGetValue(front.GetIdentifier().ID, out var frontPos);

        if (!TryGetPartAnchorSheet(front, mainPartId, frontPos.X, frontPos.Y, out var frontAnchorX, out var frontAnchorY, out reason))
        {
            TraceSkip(result, reason);
            return;
        }

        var top = views.FirstOrDefault(v => v.ViewType == DrawingView.ViewTypes.TopView);
        if (top != null)
            ApplyAssemblyMove(result, top, mainPartId, frontAnchorX, frontAnchorY, alignX: true, frameOffsetsById, sheetWidth, sheetHeight, margin, reservedAreas, arrangedViews, posById);

        foreach (var section in views.Where(v => v.ViewType == DrawingView.ViewTypes.SectionView))
            ApplyAssemblyMove(result, section, mainPartId, frontAnchorX, frontAnchorY, alignX: false, frameOffsetsById, sheetWidth, sheetHeight, margin, reservedAreas, arrangedViews, posById);
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
        IReadOnlyDictionary<int, (double X, double Y)> posById)
    {
        posById.TryGetValue(target.GetIdentifier().ID, out var targetPos);

        if (!TryGetPartAnchorSheet(target, mainPartId, targetPos.X, targetPos.Y, out var targetAnchorX, out var targetAnchorY, out var reason))
        {
            TraceSkip(result, reason);
            return;
        }

        var dx = alignX ? frontAnchorX - targetAnchorX : 0.0;
        var dy = alignX ? 0.0 : frontAnchorY - targetAnchorY;
        TryMoveView(result, target, dx, dy, frameOffsetsById, sheetWidth, sheetHeight, margin, reservedAreas, arrangedViews, targetPos.X, targetPos.Y, boundsMarginOverride: 0);
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
