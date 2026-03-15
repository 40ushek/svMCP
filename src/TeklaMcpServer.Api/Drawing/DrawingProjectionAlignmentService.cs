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
        IList<ArrangedView>? arrangedViews = null)
    {
        var result = new ProjectionAlignmentResult();
        var front = views.FirstOrDefault(v => v.ViewType == DrawingView.ViewTypes.FrontView);
        if (front == null)
        {
            TraceSkip(result, "projection-skip:no-front-view");
            return result;
        }

        switch (drawing)
        {
            case AssemblyDrawing assemblyDrawing:
                result.Mode = "assembly";
                ApplyAssemblyAlignment(result, assemblyDrawing, front, views, frameOffsetsById, sheetWidth, sheetHeight, margin, reservedAreas, arrangedViews);
                break;

            case GADrawing:
                result.Mode = "ga";
                ApplyGaAlignment(result, front, views, frameOffsetsById, sheetWidth, sheetHeight, margin, reservedAreas, arrangedViews);
                break;

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
        IList<ArrangedView>? arrangedViews)
    {
        var frontAxesResult = _gridApi.GetGridAxes(front.GetIdentifier().ID);
        if (!frontAxesResult.Success)
        {
            TraceSkip(result, $"projection-skip:front-grid-read-failed:view={front.GetIdentifier().ID}");
            return;
        }

        var top = views.FirstOrDefault(v => v.ViewType == DrawingView.ViewTypes.TopView);
        if (top != null)
            ApplyGaMove(result, front, top, frontAxesResult.Axes, requiredDirection: "X", alignX: true, frameOffsetsById, sheetWidth, sheetHeight, margin, reservedAreas, arrangedViews);

        foreach (var section in views.Where(v => v.ViewType == DrawingView.ViewTypes.SectionView))
            ApplyGaMove(result, front, section, frontAxesResult.Axes, requiredDirection: "Y", alignX: false, frameOffsetsById, sheetWidth, sheetHeight, margin, reservedAreas, arrangedViews);
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
        IList<ArrangedView>? arrangedViews)
    {
        var targetAxesResult = _gridApi.GetGridAxes(target.GetIdentifier().ID);
        if (!targetAxesResult.Success)
        {
            TraceSkip(result, $"projection-skip:grid-read-failed:view={target.GetIdentifier().ID}");
            return;
        }

        if (!DrawingProjectionAlignmentMath.TrySelectCommonAxis(frontAxes, targetAxesResult.Axes, requiredDirection, out var frontAxis, out var targetAxis))
        {
            TraceSkip(result, $"projection-skip:no-common-axis:view={target.GetIdentifier().ID}:direction={requiredDirection}");
            return;
        }

        var frontState = BuildViewState(front, frameOffsetsById);
        var targetState = BuildViewState(target, frameOffsetsById);
        var frontCoordinate = DrawingProjectionAlignmentMath.LocalCoordinateToSheet(frontState, frontAxis.Coordinate, requiredDirection);
        var targetCoordinate = DrawingProjectionAlignmentMath.LocalCoordinateToSheet(targetState, targetAxis.Coordinate, requiredDirection);
        var delta = frontCoordinate - targetCoordinate;
        var dx = alignX ? delta : 0.0;
        var dy = alignX ? 0.0 : delta;
        TryMoveView(result, target, dx, dy, frameOffsetsById, sheetWidth, sheetHeight, margin, reservedAreas, arrangedViews);
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

    private void TryMoveView(
        ProjectionAlignmentResult result,
        DrawingView view,
        double dx,
        double dy,
        IReadOnlyDictionary<int, (double X, double Y)> frameOffsetsById,
        double sheetWidth,
        double sheetHeight,
        double margin,
        IReadOnlyList<ReservedRect> reservedAreas,
        IList<ArrangedView>? arrangedViews)
    {
        if (Math.Abs(dx) < MoveEpsilon && Math.Abs(dy) < MoveEpsilon)
            return;

        var state = BuildViewState(view, frameOffsetsById);
        var candidateState = DrawingProjectionAlignmentMath.TranslateOrigin(state, dx, dy);
        var candidateRect = DrawingProjectionAlignmentMath.GetFrameRect(candidateState);

        if (!DrawingProjectionAlignmentMath.IsWithinUsableArea(candidateRect, margin, sheetWidth, sheetHeight))
        {
            TraceSkip(result, $"projection-skip:out-of-bounds:view={view.GetIdentifier().ID}");
            return;
        }

        if (DrawingProjectionAlignmentMath.IntersectsAnyReserved(candidateRect, reservedAreas))
        {
            TraceSkip(result, $"projection-skip:reserved-overlap:view={view.GetIdentifier().ID}");
            return;
        }

        var origin = view.Origin;
        origin.X += dx;
        origin.Y += dy;
        view.Origin = origin;
        view.Modify();
        UpdateArrangedView(arrangedViews, view.GetIdentifier().ID, origin.X, origin.Y);
        result.AppliedMoves++;
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
