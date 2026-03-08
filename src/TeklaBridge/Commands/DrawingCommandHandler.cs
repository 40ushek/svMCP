using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Tekla.Structures.Drawing;
using Tekla.Structures.Drawing.UI;
using Tekla.Structures.Model;
using TeklaMcpServer.Api.Drawing;

namespace TeklaBridge.Commands;

internal sealed class DrawingCommandHandler : ICommandHandler
{
    private const string NoActiveDrawingErrorJson = "{\"error\":\"No drawing is currently open\"}";
    private const string NoActiveDrawingWithPeriodErrorJson = "{\"error\":\"No drawing is currently open.\"}";
    private const string NoActiveDrawingShortErrorJson = "{\"error\":\"No active drawing\"}";
    private const string NoMatchingModelIdsInDrawingErrorJson = "{\"error\":\"None of the specified model IDs were found in the active drawing\"}";

    private readonly Model _model;
    private readonly TextWriter _output;

    public DrawingCommandHandler(Model model, TextWriter output)
    {
        _model = model;
        _output = output;
    }

    public bool TryHandle(string command, string[] args)
    {
        switch (command)
        {
            case "list_drawings":
            case "find_drawings":
            case "open_drawing":
            case "close_drawing":
            case "export_drawings_pdf":
            case "find_drawings_by_properties":
                return TryHandleDrawingCatalogCommands(command, args);

            case "create_ga_drawing":
            case "create_single_part_drawing":
            case "create_assembly_drawing":
                return TryHandleDrawingCreationCommands(command, args);

            case "select_drawing_objects":
            case "filter_drawing_objects":
            case "set_mark_content":
            case "get_drawing_context":
                return TryHandleDrawingInteractionCommands(command, args);

            case "get_drawing_views":
            case "move_view":
            case "set_view_scale":
            case "fit_views_to_sheet":
                return TryHandleViewCommands(command, args);

            case "get_drawing_dimensions":
            case "move_dimension":
            case "create_dimension":
            case "delete_dimension":
                return TryHandleDimensionCommands(command, args);

            case "get_part_geometry_in_view":
            case "get_grid_axes":
            case "get_drawing_parts":
                return TryHandleGeometryCommands(command, args);

            case "arrange_marks":
            case "create_part_marks":
            case "delete_all_marks":
            case "resolve_mark_overlaps":
            case "get_drawing_marks":
                return TryHandleMarkCommands(command, args);

            default:
                return false;
        }
    }

    // ── Private helpers ────────────────────────────────────────────────────

    private bool TryHandleDrawingCatalogCommands(string command, string[] args)
    {
        var api = new TeklaDrawingQueryApi();

        switch (command)
        {
            case "list_drawings":
            {
                var drawings = MapBasicDrawings(api.ListDrawings());

                WriteDrawingsList(drawings);
                return true;
            }

            case "find_drawings":
            {
                var parseResult = DrawingCommandParsers.ParseFindDrawingsRequest(args);
                if (!parseResult.IsValid)
                {
                    WriteError(parseResult.Error);
                    return true;
                }

                var drawings = MapBasicDrawings(
                    api.FindDrawings(parseResult.Request.NameContains, parseResult.Request.MarkContains));

                WriteDrawingsList(drawings);
                return true;
            }

            case "open_drawing":
            {
                var parseResult = DrawingCommandParsers.ParseOpenDrawingRequest(args);
                if (!parseResult.IsValid)
                {
                    WriteError(parseResult.Error);
                    return true;
                }

                var result = api.OpenDrawing(parseResult.Request.RequestedGuid);
                if (!result.Found)
                {
                    WriteDrawingFailure("Drawing not found", result.RequestedGuid);
                    return true;
                }

                if (!result.Opened)
                {
                    WriteDrawingFailure("Failed to open drawing", result.RequestedGuid);
                    return true;
                }

                WriteOpenedDrawing(
                    result.RequestedGuid,
                    result.Drawing.Name,
                    result.Drawing.Mark,
                    result.Drawing.Type);
                return true;
            }

            case "close_drawing":
            {
                var result = api.CloseActiveDrawing();
                if (!result.HasActiveDrawing)
                {
                    _output.WriteLine(NoActiveDrawingErrorJson);
                    return true;
                }

                if (!result.Closed)
                {
                    WriteDrawingFailure(
                        "Failed to close active drawing",
                        result.Drawing.Guid,
                        result.Drawing.Name,
                        result.Drawing.Mark,
                        result.Drawing.Type);
                    return true;
                }

                WriteClosedDrawing(
                    result.Drawing.Guid,
                    result.Drawing.Name,
                    result.Drawing.Mark,
                    result.Drawing.Type);
                return true;
            }

            case "export_drawings_pdf":
            {
                var modelInfo = _model.GetInfo();
                var defaultOutputDirectory = Path.Combine(modelInfo.ModelPath, "PlotFiles");
                var parseResult = DrawingCommandParsers.ParseExportDrawingsPdfRequest(args, defaultOutputDirectory);
                if (!parseResult.IsValid)
                {
                    WriteError(parseResult.Error);
                    return true;
                }

                var result = api.ExportDrawingsPdf(
                    parseResult.Request.RequestedGuids,
                    parseResult.Request.OutputDirectory);
                WriteExportDrawingsPdfResult(result);
                return true;
            }

            case "find_drawings_by_properties":
            {
                var parseResult = DrawingCommandParsers.ParseFindDrawingsByPropertiesRequest(args);
                if (!parseResult.IsValid)
                {
                    WriteError(parseResult.Error);
                    return true;
                }

                var drawings = MapBasicDrawings(
                    api.FindDrawingsByProperties(parseResult.Request.Filters),
                    includeStatus: true);

                WriteDrawingsList(drawings);
                return true;
            }

            default:
                return false;
        }
    }

