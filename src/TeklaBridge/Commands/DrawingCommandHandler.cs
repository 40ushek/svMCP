using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.Drawing.UI;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Model;
using TeklaMcpServer.Api.Drawing;

namespace TeklaBridge.Commands;

internal sealed class DrawingCommandHandler : ICommandHandler
{
    private const string NoActiveDrawingErrorJson = "{\"error\":\"No drawing is currently open\"}";

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

                _output.WriteLine(JsonSerializer.Serialize(drawings));
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

                _output.WriteLine(JsonSerializer.Serialize(drawings));
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

                _output.WriteLine(JsonSerializer.Serialize(new
                {
                    exportedCount = result.ExportedFiles.Count,
                    exportedFiles = result.ExportedFiles,
                    failedToExport = result.FailedToExport,
                    missingGuids = result.MissingGuids,
                    outputDirectory = result.OutputDirectory
                }));
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

                _output.WriteLine(JsonSerializer.Serialize(drawings));
                return true;
            }

            default:
                return false;
        }
    }

    private bool TryHandleDrawingCreationCommands(string command, string[] args)
    {
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

                var api = new TeklaDrawingCreationApi(_model);
                var result = api.CreateGaDrawing(
                    parseResult.Request.ViewName,
                    parseResult.Request.DrawingProperties,
                    parseResult.Request.OpenDrawing);
                if (!result.Created)
                {
                    _output.WriteLine(JsonSerializer.Serialize(new
                    {
                        error = "Failed to create GA drawing",
                        details = result.ErrorDetails,
                        viewName = parseResult.Request.ViewName
                    }));
                    return true;
                }

                _output.WriteLine(JsonSerializer.Serialize(new
                {
                    created = true,
                    drawingType = "GA",
                    viewName = result.ViewName,
                    drawingProperties = result.DrawingProperties,
                    openDrawing = result.OpenDrawing
                }));
                return true;
            }

            case "create_single_part_drawing":
            {
                return TryCreateModelObjectDrawing(
                    args,
                    static (api, modelObjectId, drawingProperties, openDrawing) =>
                        api.CreateSinglePartDrawing(modelObjectId, drawingProperties, openDrawing));
            }

            case "create_assembly_drawing":
            {
                return TryCreateModelObjectDrawing(
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
                    _output.WriteLine("{\"error\":\"None of the specified model IDs were found in the active drawing\"}");
                    return true;
                }

                _output.WriteLine(JsonSerializer.Serialize(new
                {
                    selectedCount = result.SelectedDrawingObjectIds.Count,
                    selectedDrawingObjectIds = result.SelectedDrawingObjectIds,
                    selectedModelIds = result.SelectedModelIds
                }));
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
                    _output.WriteLine(JsonSerializer.Serialize(new
                    {
                        error = $"Unknown drawing type: {parseResult.Request.ObjectType}",
                        hint = "Use Tekla.Structures.Drawing type names, e.g. Mark, Part, DimensionBase, Text."
                    }));
                    return true;
                }

                _output.WriteLine(JsonSerializer.Serialize(MapDrawingObjects(
                    result.Objects,
                    x => x.Id,
                    x => x.Type,
                    x => x.ModelId)));
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

                var api = new TeklaDrawingMarkApi(_model);
                var result = api.SetMarkContent(parseResult.Request);

                _output.WriteLine(JsonSerializer.Serialize(new
                {
                    updatedCount = result.UpdatedObjectIds.Count,
                    failedCount = result.FailedObjectIds.Count,
                    updatedObjectIds = result.UpdatedObjectIds,
                    failedObjectIds = result.FailedObjectIds,
                    errors = result.Errors
                }));
                return true;
            }

            case "get_drawing_context":
            {
                if (!EnsureActiveDrawing())
                {
                    return true;
                }

                var result = GetInteractionApi().GetDrawingContext();
                _output.WriteLine(JsonSerializer.Serialize(new
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
                    selectedObjects = MapDrawingObjects(
                        result.SelectedObjects,
                        x => x.Id,
                        x => x.Type,
                        x => x.ModelId)
                }));
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
                _output.WriteLine(JsonSerializer.Serialize(new
                {
                    sheetWidth  = result.SheetWidth,
                    sheetHeight = result.SheetHeight,
                    views       = result.Views.Select(v => new
                    {
                        id       = v.Id,
                        viewType = v.ViewType,
                        name     = v.Name,
                        originX  = v.OriginX,
                        originY  = v.OriginY,
                        scale    = v.Scale,
                        width    = v.Width,
                        height   = v.Height
                    })
                }));
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
                _output.WriteLine(JsonSerializer.Serialize(new
                {
                    moved      = result.Moved,
                    viewId     = result.ViewId,
                    oldOriginX = result.OldOriginX,
                    oldOriginY = result.OldOriginY,
                    newOriginX = result.NewOriginX,
                    newOriginY = result.NewOriginY
                }));
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
                _output.WriteLine(JsonSerializer.Serialize(new
                {
                    updatedCount = result.UpdatedCount,
                    updatedIds   = result.UpdatedIds,
                    scale        = result.Scale
                }));
                return true;
            }

            case "fit_views_to_sheet":
            {
                var request = DrawingCommandParsers.ParseFitViewsToSheetRequest(args);

                var result = api.FitViewsToSheet(request.Margin, request.Gap, request.TitleBlockHeight);
                _output.WriteLine(JsonSerializer.Serialize(new
                {
                    optimalScale = result.OptimalScale,
                    sheetWidth   = result.SheetWidth,
                    sheetHeight  = result.SheetHeight,
                    arranged     = result.Arranged,
                    views        = result.Views.Select(v => new { id = v.Id, viewType = v.ViewType, originX = v.OriginX, originY = v.OriginY })
                }));
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
                _output.WriteLine(JsonSerializer.Serialize(new
                {
                    total      = result.Total,
                    dimensions = result.Dimensions.Select(d => new
                    {
                        id       = d.Id,
                        type     = d.Type,
                        distance = d.Distance,
                        segments = d.Segments.Select(s => new
                        {
                            id     = s.Id,
                            startX = s.StartX,
                            startY = s.StartY,
                            endX   = s.EndX,
                            endY   = s.EndY
                        })
                    })
                }));
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
                    _output.WriteLine("{\"error\":\"No drawing is currently open.\"}");
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
                var pgApi    = new TeklaDrawingPartGeometryApi(_model);
                var pgResult = pgApi.GetPartGeometryInView(parseResult.Request.ViewId, parseResult.Request.ModelId);
                _output.WriteLine(JsonSerializer.Serialize(new
                {
                    success    = pgResult.Success,
                    viewId     = pgResult.ViewId,
                    modelId    = pgResult.ModelId,
                    startPoint = pgResult.StartPoint,
                    endPoint   = pgResult.EndPoint,
                    axisX      = pgResult.AxisX,
                    axisY      = pgResult.AxisY,
                    bboxMin    = pgResult.BboxMin,
                    bboxMax    = pgResult.BboxMax,
                    error      = pgResult.Error
                }));
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
                var gaApi    = new TeklaDrawingGridApi();
                var gaResult = gaApi.GetGridAxes(parseResult.Request.ViewId);
                _output.WriteLine(JsonSerializer.Serialize(new
                {
                    success = gaResult.Success,
                    viewId  = gaResult.ViewId,
                    axes    = gaResult.Axes.Select(a => new
                    {
                        label      = a.Label,
                        direction  = a.Direction,
                        coordinate = a.Coordinate,
                        startX     = a.StartX,
                        startY     = a.StartY,
                        endX       = a.EndX,
                        endY       = a.EndY
                    }),
                    error   = gaResult.Error
                }));
                return true;
            }

            case "get_drawing_parts":
            {
                var api    = new TeklaDrawingPartsApi(_model);
                var result = api.GetDrawingParts();
                _output.WriteLine(JsonSerializer.Serialize(new
                {
                    total = result.Total,
                    parts = result.Parts.Select(p => new
                    {
                        modelId     = p.ModelId,
                        type        = p.Type,
                        partPos     = p.PartPos,
                        assemblyPos = p.AssemblyPos,
                        profile     = p.Profile,
                        material    = p.Material,
                        name        = p.Name
                    })
                }));
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
                if (!EnsureActiveDrawing("{\"error\":\"No active drawing\"}"))
                {
                    return true;
                }

                var result = GetMarkApi().DeleteAllMarks();
                _output.WriteLine(JsonSerializer.Serialize(new { deletedCount = result.DeletedCount }));
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
        string[] args,
        Func<TeklaDrawingCreationApi, int, string, bool, DrawingCreationResult> createDrawing)
    {
        var parseResult = DrawingCommandParsers.ParseModelObjectDrawingCreationRequest(args);
        if (!parseResult.IsValid)
        {
            WriteError(parseResult.Error);
            return true;
        }

        var api = new TeklaDrawingCreationApi(_model);
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
        _output.WriteLine(JsonSerializer.Serialize(new { error = message }));
    }

    private void WriteDrawingCreationResult(DrawingCreationResult result)
    {
        _output.WriteLine(JsonSerializer.Serialize(new
        {
            created = result.Created,
            opened = result.Opened,
            drawingId = result.DrawingId,
            drawingType = result.DrawingType,
            modelObjectId = result.ModelObjectId,
            drawingProperties = result.DrawingProperties
        }));
    }

    private void WriteDrawingFailure(string error, string? guid)
    {
        _output.WriteLine(JsonSerializer.Serialize(new
        {
            error,
            guid
        }));
    }

    private void WriteDrawingFailure(
        string error,
        string? guid,
        string? name,
        string? mark,
        string? type)
    {
        _output.WriteLine(JsonSerializer.Serialize(new
        {
            error,
            guid,
            name,
            mark,
            type
        }));
    }

    private void WriteOpenedDrawing(string? guid, string? name, string? mark, string? type)
    {
        _output.WriteLine(JsonSerializer.Serialize(new
        {
            opened = true,
            guid,
            name,
            mark,
            type
        }));
    }

    private void WriteClosedDrawing(string? guid, string? name, string? mark, string? type)
    {
        _output.WriteLine(JsonSerializer.Serialize(new
        {
            closed = true,
            guid,
            name,
            mark,
            type
        }));
    }

    private void WriteMarkArrangementResult(ResolveMarksResult result)
    {
        _output.WriteLine(JsonSerializer.Serialize(new
        {
            marksMovedCount = result.MarksMovedCount,
            movedIds = result.MovedIds,
            iterations = result.Iterations,
            remainingOverlaps = result.RemainingOverlaps
        }));
    }

    private void WriteCreatePartMarksResult(CreateMarksResult result)
    {
        _output.WriteLine(JsonSerializer.Serialize(new
        {
            createdCount = result.CreatedCount,
            skippedCount = result.SkippedCount,
            createdMarkIds = result.CreatedMarkIds,
            attributesLoaded = result.AttributesLoaded
        }));
    }

    private void WriteGetMarksResult(GetMarksResult result)
    {
        _output.WriteLine(JsonSerializer.Serialize(new
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
        }));
    }

    private void WriteMoveDimensionResult(MoveDimensionResult result)
    {
        _output.WriteLine(JsonSerializer.Serialize(new
        {
            moved = result.Moved,
            dimensionId = result.DimensionId,
            newDistance = result.NewDistance
        }));
    }

    private void WriteCreateDimensionResult(CreateDimensionResult result)
    {
        _output.WriteLine(JsonSerializer.Serialize(new
        {
            created = result.Created,
            dimensionId = result.DimensionId,
            viewId = result.ViewId,
            pointCount = result.PointCount,
            error = result.Error
        }));
    }

    private void WriteDeleteDimensionResult(DeleteDimensionResult result)
    {
        _output.WriteLine(JsonSerializer.Serialize(new
        {
            deleted = result.Deleted,
            dimensionId = result.DimensionId
        }));
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

    private static IEnumerable<object> MapDrawingObjects<TDrawingObject>(
        IEnumerable<TDrawingObject> drawingObjects,
        Func<TDrawingObject, object?> idSelector,
        Func<TDrawingObject, string?> typeSelector,
        Func<TDrawingObject, object?> modelIdSelector)
    {
        return drawingObjects.Select(x => (object)new
        {
            id = idSelector(x),
            type = typeSelector(x),
            modelId = modelIdSelector(x)
        });
    }

}

