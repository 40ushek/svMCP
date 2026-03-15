using System;
using System.Collections.Generic;
using System.Linq;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Model;
using TeklaMcpServer.Api.Diagnostics;
using DrawingView = Tekla.Structures.Drawing.View;
using ModelAssembly = Tekla.Structures.Model.Assembly;
using ModelPart = Tekla.Structures.Model.Part;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class DrawingProjectionAlignmentService
{
    private const double MoveEpsilon = 0.01;

    private readonly Model _model;
    private readonly TeklaDrawingPartGeometryApi _partGeometryApi;
    private readonly TeklaDrawingGridApi _gridApi;

    public DrawingProjectionAlignmentService(
        Model? model = null,
        TeklaDrawingPartGeometryApi? partGeometryApi = null,
        TeklaDrawingGridApi? gridApi = null)
    {
        _model = model ?? new Model();
        _partGeometryApi = partGeometryApi ?? new TeklaDrawingPartGeometryApi(_model);
        _gridApi = gridApi ?? new TeklaDrawingGridApi();
    }

    public ProjectionAlignmentResult Apply(
        Tekla.Structures.Drawing.Drawing drawing,
        IReadOnlyList<DrawingView> views,
        IReadOnlyDictionary<int, (double X, double Y)> frameOffsetsById,
        double sheetWidth,
        double sheetHeight,
        double margin,
        IReadOnlyList<ReservedRect> reservedAreas,
        IList<ArrangedView>? arrangedViews = null,
        IReadOnlyDictionary<int, IReadOnlyList<GridAxisInfo>>? preloadedAxes = null)
    {
        var result = new ProjectionAlignmentResult();

        switch (drawing)
        {
            case AssemblyDrawing assemblyDrawing:
            {
                result.Mode = "assembly";
                var front = views.FirstOrDefault(v => v.ViewType == DrawingView.ViewTypes.FrontView);
                if (front == null)
                {
                    TraceSkip(result, "projection-skip:no-front-view");
                    return result;
                }
                ApplyAssemblyAlignment(result, assemblyDrawing, front, views, frameOffsetsById, sheetWidth, sheetHeight, margin, reservedAreas, arrangedViews);
                break;
            }

            case GADrawing:
            {
                result.Mode = "ga";
                var front = views.FirstOrDefault(v => v.ViewType == DrawingView.ViewTypes.FrontView);
                if (front != null)
                    ApplyGaAlignment(result, front, views, frameOffsetsById, sheetWidth, sheetHeight, margin, reservedAreas, arrangedViews, preloadedAxes);
                else
                    ApplyGaNeighborAlignment(result, views, frameOffsetsById, sheetWidth, sheetHeight, margin, reservedAreas, arrangedViews, preloadedAxes);
                break;
            }

            default:
                TraceSkip(result, $"projection-skip:unsupported-drawing-type:{drawing.GetType().Name}");
                break;
        }

        return result;
    }

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

        if (!TryGetPartAnchorSheet(front, mainPartId, out var frontAnchorX, out var frontAnchorY, out reason))
        {
            TraceSkip(result, reason);
            return;
        }

        var top = views.FirstOrDefault(v => v.ViewType == DrawingView.ViewTypes.TopView);
        if (top != null)
            ApplyAssemblyMove(result, top, mainPartId, frontAnchorX, frontAnchorY, alignX: true, frameOffsetsById, sheetWidth, sheetHeight, margin, reservedAreas, arrangedViews);

        foreach (var section in views.Where(v => v.ViewType == DrawingView.ViewTypes.SectionView))
            ApplyAssemblyMove(result, section, mainPartId, frontAnchorX, frontAnchorY, alignX: false, frameOffsetsById, sheetWidth, sheetHeight, margin, reservedAreas, arrangedViews);
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
        IList<ArrangedView>? arrangedViews)
    {
        if (!TryGetPartAnchorSheet(target, mainPartId, out var targetAnchorX, out var targetAnchorY, out var reason))
        {
            TraceSkip(result, reason);
            return;
        }

        var dx = alignX ? frontAnchorX - targetAnchorX : 0.0;
        var dy = alignX ? 0.0 : frontAnchorY - targetAnchorY;
        TryMoveView(result, target, dx, dy, frameOffsetsById, sheetWidth, sheetHeight, margin, reservedAreas, arrangedViews);
    }

    private void ApplyGaAlignment(
        ProjectionAlignmentResult result,
        DrawingView front,
        IReadOnlyList<DrawingView> views,
        IReadOnlyDictionary<int, (double X, double Y)> frameOffsetsById,
        double sheetWidth,
        double sheetHeight,
        double margin,
        IReadOnlyList<ReservedRect> reservedAreas,
        IList<ArrangedView>? arrangedViews,
        IReadOnlyDictionary<int, IReadOnlyList<GridAxisInfo>>? preloadedAxes)
    {
        var frontId = front.GetIdentifier().ID;
        IReadOnlyList<GridAxisInfo> frontAxes;
        if (preloadedAxes != null && preloadedAxes.TryGetValue(frontId, out var cached))
        {
            frontAxes = cached;
        }
        else
        {
            var frontAxesResult = _gridApi.GetGridAxes(frontId);
            if (!frontAxesResult.Success)
            {
                TraceSkip(result, $"projection-skip:front-grid-read-failed:view={frontId}");
                return;
            }
            frontAxes = frontAxesResult.Axes;
        }

        var posById = BuildPositionLookup(views, arrangedViews);

        var top = views.FirstOrDefault(v => v.ViewType == DrawingView.ViewTypes.TopView);
        if (top != null)
            ApplyGaMove(result, front, top, frontAxes, requiredDirection: "X", alignX: true, frameOffsetsById, sheetWidth, sheetHeight, margin, reservedAreas, arrangedViews, preloadedAxes, posById);

        foreach (var section in views.Where(v => v.ViewType == DrawingView.ViewTypes.SectionView))
            ApplyGaMove(result, front, section, frontAxes, requiredDirection: "Y", alignX: false, frameOffsetsById, sheetWidth, sheetHeight, margin, reservedAreas, arrangedViews, preloadedAxes, posById);
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
        IReadOnlyDictionary<int, (double X, double Y)> posById)
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
        var frontState  = BuildViewStateFromPos(front,  frontPos.X,  frontPos.Y,  frameOffsetsById);
        var targetState = BuildViewStateFromPos(target, targetPos.X, targetPos.Y, frameOffsetsById);
        var frontCoordinate  = DrawingProjectionAlignmentMath.LocalCoordinateToSheet(frontState,  frontAxis.Coordinate,  requiredDirection);
        var targetCoordinate = DrawingProjectionAlignmentMath.LocalCoordinateToSheet(targetState, targetAxis.Coordinate, requiredDirection);
        var delta = frontCoordinate - targetCoordinate;
        var dx = alignX ? delta : 0.0;
        var dy = alignX ? 0.0 : delta;
        TryMoveView(result, target, dx, dy, frameOffsetsById, sheetWidth, sheetHeight, margin, reservedAreas, arrangedViews, targetPos.X, targetPos.Y, boundsMarginOverride: 0);
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
        const double RowGroupThreshold = 60.0;  // mm on sheet — views within this ΔY are "same row"
        const double ColGroupThreshold = 80.0;  // mm on sheet — views within this ΔX are "same column"
        const double MaxAllowedMove    = 30.0;  // mm on sheet — skip if required shift exceeds this

        // Use arranged positions as source of truth — view.Origin may be stale before CommitChanges().
        var posById = BuildPositionLookup(views, arrangedViews);

        // Use pre-loaded axes (fetched in committed state) when available; fall back to live API.
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

        // Build frame-center lookup for grouping. Frame center accounts for the frameOffset so that
        // views with large frame offsets (due to different content extents) are still correctly
        // identified as row/column neighbors on the sheet.
        var frameCenterById = new Dictionary<int, (double X, double Y)>();
        foreach (var v in views)
        {
            var id = v.GetIdentifier().ID;
            if (!posById.TryGetValue(id, out var pos)) continue;
            var state = BuildViewStateFromPos(v, pos.X, pos.Y, frameOffsetsById);
            frameCenterById[id] = (state.OriginX + state.FrameOffsetSheetX, state.OriginY + state.FrameOffsetSheetY);
        }

        // Align Y for views in the same horizontal row.
        // Reference = leftmost view in the pair (sorted by frame-center X).
        var byX = views.OrderBy(v => frameCenterById.TryGetValue(v.GetIdentifier().ID, out var p) ? p.X : 0).ToList();
        for (var i = 0; i < byX.Count; i++)
        {
            var refView = byX[i];
            var refId   = refView.GetIdentifier().ID;
            if (!axesCache.TryGetValue(refId, out var refAxes)) continue;
            if (!posById.TryGetValue(refId, out var refPos)) continue;
            frameCenterById.TryGetValue(refId, out var refCenter);

            for (var j = i + 1; j < byX.Count; j++)
            {
                var target   = byX[j];
                var targetId = target.GetIdentifier().ID;
                if (!axesCache.TryGetValue(targetId, out var targetAxes)) continue;
                if (!posById.TryGetValue(targetId, out var targetPos)) continue;
                frameCenterById.TryGetValue(targetId, out var targetCenter);

                if (Math.Abs(refCenter.Y - targetCenter.Y) > RowGroupThreshold) continue;

                if (!DrawingProjectionAlignmentMath.TrySelectCommonAxis(refAxes, targetAxes, "Y", out var refAxis, out var targetAxis))
                {
                    TraceSkip(result, $"projection-skip:ga-neighbor-no-common-y-axis:views={refId}/{targetId}");
                    continue;
                }

                var refState    = BuildViewStateFromPos(refView,    refPos.X,    refPos.Y,    frameOffsetsById);
                var targetState = BuildViewStateFromPos(target,     targetPos.X, targetPos.Y, frameOffsetsById);
                var refSheetY    = DrawingProjectionAlignmentMath.LocalCoordinateToSheet(refState,    refAxis.Coordinate,    "Y");
                var targetSheetY = DrawingProjectionAlignmentMath.LocalCoordinateToSheet(targetState, targetAxis.Coordinate, "Y");
                var dy = refSheetY - targetSheetY;

                if (Math.Abs(dy) > MaxAllowedMove)
                {
                    TraceSkip(result, $"projection-skip:ga-neighbor-y-move-too-large:view={targetId}:dy={dy:F1}");
                    continue;
                }

                if (TryMoveView(result, target, 0, dy, frameOffsetsById, sheetWidth, sheetHeight, margin, reservedAreas, arrangedViews, targetPos.X, targetPos.Y, boundsMarginOverride: 0))
                    posById[targetId] = (targetPos.X, targetPos.Y + dy);
            }
        }

        // Align X for views in the same vertical column.
        // Reference = bottom-most view in the pair (sorted by frame-center Y).
        var byY = views.OrderBy(v => frameCenterById.TryGetValue(v.GetIdentifier().ID, out var p) ? p.Y : 0).ToList();
        for (var i = 0; i < byY.Count; i++)
        {
            var refView = byY[i];
            var refId   = refView.GetIdentifier().ID;
            if (!axesCache.TryGetValue(refId, out var refAxes)) continue;
            if (!posById.TryGetValue(refId, out var refPos)) continue;
            frameCenterById.TryGetValue(refId, out var refCenter2);

            for (var j = i + 1; j < byY.Count; j++)
            {
                var target   = byY[j];
                var targetId = target.GetIdentifier().ID;
                if (!axesCache.TryGetValue(targetId, out var targetAxes)) continue;
                if (!posById.TryGetValue(targetId, out var targetPos)) continue;
                frameCenterById.TryGetValue(targetId, out var targetCenter2);

                if (Math.Abs(refCenter2.X - targetCenter2.X) > ColGroupThreshold) continue;

                if (!DrawingProjectionAlignmentMath.TrySelectCommonAxis(refAxes, targetAxes, "X", out var refAxis, out var targetAxis))
                {
                    TraceSkip(result, $"projection-skip:ga-neighbor-no-common-x-axis:views={refId}/{targetId}");
                    continue;
                }

                var refState    = BuildViewStateFromPos(refView,    refPos.X,    refPos.Y,    frameOffsetsById);
                var targetState = BuildViewStateFromPos(target,     targetPos.X, targetPos.Y, frameOffsetsById);
                var refSheetX    = DrawingProjectionAlignmentMath.LocalCoordinateToSheet(refState,    refAxis.Coordinate,    "X");
                var targetSheetX = DrawingProjectionAlignmentMath.LocalCoordinateToSheet(targetState, targetAxis.Coordinate, "X");
                var dx = refSheetX - targetSheetX;

                if (Math.Abs(dx) > MaxAllowedMove)
                {
                    TraceSkip(result, $"projection-skip:ga-neighbor-x-move-too-large:view={targetId}:dx={dx:F1}");
                    continue;
                }

                if (TryMoveView(result, target, dx, 0, frameOffsetsById, sheetWidth, sheetHeight, margin, reservedAreas, arrangedViews, targetPos.X, targetPos.Y, boundsMarginOverride: 0))
                    posById[targetId] = (targetPos.X + dx, targetPos.Y);
            }
        }
    }

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
        // Fallback to view.Origin for any view not found in arranged list.
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

    private bool TryGetPartAnchorSheet(DrawingView view, int modelId, out double anchorX, out double anchorY, out string reason)
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
            view.Origin?.X ?? 0,
            view.Origin?.Y ?? 0,
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
        double boundsMarginOverride = double.NaN)
    {
        if (Math.Abs(dx) < MoveEpsilon && Math.Abs(dy) < MoveEpsilon)
            return false;

        var effectiveMargin = double.IsNaN(boundsMarginOverride) ? margin : boundsMarginOverride;
        var state = knownOriginX.HasValue
            ? BuildViewStateFromPos(view, knownOriginX.Value, knownOriginY ?? (view.Origin?.Y ?? 0), frameOffsetsById)
            : BuildViewState(view, frameOffsetsById);
        var candidateState = DrawingProjectionAlignmentMath.TranslateOrigin(state, dx, dy);
        var candidateRect = DrawingProjectionAlignmentMath.GetFrameRect(candidateState);

        if (!DrawingProjectionAlignmentMath.IsWithinUsableArea(candidateRect, effectiveMargin, sheetWidth, sheetHeight))
        {
            TraceSkip(result, $"projection-skip:out-of-bounds:view={view.GetIdentifier().ID}:rect=[{candidateRect.MinX:F1},{candidateRect.MinY:F1},{candidateRect.MaxX:F1},{candidateRect.MaxY:F1}]:sheet={sheetWidth}x{sheetHeight}:margin={margin}:scale={state.Scale}:w={state.Width:F1}:h={state.Height:F1}:offX={state.FrameOffsetSheetX:F2}:offY={state.FrameOffsetSheetY:F2}");
            return false;
        }

        if (DrawingProjectionAlignmentMath.IntersectsAnyReserved(candidateRect, reservedAreas))
        {
            TraceSkip(result, $"projection-skip:reserved-overlap:view={view.GetIdentifier().ID}");
            return false;
        }

        var origin = view.Origin;
        origin.X += dx;
        origin.Y += dy;
        view.Origin = origin;
        view.Modify();
        UpdateArrangedView(arrangedViews, view.GetIdentifier().ID, origin.X, origin.Y);
        result.AppliedMoves++;
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
                OriginY = originY
            };
            return;
        }
    }

    private static void TraceSkip(ProjectionAlignmentResult result, string reason)
    {
        result.SkippedMoves++;
        result.Diagnostics.Add(reason);
        PerfTrace.Write("api-view", "fit_views_projection_skip", 0, reason);
    }
}

