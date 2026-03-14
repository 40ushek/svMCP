using System;
using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.Drawing;
using TeklaMcpServer.Api.Drawing;

namespace TeklaBridge.Commands;

internal sealed partial class DrawingCommandHandler
{
    private bool TryHandleMarkCommands(string command, string[] args)
    {
        TeklaDrawingMarkApi? api = null;
        TeklaDrawingMarkApi GetMarkApi() => api ??= new TeklaDrawingMarkApi(_model);

        switch (command)
        {
            case "arrange_marks":
                return HandleArrangeMarks(GetMarkApi(), args);

            case "create_part_marks":
                return HandleCreatePartMarks(GetMarkApi(), args);

            case "set_mark_content":
                return HandleSetMarkContent(GetMarkApi(), args);

            case "delete_all_marks":
                return HandleDeleteAllMarks(GetMarkApi());

            case "resolve_mark_overlaps":
                return HandleResolveMarkOverlaps(GetMarkApi(), args);

            case "get_drawing_marks":
                return HandleGetDrawingMarks(GetMarkApi(), args);

            case "test_move_selected_mark":
                return HandleTestMoveSelectedMark(args);

            default:
                return false;
        }
    }

    private bool HandleArrangeMarks(TeklaDrawingMarkApi api, string[] args)
    {
        var parseResult = DrawingCommandParsers.ParseArrangeMarksGap(args);
        if (!parseResult.IsValid)
        {
            WriteError(parseResult.Error);
            return true;
        }

        if (!EnsureActiveDrawing())
        {
            return true;
        }

        var result = api.ArrangeMarks(parseResult.Value);
        WriteMarkArrangementResult(result);
        return true;
    }

    private bool HandleCreatePartMarks(TeklaDrawingMarkApi api, string[] args)
    {
        var parseRequest = DrawingCommandParsers.ParseCreatePartMarksRequest(args);
        var result = api.CreatePartMarks(
            parseRequest.ContentAttributesCsv,
            parseRequest.MarkAttributesFile,
            parseRequest.FrameType,
            parseRequest.ArrowheadType);
        WriteCreatePartMarksResult(result);
        return true;
    }

    private bool HandleSetMarkContent(TeklaDrawingMarkApi api, string[] args)
    {
        var parseResult = DrawingCommandParsers.ParseSetMarkContentRequest(args);
        if (!parseResult.IsValid)
        {
            WriteError(parseResult.Error);
            return true;
        }

        if (!EnsureActiveDrawing())
        {
            return true;
        }

        var result = api.SetMarkContent(parseResult.Request);
        WriteSetMarkContentResult(result);
        return true;
    }

    private bool HandleDeleteAllMarks(TeklaDrawingMarkApi api)
    {
        if (!EnsureActiveDrawing())
        {
            return true;
        }

        var result = api.DeleteAllMarks();
        WriteDeleteAllMarksResult(result);
        return true;
    }

    private bool HandleResolveMarkOverlaps(TeklaDrawingMarkApi api, string[] args)
    {
        var parseResult = DrawingCommandParsers.ParseResolveMarkOverlapsMargin(args);
        if (!parseResult.IsValid)
        {
            WriteError(parseResult.Error);
            return true;
        }

        if (!EnsureActiveDrawing())
        {
            return true;
        }

        var result = api.ResolveMarkOverlaps(parseResult.Value);
        WriteMarkArrangementResult(result);
        return true;
    }

    private bool HandleGetDrawingMarks(TeklaDrawingMarkApi api, string[] args)
    {
        var viewId = DrawingCommandParsers.ParseOptionalViewId(args);
        var result = api.GetMarks(viewId);
        WriteGetMarksResult(result);
        return true;
    }

    private void WriteMarkArrangementResult(ResolveMarksResult result)
    {
        WriteJson(new
        {
            marksMovedCount = result.MarksMovedCount,
            movedIds = result.MovedIds,
            iterations = result.Iterations,
            remainingOverlaps = result.RemainingOverlaps
        });
    }

