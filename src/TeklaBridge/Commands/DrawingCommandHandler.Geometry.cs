using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using TeklaMcpServer.Api.Drawing;

namespace TeklaBridge.Commands;

internal sealed partial class DrawingCommandHandler
{
    private bool TryHandleGeometryCommands(string command, string[] args)
    {
        TeklaDrawingPartGeometryApi? partGeometryApi = null;
        TeklaDrawingPartGeometryApi GetPartGeometryApi() => partGeometryApi ??= new TeklaDrawingPartGeometryApi(_model);
        TeklaDrawingGridApi? gridApi = null;
        TeklaDrawingGridApi GetGridApi() => gridApi ??= new TeklaDrawingGridApi();
        TeklaDrawingPartsApi? partsApi = null;
        TeklaDrawingPartsApi GetPartsApi() => partsApi ??= new TeklaDrawingPartsApi(_model);
        TeklaDrawingDebugOverlayApi? debugOverlayApi = null;
        TeklaDrawingDebugOverlayApi GetDebugOverlayApi() => debugOverlayApi ??= new TeklaDrawingDebugOverlayApi();

        switch (command)
        {
            case "get_part_geometry_in_view":
                return HandleGetPartGeometryInView(GetPartGeometryApi(), args);

            case "get_all_parts_geometry_in_view":
                return HandleGetAllPartsGeometryInView(GetPartGeometryApi(), args);

            case "get_grid_axes":
                return HandleGetGridAxes(GetGridApi(), args);

            case "get_drawing_parts":
                return HandleGetDrawingParts(GetPartsApi());

            case "draw_debug_overlay":
                return HandleDrawDebugOverlay(GetDebugOverlayApi(), args);

            case "draw_selected_mark_part_axis_geometry":
                return HandleDrawSelectedMarkPartAxisGeometry(GetDebugOverlayApi());

            case "clear_debug_overlay":
                return HandleClearDebugOverlay(GetDebugOverlayApi(), args);

            default:
                return false;
        }
    }

    private bool HandleGetAllPartsGeometryInView(TeklaDrawingPartGeometryApi api, string[] args)
    {
        if (args.Length < 2 || !int.TryParse(args[1], out var viewId))
        {
            WriteError("get_all_parts_geometry_in_view requires viewId argument");
            return true;
        }
        var results = api.GetAllPartsGeometryInView(viewId);
        WriteJson(new
        {
            viewId,
            total = results.Count,
            parts = results.Select(r => new
            {
                modelId    = r.ModelId,
                type       = r.Type,
                name       = r.Name,
                partPos    = r.PartPos,
                profile    = r.Profile,
                material   = r.Material,
                startPoint = r.StartPoint,
                endPoint   = r.EndPoint,
                axisX      = r.AxisX,
                axisY      = r.AxisY,
                bboxMin    = r.BboxMin,
                bboxMax    = r.BboxMax
            })
        });
        return true;
    }

    private bool HandleGetPartGeometryInView(TeklaDrawingPartGeometryApi api, string[] args)
    {
        var parseResult = DrawingCommandParsers.ParsePartGeometryInViewRequest(args);
        if (!parseResult.IsValid)
        {
            WriteError(parseResult.Error);
            return true;
        }

        var result = api.GetPartGeometryInView(
            parseResult.Request.ViewId,
            parseResult.Request.ModelId);
        WritePartGeometryInViewResult(result);
        return true;
    }

    private bool HandleGetGridAxes(TeklaDrawingGridApi api, string[] args)
    {
        var parseResult = DrawingCommandParsers.ParseGridAxesRequest(args);
        if (!parseResult.IsValid)
        {
            WriteError(parseResult.Error);
            return true;
        }

        var result = api.GetGridAxes(parseResult.Request.ViewId);
        WriteGridAxesResult(result);
        return true;
    }

    private bool HandleGetDrawingParts(TeklaDrawingPartsApi api)
    {
        var result = api.GetDrawingParts();
        WriteGetDrawingPartsResult(result);
        return true;
    }

    private bool HandleDrawDebugOverlay(TeklaDrawingDebugOverlayApi api, string[] args)
    {
        if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
        {
            WriteError("draw_debug_overlay requires a JSON payload argument");
            return true;
        }

        var result = api.DrawOverlay(args[1]);
        WriteJson(new
        {
            group = result.Group,
            clearedCount = result.ClearedCount,
            createdCount = result.CreatedCount,
            createdIds = result.CreatedIds
        });
        return true;
    }

