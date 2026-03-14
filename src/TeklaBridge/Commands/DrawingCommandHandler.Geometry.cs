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
                return HandleDrawSelectedMarkPartAxisGeometry(GetPartGeometryApi(), GetDebugOverlayApi());

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

    private bool HandleDrawSelectedMarkPartAxisGeometry(TeklaDrawingPartGeometryApi partGeometryApi, TeklaDrawingDebugOverlayApi debugOverlayApi)
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

        if (selectedMarks.Count != 1)
        {
            WriteError($"Expected exactly 1 selected mark, got {selectedMarks.Count}");
            return true;
        }

        var mark = selectedMarks[0];
        var view = mark.GetView();
        if (view == null)
        {
            WriteError($"Selected mark {mark.GetIdentifier().ID} has no owner view");
            return true;
        }

        int? modelId = null;
        var related = mark.GetRelatedObjects();
        while (related.MoveNext())
        {
            if (related.Current is Tekla.Structures.Drawing.ModelObject drawingModelObject)
            {
                modelId = drawingModelObject.ModelIdentifier.ID;
                break;
            }
        }

        if (!modelId.HasValue)
        {
            WriteError($"Selected mark {mark.GetIdentifier().ID} has no related model object");
            return true;
        }

        var geometry = partGeometryApi.GetPartGeometryInView(view.GetIdentifier().ID, modelId.Value);
        if (!geometry.Success)
        {
            WriteError(geometry.Error ?? "Failed to read part geometry in view");
            return true;
        }

        if (geometry.StartPoint.Length < 2 || geometry.EndPoint.Length < 2)
        {
            WriteError($"Related model object {modelId.Value} does not expose a usable start/end axis in view {view.GetIdentifier().ID}");
            return true;
        }

        var axisDx = geometry.EndPoint[0] - geometry.StartPoint[0];
        var axisDy = geometry.EndPoint[1] - geometry.StartPoint[1];
        var axisLength = Math.Sqrt((axisDx * axisDx) + (axisDy * axisDy));
        if (axisLength < 0.001)
        {
            WriteError($"Related model object {modelId.Value} axis is too short in view {view.GetIdentifier().ID}");
            return true;
        }

        var bbox = mark.GetAxisAlignedBoundingBox();
        var objectAligned = mark.GetObjectAlignedBoundingBox();
        var centerX = (bbox.MinPoint.X + bbox.MaxPoint.X) / 2.0;
        var centerY = (bbox.MinPoint.Y + bbox.MaxPoint.Y) / 2.0;
        var halfWidth = objectAligned.Width / 2.0;
        var halfHeight = objectAligned.Height / 2.0;
        var ux = axisDx / axisLength;
        var uy = axisDy / axisLength;
        var vx = -uy;
        var vy = ux;

        var p1 = new[] { Round2(centerX - (ux * halfWidth) - (vx * halfHeight)), Round2(centerY - (uy * halfWidth) - (vy * halfHeight)) };
        var p2 = new[] { Round2(centerX + (ux * halfWidth) - (vx * halfHeight)), Round2(centerY + (uy * halfWidth) - (vy * halfHeight)) };
        var p3 = new[] { Round2(centerX + (ux * halfWidth) + (vx * halfHeight)), Round2(centerY + (uy * halfWidth) + (vy * halfHeight)) };
        var p4 = new[] { Round2(centerX - (ux * halfWidth) + (vx * halfHeight)), Round2(centerY - (uy * halfWidth) + (vy * halfHeight)) };

        var axisHalf = Math.Min(objectAligned.Width * 0.6, 250.0);
        var axisStart = new[] { Round2(centerX - (ux * axisHalf)), Round2(centerY - (uy * axisHalf)) };
        var axisEnd = new[] { Round2(centerX + (ux * axisHalf)), Round2(centerY + (uy * axisHalf)) };
        var angleDeg = Round2(Math.Atan2(uy, ux) * (180.0 / Math.PI));

        var request = new DrawingDebugOverlayRequest
        {
            Group = "selected-mark-part-axis-geometry",
            ClearGroupFirst = true,
            Shapes = new List<DrawingDebugShape>
            {
                new()
                {
                    Kind = "polygon",
                    ViewId = view.GetIdentifier().ID,
                    Points = new List<double[]> { p1, p2, p3, p4 },
                    Color = "Green",
                    LineType = "DashDot"
                },
                new()
                {
                    Kind = "line",
                    ViewId = view.GetIdentifier().ID,
                    X1 = axisStart[0],
                    Y1 = axisStart[1],
                    X2 = axisEnd[0],
                    Y2 = axisEnd[1],
                    Color = "Cyan",
                    LineType = "Solid"
                },
                new()
                {
                    Kind = "cross",
                    ViewId = view.GetIdentifier().ID,
                    X1 = Round2(centerX),
                    Y1 = Round2(centerY),
                    Size = 30,
                    Color = "Yellow"
                },
                new()
                {
                    Kind = "text",
                    ViewId = view.GetIdentifier().ID,
                    X1 = Round2(centerX + 35),
                    Y1 = Round2(centerY + 25),
                    Text = $"partAxis={angleDeg} deg",
                    Color = "Yellow",
                    TextHeight = 1.5
                }
            }
        };

        var overlayResult = debugOverlayApi.DrawOverlay(JsonSerializer.Serialize(request));
        WriteJson(new
        {
            markId = mark.GetIdentifier().ID,
            viewId = view.GetIdentifier().ID,
            modelId = modelId.Value,
            centerX = Round2(centerX),
            centerY = Round2(centerY),
            objectWidth = Round2(objectAligned.Width),
            objectHeight = Round2(objectAligned.Height),
            partAxisAngleDeg = angleDeg,
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