internal sealed class ProjectionAlignmentResult
{
    public string Mode { get; set; } = "none";
    public int AppliedMoves { get; set; }
    public int SkippedMoves { get; set; }
    public List<string> Diagnostics { get; } = new();
}

internal sealed class ProjectionViewState
{
    public ProjectionViewState(
        int viewId,
        double originX,
        double originY,
        double scale,
        double width,
        double height,
        double frameOffsetSheetX,
        double frameOffsetSheetY)
    {
        ViewId = viewId;
        OriginX = originX;
        OriginY = originY;
        Scale = scale;
        Width = width;
        Height = height;
        FrameOffsetSheetX = frameOffsetSheetX;
        FrameOffsetSheetY = frameOffsetSheetY;
    }

    public int ViewId { get; }
    public double OriginX { get; }
    public double OriginY { get; }
    public double Scale { get; }
    public double Width { get; }
    public double Height { get; }
    public double FrameOffsetSheetX { get; }
    public double FrameOffsetSheetY { get; }
}

internal sealed class ProjectionRect
{
    public ProjectionRect(double minX, double minY, double maxX, double maxY)
    {
        MinX = minX;
        MinY = minY;
        MaxX = maxX;
        MaxY = maxY;
    }

    public double MinX { get; }
    public double MinY { get; }
    public double MaxX { get; }
    public double MaxY { get; }
}