    private bool TryHandleDrawingCreationCommands(string command, string[] args)
    {
        var api = new TeklaDrawingCreationApi(_model);

        switch (command)
        {
            case "create_ga_drawing":
            {
                var parseResult = DrawingCommandParsers.ParseGaDrawingCreationRequest(args);
                if (!parseResult.IsValid)
                {
                    WriteError(parseResult.Error);
                    return true;
                }

                var result = api.CreateGaDrawing(
                    parseResult.Request.ViewName,
                    parseResult.Request.DrawingProperties,
                    parseResult.Request.OpenDrawing);
                if (!result.Created)
                {
                    WriteGaDrawingCreationFailure(result.ErrorDetails, parseResult.Request.ViewName);
                    return true;
                }

                WriteGaDrawingCreationResult(result);
                return true;
            }

            case "create_single_part_drawing":
            {
                return TryCreateModelObjectDrawing(
                    api,
                    args,
                    static (api, modelObjectId, drawingProperties, openDrawing) =>
                        api.CreateSinglePartDrawing(modelObjectId, drawingProperties, openDrawing));
            }

            case "create_assembly_drawing":
            {
                return TryCreateModelObjectDrawing(
                    api,
                    args,
                    static (api, modelObjectId, drawingProperties, openDrawing) =>
                        api.CreateAssemblyDrawing(modelObjectId, drawingProperties, openDrawing));
            }

            default:
                return false;
        }
    }