    private bool HandleTestMoveSelectedMark(string[] args)
    {
        if (!EnsureActiveDrawing())
            return true;

        var moveDelta = 50.0;
        if (args.Length >= 2 && double.TryParse(args[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            moveDelta = parsed;

        var drawingHandler = new DrawingHandler();
        var selected = drawingHandler.GetDrawingObjectSelector().GetSelected();
        Mark? mark = null;
        while (selected.MoveNext())
        {
            if (selected.Current is Mark m) { mark = m; break; }
        }

        if (mark == null)
        {
            WriteError("No mark selected");
            return true;
        }

        var geometry = MarkGeometryHelper.Build(mark, _model);
        var beforeInsX = mark.InsertionPoint.X;
        var beforeInsY = mark.InsertionPoint.Y;
        var beforeCenterX = geometry.CenterX;
        var beforeCenterY = geometry.CenterY;

        double dx = 0, dy = moveDelta;
        if (geometry.HasAxis)
        {
            dx = geometry.AxisDx * moveDelta;
            dy = geometry.AxisDy * moveDelta;
        }

        var insertionPoint = mark.InsertionPoint;
        insertionPoint.X += dx;
        insertionPoint.Y += dy;
        mark.InsertionPoint = insertionPoint;
        var modifyResult = mark.Modify();

        var afterInsX = mark.InsertionPoint.X;
        var afterInsY = mark.InsertionPoint.Y;
        var afterGeometry = MarkGeometryHelper.Build(mark, _model);

        WriteJson(new
        {
            markId = -1,
            placingType = mark.Placing?.GetType().Name,
            hasAxis = geometry.HasAxis,
            axisDx = Math.Round(geometry.AxisDx, 4),
            axisDy = Math.Round(geometry.AxisDy, 4),
            moveDx = Math.Round(dx, 3),
            moveDy = Math.Round(dy, 3),
            before = new { insX = beforeInsX, insY = beforeInsY, centerX = beforeCenterX, centerY = beforeCenterY },
            modifyResult,
            after = new { insX = afterInsX, insY = afterInsY, centerX = afterGeometry.CenterX, centerY = afterGeometry.CenterY },
            insertionChanged = Math.Abs(afterInsX - beforeInsX) > 0.05 || Math.Abs(afterInsY - beforeInsY) > 0.05,
            centerChanged = Math.Abs(afterGeometry.CenterX - beforeCenterX) > 0.05 || Math.Abs(afterGeometry.CenterY - beforeCenterY) > 0.05
        });
        return true;
    }

    private void WriteCreatePartMarksResult(CreateMarksResult result)
    {
        WriteJson(new
        {
            createdCount = result.CreatedCount,
            skippedCount = result.SkippedCount,
            createdMarkIds = result.CreatedMarkIds,
            attributesLoaded = result.AttributesLoaded
        });
    }

    private void WriteSetMarkContentResult(SetMarkContentResult result)
    {
        WriteJson(new
        {
            updatedCount = result.UpdatedObjectIds.Count,
            failedCount = result.FailedObjectIds.Count,
            updatedObjectIds = result.UpdatedObjectIds,
            failedObjectIds = result.FailedObjectIds,
            errors = result.Errors
        });
    }

    private void WriteDeleteAllMarksResult(DeleteAllMarksResult result)
    {
        WriteJson(new
        {
            deletedCount = result.DeletedCount
        });
    }

    private void WriteGetMarksResult(GetMarksResult result)
    {
        WriteJson(new
        {
            total = result.Total,
            overlaps = result.Overlaps.Select(o => new { idA = o.IdA, idB = o.IdB }),
            marks = result.Marks.Select(m => new
            {
                id = m.Id,
                viewId = m.ViewId,
                modelId = m.ModelId,
                insertionX = m.InsertionX,
                insertionY = m.InsertionY,
                centerX = m.CenterX,
                centerY = m.CenterY,
                bbox = new { minX = m.BboxMinX, minY = m.BboxMinY, maxX = m.BboxMaxX, maxY = m.BboxMaxY },
                placingType = m.PlacingType,
                placingX = m.PlacingX,
                placingY = m.PlacingY,
                angle = m.Angle,
                rotationAngle = m.RotationAngle,
                textAlignment = m.TextAlignment,
                axis = m.Axis == null ? null : new
                {
                    startX = m.Axis.StartX,
                    startY = m.Axis.StartY,
                    endX = m.Axis.EndX,
                    endY = m.Axis.EndY,
                    dx = m.Axis.Dx,
                    dy = m.Axis.Dy,
                    length = m.Axis.Length,
                    angleDeg = m.Axis.AngleDeg,
                    isReliable = m.Axis.IsReliable
                },
                objectAlignedBbox = m.ObjectAlignedBoundingBox == null ? null : new
                {
                    width = m.ObjectAlignedBoundingBox.Width,
                    height = m.ObjectAlignedBoundingBox.Height,
                    angleToAxis = m.ObjectAlignedBoundingBox.AngleToAxis,
                    centerX = m.ObjectAlignedBoundingBox.CenterX,
                    centerY = m.ObjectAlignedBoundingBox.CenterY,
                    minX = m.ObjectAlignedBoundingBox.MinX,
                    minY = m.ObjectAlignedBoundingBox.MinY,
                    maxX = m.ObjectAlignedBoundingBox.MaxX,
                    maxY = m.ObjectAlignedBoundingBox.MaxY,
                    corners = m.ObjectAlignedBoundingBox.Corners
                },
                resolvedGeometry = m.ResolvedGeometry == null ? null : new
                {
                    source = m.ResolvedGeometry.Source,
                    isReliable = m.ResolvedGeometry.IsReliable,
                    width = m.ResolvedGeometry.Width,
                    height = m.ResolvedGeometry.Height,
                    centerX = m.ResolvedGeometry.CenterX,
                    centerY = m.ResolvedGeometry.CenterY,
                    minX = m.ResolvedGeometry.MinX,
                    minY = m.ResolvedGeometry.MinY,
                    maxX = m.ResolvedGeometry.MaxX,
                    maxY = m.ResolvedGeometry.MaxY,
                    angleDeg = m.ResolvedGeometry.AngleDeg,
                    corners = m.ResolvedGeometry.Corners
                },
                arrowHead = new
                {
                    type = m.ArrowHead.Type,
                    position = m.ArrowHead.Position,
                    height = m.ArrowHead.Height,
                    width = m.ArrowHead.Width
                },
                leaderLines = m.LeaderLines.Select(l => new
                {
                    type = l.Type,
                    startX = l.StartX,
                    startY = l.StartY,
                    endX = l.EndX,
                    endY = l.EndY,
                    elbowPoints = l.ElbowPoints
                }),
                properties = m.Properties.Select(p => new { name = p.Name, value = p.Value })
            })
        });
    }
}