internal static class DrawingProjectionAlignmentMath
{
    public static (double X, double Y) LocalToSheet(ProjectionViewState view, double localX, double localY)
    {
        var scale = view.Scale > 0 ? view.Scale : 1.0;
        return (view.OriginX + (localX / scale), view.OriginY + (localY / scale));
    }

    public static double LocalCoordinateToSheet(ProjectionViewState view, double localCoordinate, string direction)
    {
        var scale = view.Scale > 0 ? view.Scale : 1.0;
        return string.Equals(direction, "Y", StringComparison.OrdinalIgnoreCase)
            ? view.OriginY + (localCoordinate / scale)
            : view.OriginX + (localCoordinate / scale);
    }

    public static ProjectionRect GetFrameRect(ProjectionViewState view)
    {
        var centerX = view.OriginX + view.FrameOffsetSheetX;
        var centerY = view.OriginY + view.FrameOffsetSheetY;
        return new ProjectionRect(
            centerX - (view.Width * 0.5),
            centerY - (view.Height * 0.5),
            centerX + (view.Width * 0.5),
            centerY + (view.Height * 0.5));
    }

    public static ProjectionViewState TranslateOrigin(ProjectionViewState view, double dx, double dy)
    {
        return new ProjectionViewState(
            view.ViewId,
            view.OriginX + dx,
            view.OriginY + dy,
            view.Scale,
            view.Width,
            view.Height,
            view.FrameOffsetSheetX,
            view.FrameOffsetSheetY);
    }