    private bool HandleDrawSelectedMarkPartAxisGeometry(TeklaDrawingDebugOverlayApi debugOverlayApi)
    {
        if (!EnsureActiveDrawing())
            return true;

        var drawingHandler = new DrawingHandler();
        var activeDrawing = drawingHandler.GetActiveDrawing();
        if (activeDrawing == null)
        {
            WriteError("No drawing is currently open");
            return true;
        }

        var selectedMarks = new List<Mark>();
        var selected = drawingHandler.GetDrawingObjectSelector().GetSelected();
        while (selected.MoveNext())
        {
            if (selected.Current is Mark selectedMark)
                selectedMarks.Add(selectedMark);
        }

        if (selectedMarks.Count == 0)
        {
            WriteError("No marks selected");
            return true;
        }

        var request = new DrawingDebugOverlayRequest
        {
            Group = "selected-mark-part-axis-geometry",
            ClearGroupFirst = true,
            Shapes = new List<DrawingDebugShape>()
        };

        var markResults = new List<object>();

        foreach (var mark in selectedMarks)
        {
            var view = mark.GetView();
            if (view == null)
                continue;

            var viewId = view.GetIdentifier().ID;
            var geometry = MarkGeometryHelper.Build(mark, _model, viewId);
            if (geometry.Corners.Count < 4)
                continue;
            var centerX = geometry.CenterX;
            var centerY = geometry.CenterY;
            var polygonPoints = geometry.Corners
                .Select(p => new[] { Round2(p[0]), Round2(p[1]) })
                .ToList();

            request.Shapes.Add(new DrawingDebugShape
            {
                Kind = "polygon",
                ViewId = viewId,
                Points = polygonPoints,
                Color = "Green",
                LineType = "DashDot"
            });
            request.Shapes.Add(new DrawingDebugShape
            {
                Kind = "cross",
                ViewId = viewId,
                X1 = Round2(centerX),
                Y1 = Round2(centerY),
                Size = 30,
                Color = "Yellow"
            });

            if (geometry.HasAxis)
            {
                var ux = geometry.AxisDx;
                var uy = geometry.AxisDy;
                var axisHalf = Math.Min(geometry.Width * 0.6, 250.0);
                request.Shapes.Add(new DrawingDebugShape
                {
                    Kind = "line",
                    ViewId = viewId,
                    X1 = Round2(centerX - (ux * axisHalf)),
                    Y1 = Round2(centerY - (uy * axisHalf)),
                    X2 = Round2(centerX + (ux * axisHalf)),
                    Y2 = Round2(centerY + (uy * axisHalf)),
                    Color = "Cyan",
                    LineType = "Solid"
                });
            }

            markResults.Add(new
            {
                markId = mark.GetIdentifier().ID,
                viewId,
                centerX = Round2(centerX),
                centerY = Round2(centerY),
                angleDeg = Round2(geometry.AngleDeg),
                geometrySource = geometry.Source
            });
        }

        var overlayResult = debugOverlayApi.DrawOverlay(JsonSerializer.Serialize(request));
        WriteJson(new
        {
            marks = markResults,
            group = overlayResult.Group,
            clearedCount = overlayResult.ClearedCount,
            createdCount = overlayResult.CreatedCount,
            createdIds = overlayResult.CreatedIds
        });
        return true;
    }

    private bool HandleClearDebugOverlay(TeklaDrawingDebugOverlayApi api, string[] args)
    {
        var group = args.Length >= 2 ? args[1] : string.Empty;
        var result = api.ClearOverlay(group);
        WriteJson(new
        {
            group = result.Group,
            clearedCount = result.ClearedCount
        });
        return true;
    }

    private void WriteGetDrawingPartsResult(GetDrawingPartsResult result)
    {
        WriteJson(new
        {
            total = result.Total,
            parts = result.Parts.Select(p => new
            {
                modelId = p.ModelId,
                type = p.Type,
                partPos = p.PartPos,
                assemblyPos = p.AssemblyPos,
                profile = p.Profile,
                material = p.Material,
                name = p.Name
            })
        });
    }

    private void WritePartGeometryInViewResult(PartGeometryInViewResult result)
    {
        WriteJson(new
        {
            success = result.Success,
            viewId = result.ViewId,
            modelId = result.ModelId,
            startPoint = result.StartPoint,
            endPoint = result.EndPoint,
            axisX = result.AxisX,
            axisY = result.AxisY,
            bboxMin = result.BboxMin,
            bboxMax = result.BboxMax,
            error = result.Error
        });
    }

    private void WriteGridAxesResult(GetGridAxesResult result)
    {
        WriteJson(new
        {
            success = result.Success,
            viewId = result.ViewId,
            axes = result.Axes.Select(a => new
            {
                label = a.Label,
                direction = a.Direction,
                coordinate = a.Coordinate,
                startX = a.StartX,
                startY = a.StartY,
                endX = a.EndX,
                endY = a.EndY
            }),
            error = result.Error
        });
    }

    private static double Round2(double value) => Math.Round(value, 2);
}