    private bool TryHandleDrawingInteractionCommands(string command, string[] args)
    {
        TeklaDrawingInteractionApi? interactionApi = null;
        TeklaDrawingInteractionApi GetInteractionApi() => interactionApi ??= new TeklaDrawingInteractionApi(_model);
        TeklaDrawingMarkApi? markApi = null;
        TeklaDrawingMarkApi GetMarkApi() => markApi ??= new TeklaDrawingMarkApi(_model);

        switch (command)
        {
            case "select_drawing_objects":
            {
                var parseResult = DrawingCommandParsers.ParseSelectDrawingObjectsRequest(args);
                if (!parseResult.IsValid)
                {
                    WriteError(parseResult.Error);
                    return true;
                }

                var result = GetInteractionApi().SelectObjectsByModelIds(parseResult.Request.TargetModelIds);
                if (result.SelectedDrawingObjectIds.Count == 0)
                {
                    _output.WriteLine(NoMatchingModelIdsInDrawingErrorJson);
                    return true;
                }

                WriteSelectDrawingObjectsResult(result);
                return true;
            }

            case "filter_drawing_objects":
            {
                var parseResult = DrawingCommandParsers.ParseFilterDrawingObjectsRequest(args);
                if (!parseResult.IsValid)
                {
                    WriteError(parseResult.Error);
                    return true;
                }

                if (!EnsureActiveDrawing())
                {
                    return true;
                }

                var result = GetInteractionApi().FilterObjects(
                    parseResult.Request.ObjectType,
                    parseResult.Request.SpecificType);
                if (!result.IsKnownType)
                {
                    WriteUnknownDrawingTypeError(parseResult.Request.ObjectType);
                    return true;
                }

                WriteFilterDrawingObjectsResult(result);
                return true;
            }

            case "set_mark_content":
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

                var result = GetMarkApi().SetMarkContent(parseResult.Request);
                WriteSetMarkContentResult(result);
                return true;
            }

            case "get_drawing_context":
            {
                if (!EnsureActiveDrawing())
                {
                    return true;
                }

                var result = GetInteractionApi().GetDrawingContext();
                WriteDrawingContextResult(result);
                return true;
            }

            default:
                return false;
        }
    }

    private bool TryHandleViewCommands(string command, string[] args)
    {
        var api = new TeklaDrawingViewApi(_model);

        switch (command)
        {
            case "get_drawing_views":
            {
                var result = api.GetViews();
                WriteGetDrawingViewsResult(result);
                return true;
            }

            case "move_view":
            {
                var parseResult = DrawingCommandParsers.ParseMoveViewRequest(args);
                if (!parseResult.IsValid)
                {
                    WriteError(parseResult.Error);
                    return true;
                }

                var result   = api.MoveView(
                    parseResult.Request.ViewId,
                    parseResult.Request.Dx,
                    parseResult.Request.Dy,
                    parseResult.Request.Absolute);
                WriteMoveViewResult(result);
                return true;
            }

            case "set_view_scale":
            {
                var parseResult = DrawingCommandParsers.ParseSetViewScaleRequest(args);
                if (!parseResult.IsValid)
                {
                    WriteError(parseResult.Error);
                    return true;
                }

                var result = api.SetViewScale(parseResult.Request.ViewIds, parseResult.Request.Scale);
                WriteSetViewScaleResult(result);
                return true;
            }

            case "fit_views_to_sheet":
            {
                var request = DrawingCommandParsers.ParseFitViewsToSheetRequest(args);

                var result = api.FitViewsToSheet(request.Margin, request.Gap, request.TitleBlockHeight);
                WriteFitViewsToSheetResult(result);
                return true;
            }

            default:
                return false;
        }
    }

    private bool TryHandleDimensionCommands(string command, string[] args)
    {
        var api = new TeklaDrawingDimensionsApi(_model);

        switch (command)
        {
            case "get_drawing_dimensions":
            {
                var viewId = DrawingCommandParsers.ParseOptionalViewId(args);

                var result = api.GetDimensions(viewId);
                WriteGetDimensionsResult(result);
                return true;
            }

            case "move_dimension":
            {
                var parseResult = DrawingCommandParsers.ParseMoveDimensionRequest(args);
                if (!parseResult.IsValid)
                {
                    WriteError(parseResult.Error);
                    return true;
                }
                var result = api.MoveDimension(parseResult.Request.DimensionId, parseResult.Request.Delta);
                WriteMoveDimensionResult(result);
                return true;
            }

            case "create_dimension":
            {
                var parseResult = DrawingCommandParsers.ParseCreateDimensionRequest(args);
                if (!parseResult.IsValid)
                {
                    WriteError(parseResult.Error);
                    return true;
                }

                var result = api.CreateDimension(
                    parseResult.Request.ViewId,
                    parseResult.Request.Points,
                    parseResult.Request.Direction,
                    parseResult.Request.Distance,
                    parseResult.Request.AttributesFile);
                WriteCreateDimensionResult(result);
                return true;
            }

            case "delete_dimension":
            {
                var parseResult = DrawingCommandParsers.ParseDeleteDimensionRequest(args);
                if (!parseResult.IsValid)
                {
                    WriteError(parseResult.Error);
                    return true;
                }

                var result = api.DeleteDimension(parseResult.Request.DimensionId);
                if (!result.HasActiveDrawing)
                {
                    _output.WriteLine(NoActiveDrawingWithPeriodErrorJson);
                    return true;
                }

                WriteDeleteDimensionResult(result);
                return true;
            }

            default:
                return false;
        }
    }

    private bool TryHandleGeometryCommands(string command, string[] args)
    {
        TeklaDrawingPartGeometryApi? partGeometryApi = null;
        TeklaDrawingPartGeometryApi GetPartGeometryApi() => partGeometryApi ??= new TeklaDrawingPartGeometryApi(_model);
        TeklaDrawingGridApi? gridApi = null;
        TeklaDrawingGridApi GetGridApi() => gridApi ??= new TeklaDrawingGridApi();
        TeklaDrawingPartsApi? partsApi = null;
        TeklaDrawingPartsApi GetPartsApi() => partsApi ??= new TeklaDrawingPartsApi(_model);

        switch (command)
        {
            case "get_part_geometry_in_view":
            {
                var parseResult = DrawingCommandParsers.ParsePartGeometryInViewRequest(args);
                if (!parseResult.IsValid)
                {
                    WriteError(parseResult.Error);
                    return true;
                }
                var pgResult = GetPartGeometryApi().GetPartGeometryInView(
                    parseResult.Request.ViewId,
                    parseResult.Request.ModelId);
                WritePartGeometryInViewResult(pgResult);
                return true;
            }

            case "get_grid_axes":
            {
                var parseResult = DrawingCommandParsers.ParseGridAxesRequest(args);
                if (!parseResult.IsValid)
                {
                    WriteError(parseResult.Error);
                    return true;
                }
                var gaResult = GetGridApi().GetGridAxes(parseResult.Request.ViewId);
                WriteGridAxesResult(gaResult);
                return true;
            }

            case "get_drawing_parts":
            {
                var result = GetPartsApi().GetDrawingParts();
                WriteGetDrawingPartsResult(result);
                return true;
            }

            default:
                return false;
        }
    }

    private bool TryHandleMarkCommands(string command, string[] args)
    {
        TeklaDrawingMarkApi? api = null;
        TeklaDrawingMarkApi GetMarkApi() => api ??= new TeklaDrawingMarkApi(_model);

        switch (command)
        {
            case "arrange_marks":
            {
                var parseResult = DrawingCommandParsers.ParseArrangeMarksGap(args);
                if (!parseResult.IsValid)
                {
                    WriteError(parseResult.Error);
                    return true;
                }

                var result = GetMarkApi().ArrangeMarks(parseResult.Value);
                WriteMarkArrangementResult(result);
                return true;
            }

            case "create_part_marks":
            {
                var parseRequest = DrawingCommandParsers.ParseCreatePartMarksRequest(args);

                var result = GetMarkApi().CreatePartMarks(
                    parseRequest.ContentAttributesCsv,
                    parseRequest.MarkAttributesFile,
                    parseRequest.FrameType,
                    parseRequest.ArrowheadType);
                WriteCreatePartMarksResult(result);
                return true;
            }

            case "delete_all_marks":
            {
                if (!EnsureActiveDrawing(NoActiveDrawingShortErrorJson))
                {
                    return true;
                }

                var result = GetMarkApi().DeleteAllMarks();
                WriteDeleteAllMarksResult(result);
                return true;
            }

            case "resolve_mark_overlaps":
            {
                var parseResult = DrawingCommandParsers.ParseResolveMarkOverlapsMargin(args);
                if (!parseResult.IsValid)
                {
                    WriteError(parseResult.Error);
                    return true;
                }
                var result = GetMarkApi().ResolveMarkOverlaps(parseResult.Value);
                WriteMarkArrangementResult(result);
                return true;
            }

            case "get_drawing_marks":
            {
                var viewId = DrawingCommandParsers.ParseOptionalViewId(args);

                var result = GetMarkApi().GetMarks(viewId);
                WriteGetMarksResult(result);
                return true;
            }

            default:
                return false;
        }
    }

    private bool EnsureActiveDrawing(string noActiveDrawingJson)
    {
        if (new DrawingHandler().GetActiveDrawing() != null)
            return true;

        _output.WriteLine(noActiveDrawingJson);
        return false;
    }

    private bool EnsureActiveDrawing()
    {
        return EnsureActiveDrawing(NoActiveDrawingErrorJson);
    }

    private bool TryCreateModelObjectDrawing(
        TeklaDrawingCreationApi api,
        string[] args,
        Func<TeklaDrawingCreationApi, int, string, bool, DrawingCreationResult> createDrawing)
    {
        var parseResult = DrawingCommandParsers.ParseModelObjectDrawingCreationRequest(args);
        if (!parseResult.IsValid)
        {
            WriteError(parseResult.Error);
            return true;
        }

        var result = createDrawing(
            api,
            parseResult.Request.ModelObjectId,
            parseResult.Request.DrawingProperties,
            parseResult.Request.OpenDrawing);
        WriteDrawingCreationResult(result);
        return true;
    }

    private void WriteError(string message)
    {
        WriteJson(new { error = message });
    }

    private void WriteDrawingsList(IEnumerable<object> drawings)
    {
        WriteJson(drawings);
    }

    private void WriteDrawingCreationResult(DrawingCreationResult result)
    {
        WriteJson(new
        {
            created = result.Created,
            opened = result.Opened,
            drawingId = result.DrawingId,
            drawingType = result.DrawingType,
            modelObjectId = result.ModelObjectId,
            drawingProperties = result.DrawingProperties
        });
    }

    private void WriteGaDrawingCreationFailure(string details, string viewName)
    {
        WriteJson(new
        {
            error = "Failed to create GA drawing",
            details,
            viewName
        });
    }

    private void WriteGaDrawingCreationResult(GaDrawingCreationResult result)
    {
        WriteJson(new
        {
            created = true,
            drawingType = "GA",
            viewName = result.ViewName,
            drawingProperties = result.DrawingProperties,
            openDrawing = result.OpenDrawing
        });
    }

    private void WriteDrawingFailure(string error, string? guid)
    {
        WriteJson(new
        {
            error,
            guid
        });
    }

    private void WriteDrawingFailure(
        string error,
        string? guid,
        string? name,
        string? mark,
        string? type)
    {
        WriteJson(new
        {
            error,
            guid,
            name,
            mark,
            type
        });
    }

    private void WriteOpenedDrawing(string? guid, string? name, string? mark, string? type)
    {
        WriteJson(new
        {
            opened = true,
            guid,
            name,
            mark,
            type
        });
    }

    private void WriteClosedDrawing(string? guid, string? name, string? mark, string? type)
    {
        WriteJson(new
        {
            closed = true,
            guid,
            name,
            mark,
            type
        });
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

    private void WriteDeleteAllMarksResult(DeleteAllMarksResult result)
    {
        WriteJson(new
        {
            deletedCount = result.DeletedCount
        });
    }

    private void WriteExportDrawingsPdfResult(ExportDrawingsPdfResult result)
    {
        WriteJson(new
        {
            exportedCount = result.ExportedFiles.Count,
            exportedFiles = result.ExportedFiles,
            failedToExport = result.FailedToExport,
            missingGuids = result.MissingGuids,
            outputDirectory = result.OutputDirectory
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
                bbox = new { minX = m.BboxMinX, minY = m.BboxMinY, maxX = m.BboxMaxX, maxY = m.BboxMaxY },
                placingType = m.PlacingType,
                placingX = m.PlacingX,
                placingY = m.PlacingY,
                properties = m.Properties.Select(p => new { name = p.Name, value = p.Value })
            })
        });
    }

    private void WriteGetDimensionsResult(GetDimensionsResult result)
    {
        WriteJson(new
        {
            total = result.Total,
            dimensions = result.Dimensions.Select(d => new
            {
                id = d.Id,
                type = d.Type,
                distance = d.Distance,
                segments = d.Segments.Select(s => new
                {
                    id = s.Id,
                    startX = s.StartX,
                    startY = s.StartY,
                    endX = s.EndX,
                    endY = s.EndY
                })
            })
        });
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

    private void WriteGetDrawingViewsResult(DrawingViewsResult result)
    {
        WriteJson(new
        {
            sheetWidth = result.SheetWidth,
            sheetHeight = result.SheetHeight,
            views = result.Views.Select(v => new
            {
                id = v.Id,
                viewType = v.ViewType,
                name = v.Name,
                originX = v.OriginX,
                originY = v.OriginY,
                scale = v.Scale,
                width = v.Width,
                height = v.Height
            })
        });
    }

    private void WriteSelectDrawingObjectsResult(SelectDrawingObjectsResult result)
    {
        WriteJson(new
        {
            selectedCount = result.SelectedDrawingObjectIds.Count,
            selectedDrawingObjectIds = result.SelectedDrawingObjectIds,
            selectedModelIds = result.SelectedModelIds
        });
    }

    private void WriteUnknownDrawingTypeError(string objectType)
    {
        WriteJson(new
        {
            error = $"Unknown drawing type: {objectType}",
            hint = "Use Tekla.Structures.Drawing type names, e.g. Mark, Part, DimensionBase, Text."
        });
    }

    private void WriteFilterDrawingObjectsResult(FilterDrawingObjectsResult result)
    {
        WriteJson(MapDrawingObjects(result.Objects));
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

    private void WriteDrawingContextResult(DrawingContextResult result)
    {
        WriteJson(new
        {
            drawing = new
            {
                guid = result.Drawing.Guid,
                name = result.Drawing.Name,
                mark = result.Drawing.Mark,
                type = result.Drawing.Type,
                status = result.Drawing.Status
            },
            selectedCount = result.SelectedObjects.Count,
            selectedObjects = MapDrawingObjects(result.SelectedObjects)
        });
    }

    private void WriteMoveViewResult(MoveViewResult result)
    {
        WriteJson(new
        {
            moved = result.Moved,
            viewId = result.ViewId,
            oldOriginX = result.OldOriginX,
            oldOriginY = result.OldOriginY,
            newOriginX = result.NewOriginX,
            newOriginY = result.NewOriginY
        });
    }

    private void WriteSetViewScaleResult(SetViewScaleResult result)
    {
        WriteJson(new
        {
            updatedCount = result.UpdatedCount,
            updatedIds = result.UpdatedIds,
            scale = result.Scale
        });
    }

    private void WriteFitViewsToSheetResult(FitViewsResult result)
    {
        WriteJson(new
        {
            optimalScale = result.OptimalScale,
            sheetWidth = result.SheetWidth,
            sheetHeight = result.SheetHeight,
            arranged = result.Arranged,
            views = result.Views.Select(v => new
            {
                id = v.Id,
                viewType = v.ViewType,
                originX = v.OriginX,
                originY = v.OriginY
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

    private void WriteMoveDimensionResult(MoveDimensionResult result)
    {
        WriteJson(new
        {
            moved = result.Moved,
            dimensionId = result.DimensionId,
            newDistance = result.NewDistance
        });
    }

    private void WriteCreateDimensionResult(CreateDimensionResult result)
    {
        WriteJson(new
        {
            created = result.Created,
            dimensionId = result.DimensionId,
            viewId = result.ViewId,
            pointCount = result.PointCount,
            error = result.Error
        });
    }

    private void WriteDeleteDimensionResult(DeleteDimensionResult result)
    {
        WriteJson(new
        {
            deleted = result.Deleted,
            dimensionId = result.DimensionId
        });
    }

    private void WriteJson<T>(T payload)
    {
        _output.WriteLine(JsonSerializer.Serialize(payload));
    }

    private static IEnumerable<object> MapBasicDrawings(
        IEnumerable<DrawingInfo> drawings,
        bool includeStatus = false)
    {
        if (!includeStatus)
        {
            return drawings.Select(d => (object)new
            {
                guid = d.Guid,
                name = d.Name,
                mark = d.Mark,
                type = d.Type
            });
        }

        return drawings.Select(d => (object)new
        {
            guid = d.Guid,
            name = d.Name,
            mark = d.Mark,
            type = d.Type,
            status = d.Status
        });
    }

    private static IEnumerable<object> MapDrawingObjects(
        IEnumerable<DrawingObjectItem> drawingObjects)
    {
        return drawingObjects.Select(x => (object)new
        {
            id = x.Id,
            type = x.Type,
            modelId = x.ModelId
        });
    }

}