    public static bool IsWithinUsableArea(ProjectionRect rect, double margin, double sheetWidth, double sheetHeight)
    {
        return rect.MinX >= margin
            && rect.MaxX <= sheetWidth - margin
            && rect.MinY >= margin
            && rect.MaxY <= sheetHeight - margin;
    }

    public static bool IntersectsAnyReserved(ProjectionRect rect, IReadOnlyList<ReservedRect> reservedAreas)
    {
        foreach (var area in reservedAreas)
        {
            if (Intersects(rect, area))
                return true;
        }

        return false;
    }

    public static bool TrySelectCommonAxis(
        IReadOnlyList<GridAxisInfo> frontAxes,
        IReadOnlyList<GridAxisInfo> targetAxes,
        string requiredDirection,
        out GridAxisInfo frontAxis,
        out GridAxisInfo targetAxis)
    {
        frontAxis = new GridAxisInfo();
        targetAxis = new GridAxisInfo();

        var orderedFrontAxes = frontAxes
            .Where(a => string.Equals(a.Direction, requiredDirection, StringComparison.OrdinalIgnoreCase))
            .OrderBy(a => a.Coordinate)
            .ThenBy(a => Normalize(a.Guid))
            .ThenBy(a => Normalize(a.Label))
            .ToList();
        if (orderedFrontAxes.Count == 0)
            return false;

        var targetByGuid = targetAxes
            .Where(a => string.Equals(a.Direction, requiredDirection, StringComparison.OrdinalIgnoreCase))
            .Where(a => !string.IsNullOrWhiteSpace(a.Guid))
            .GroupBy(a => Normalize(a.Guid))
            .ToDictionary(g => g.Key, g => g.OrderBy(a => a.Coordinate).First());

        var targetByFallback = targetAxes
            .Where(a => string.Equals(a.Direction, requiredDirection, StringComparison.OrdinalIgnoreCase))
            .GroupBy(BuildFallbackKey)
            .ToDictionary(g => g.Key, g => g.OrderBy(a => a.Coordinate).First());

        foreach (var candidate in orderedFrontAxes)
        {
            var guidKey = Normalize(candidate.Guid);
            if (!string.IsNullOrWhiteSpace(guidKey) && targetByGuid.TryGetValue(guidKey, out var guidMatch))
            {
                frontAxis = candidate;
                targetAxis = guidMatch;
                return true;
            }

            if (targetByFallback.TryGetValue(BuildFallbackKey(candidate), out var fallbackMatch))
            {
                frontAxis = candidate;
                targetAxis = fallbackMatch;
                return true;
            }
        }

        return false;
    }

    private static bool Intersects(ProjectionRect rect, ReservedRect area)
    {
        return !(rect.MaxX <= area.MinX
            || area.MaxX <= rect.MinX
            || rect.MaxY <= area.MinY
            || area.MaxY <= rect.MinY);
    }

    private static string BuildFallbackKey(GridAxisInfo axis)
    {
        return $"{Normalize(axis.Direction)}|{Normalize(axis.Label)}";
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value!.Trim().ToUpperInvariant();
    }
}
