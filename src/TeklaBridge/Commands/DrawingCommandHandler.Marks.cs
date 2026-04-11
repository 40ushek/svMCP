using System.Linq;
using System.Diagnostics;
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
            case "arrange_marks_no_collisions":
                return HandleArrangeMarksNoCollisions(GetMarkApi(), args);

            case "arrange_marks":
                return HandleArrangeMarks(GetMarkApi(), args);

            case "move_mark":
                return HandleMoveMark(GetMarkApi(), args);

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

            default:
                return false;
        }
    }

    private bool HandleArrangeMarksNoCollisions(TeklaDrawingMarkApi api, string[] args)
    {
        var gap = 2.0;
        var margin = 2.0;
        var maxPasses = 3;

        if (args.Length > 1 &&
            (!double.TryParse(args[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out gap) || gap < 0))
        {
            WriteError("gap must be a number >= 0");
            return true;
        }

        if (args.Length > 2 &&
            (!double.TryParse(args[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out margin) || margin < 0))
        {
            WriteError("margin must be a number >= 0");
            return true;
        }

        if (args.Length > 3 &&
            (!int.TryParse(args[3], out maxPasses) || maxPasses < 1))
        {
            WriteError("maxPasses must be an integer >= 1");
            return true;
        }

        if (!EnsureActiveDrawing())
            return true;

        var total = Stopwatch.StartNew();

        var arrangeSw = Stopwatch.StartNew();
        var firstArrange = api.ArrangeMarks(gap);
        arrangeSw.Stop();

        var arrangePasses = 1;
        var arrangeMovedTotal = firstArrange.MarksMovedCount;
        var arrangeIterationsTotal = firstArrange.Iterations;
        var reportedRemaining = firstArrange.RemainingOverlaps;

        var verifySw = Stopwatch.StartNew();
        var initialActualOverlaps = api.GetMarks(null).Overlaps.Count;
        var actualRemaining = initialActualOverlaps;
        verifySw.Stop();

        var resolvePasses = 0;
        var resolveMovedTotal = 0;
        var resolveIterationsTotal = 0;
        var stopReason = actualRemaining <= 0 ? "no_overlaps_after_arrange" : "max_passes_reached";

        var resolveSw = Stopwatch.StartNew();
        while (actualRemaining > 0 && resolvePasses < maxPasses)
        {
            var overlapsBeforePass = actualRemaining;
            var passMargin = margin + (resolvePasses * 1.5);
            var resolve = api.ResolveMarkOverlaps(passMargin);
            resolvePasses++;
            resolveMovedTotal += resolve.MarksMovedCount;
            resolveIterationsTotal += resolve.Iterations;
            reportedRemaining = resolve.RemainingOverlaps;

            verifySw.Start();
            actualRemaining = api.GetMarks(null).Overlaps.Count;
            verifySw.Stop();

            if (actualRemaining <= 0)
            {
                stopReason = "resolved";
                break;
            }

            if (resolve.MarksMovedCount <= 0)
            {
                stopReason = "no_marks_moved";
                break;
            }

            if (actualRemaining >= overlapsBeforePass)
            {
                stopReason = "no_progress";
                break;
            }
        }
        resolveSw.Stop();

        TeklaBridge.PerfTrace.Write(
            "bridge-mark",
            "arrange_marks_no_collisions",
            total.ElapsedMilliseconds,
            $"gap={gap.ToString(System.Globalization.CultureInfo.InvariantCulture)} margin={margin.ToString(System.Globalization.CultureInfo.InvariantCulture)} maxPasses={maxPasses} arrangePasses={arrangePasses} arrangeMoved={arrangeMovedTotal} resolvePasses={resolvePasses} resolveMoved={resolveMovedTotal} reportedRemaining={reportedRemaining} actualRemaining={actualRemaining} stopReason={stopReason} arrangeMs={arrangeSw.ElapsedMilliseconds} resolveMs={resolveSw.ElapsedMilliseconds} verifyMs={verifySw.ElapsedMilliseconds}");

        WriteJson(new
        {
            arrange = new
            {
                passes = arrangePasses,
                marksMovedCount = arrangeMovedTotal,
                movedIds = firstArrange.MovedIds,
                iterations = arrangeIterationsTotal,
                remainingOverlaps = reportedRemaining
            },
            resolve = new
            {
                passes = resolvePasses,
                movedCount = resolveMovedTotal,
                iterations = resolveIterationsTotal,
                finalRemainingOverlaps = reportedRemaining
            },
            verify = new
            {
                initialOverlaps = initialActualOverlaps,
                finalOverlaps = actualRemaining,
                stopReason
            },
            timings = new
            {
                arrangeMs = arrangeSw.ElapsedMilliseconds,
                resolveMs = resolveSw.ElapsedMilliseconds,
                verifyMs = verifySw.ElapsedMilliseconds,
                totalMs = total.ElapsedMilliseconds
            }
        });
        return true;
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

        var total = Stopwatch.StartNew();
        var result = api.ArrangeMarks(parseResult.Value);
        TeklaBridge.PerfTrace.Write("bridge-mark", "arrange_marks", total.ElapsedMilliseconds, $"gap={parseResult.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)} moved={result.MarksMovedCount} overlaps={result.RemainingOverlaps} iterations={result.Iterations}");
        WriteMarkArrangementResult(result);
        return true;
    }

    private bool HandleMoveMark(TeklaDrawingMarkApi api, string[] args)
    {
        var parseResult = DrawingCommandParsers.ParseMoveMarkRequest(args);
        if (!parseResult.IsValid)
        {
            WriteError(parseResult.Error);
            return true;
        }

        if (!EnsureActiveDrawing())
            return true;

        var total = Stopwatch.StartNew();
        var result = api.MoveMark(
            parseResult.Request.MarkId,
            parseResult.Request.InsertionX,
            parseResult.Request.InsertionY);
        TeklaBridge.PerfTrace.Write(
            "bridge-mark",
            "move_mark",
            total.ElapsedMilliseconds,
            $"markId={result.MarkId} x={result.InsertionX.ToString(System.Globalization.CultureInfo.InvariantCulture)} y={result.InsertionY.ToString(System.Globalization.CultureInfo.InvariantCulture)} moved={result.Moved}");
        WriteJson(new
        {
            moved = result.Moved,
            markId = result.MarkId,
            insertionX = result.InsertionX,
            insertionY = result.InsertionY
        });
        return true;
    }

    private bool HandleCreatePartMarks(TeklaDrawingMarkApi api, string[] args)
    {
        var parseRequest = DrawingCommandParsers.ParseCreatePartMarksRequest(args);
        if (!EnsureActiveDrawing())
        {
            return true;
        }

        var total = Stopwatch.StartNew();
        var result = api.CreatePartMarks(
            parseRequest.ContentAttributesCsv,
            parseRequest.MarkAttributesFile,
            parseRequest.FrameType,
            parseRequest.ArrowheadType);
        TeklaBridge.PerfTrace.Write("bridge-mark", "create_part_marks", total.ElapsedMilliseconds, $"created={result.CreatedCount} skipped={result.SkippedCount}");
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

        var total = Stopwatch.StartNew();
        var result = api.ResolveMarkOverlaps(parseResult.Value);
        TeklaBridge.PerfTrace.Write("bridge-mark", "resolve_mark_overlaps", total.ElapsedMilliseconds, $"margin={parseResult.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)} moved={result.MarksMovedCount} overlaps={result.RemainingOverlaps} iterations={result.Iterations}");
        WriteMarkArrangementResult(result);
        return true;
    }

    private bool HandleGetDrawingMarks(TeklaDrawingMarkApi api, string[] args)
    {
        var viewId = DrawingCommandParsers.ParseOptionalViewId(args);
        var total = Stopwatch.StartNew();
        var result = api.GetMarks(viewId);
        TeklaBridge.PerfTrace.Write("bridge-mark", "get_drawing_marks", total.ElapsedMilliseconds, $"viewId={(viewId.HasValue ? viewId.Value.ToString() : "all")} total={result.Total} overlaps={result.Overlaps.Count}");
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
                    axisDx = m.ResolvedGeometry.AxisDx,
                    axisDy = m.ResolvedGeometry.AxisDy,
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
