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
        switch (command)
        {
            case "list_drawings":
            {
                var api = new TeklaDrawingQueryApi();
                var drawings = api.ListDrawings().Select(d => new
                {
                    guid = d.Guid,
                    name = d.Name,
                    mark = d.Mark,
                    type = d.Type
                });

                _output.WriteLine(JsonSerializer.Serialize(drawings));
                return true;
            }

            case "find_drawings":
            {
                var nameContains = args.Length > 1 ? args[1] : string.Empty;
                var markContains = args.Length > 2 ? args[2] : string.Empty;

                if (string.IsNullOrWhiteSpace(nameContains) && string.IsNullOrWhiteSpace(markContains))
                {
                    _output.WriteLine("{\"error\":\"Provide at least one filter: nameContains or markContains\"}");
                    return true;
                }

                var api = new TeklaDrawingQueryApi();
                var drawings = api.FindDrawings(nameContains, markContains).Select(d => new
                {
                    guid = d.Guid,
                    name = d.Name,
                    mark = d.Mark,
                    type = d.Type
                });

                _output.WriteLine(JsonSerializer.Serialize(drawings));
                return true;
            }

            case "open_drawing":
            {
                if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
                {
                    _output.WriteLine("{\"error\":\"Missing drawing GUID\"}");
                    return true;
                }

                if (!Guid.TryParse(args[1], out var requestedGuid))
                {
                    _output.WriteLine("{\"error\":\"Invalid drawing GUID format\"}");
                    return true;
                }

                var api = new TeklaDrawingQueryApi();
                var result = api.OpenDrawing(requestedGuid);
                if (!result.Found)
                {
                    _output.WriteLine(JsonSerializer.Serialize(new
                    {
                        error = "Drawing not found",
                        guid = result.RequestedGuid
                    }));
                    return true;
                }

                if (!result.Opened)
                {
                    _output.WriteLine(JsonSerializer.Serialize(new
                    {
                        error = "Failed to open drawing",
                        guid = result.RequestedGuid
                    }));
                    return true;
                }

                _output.WriteLine(JsonSerializer.Serialize(new
                {
                    opened = true,
                    guid = result.RequestedGuid,
                    name = result.Drawing.Name,
                    mark = result.Drawing.Mark,
                    type = result.Drawing.Type
                }));
                return true;
            }

            case "close_drawing":
            {
                var api = new TeklaDrawingQueryApi();
                var result = api.CloseActiveDrawing();
                if (!result.HasActiveDrawing)
                {
                    _output.WriteLine("{\"error\":\"No drawing is currently open\"}");
                    return true;
                }

                if (!result.Closed)
                {
                    _output.WriteLine(JsonSerializer.Serialize(new
                    {
                        error = "Failed to close active drawing",
                        guid = result.Drawing.Guid,
                        name = result.Drawing.Name,
                        mark = result.Drawing.Mark,
                        type = result.Drawing.Type
                    }));
                    return true;
                }

                _output.WriteLine(JsonSerializer.Serialize(new
                {
                    closed = true,
                    guid = result.Drawing.Guid,
                    name = result.Drawing.Name,
                    mark = result.Drawing.Mark,
                    type = result.Drawing.Type
                }));
                return true;
            }

            case "export_drawings_pdf":
            {
                if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
                {
                    _output.WriteLine("{\"error\":\"Missing drawing GUID list (comma-separated)\"}");
                    return true;
                }

                var requestedGuids = args[1]
                    .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                if (requestedGuids.Count == 0)
                {
                    _output.WriteLine("{\"error\":\"No valid drawing GUIDs provided\"}");
                    return true;
                }

                var modelInfo = _model.GetInfo();
                var outputDir = (args.Length > 2 && !string.IsNullOrWhiteSpace(args[2]))
                    ? args[2]
                    : Path.Combine(modelInfo.ModelPath, "PlotFiles");

                var api = new TeklaDrawingQueryApi();
                var result = api.ExportDrawingsPdf(requestedGuids.ToList(), outputDir);

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
                if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
                {
                    _output.WriteLine("{\"error\":\"Missing filters JSON\"}");
                    return true;
                }

                var filters = DrawingPropertyFilterParser.Parse(args[1]);
                if (filters.Count == 0)
                {
                    _output.WriteLine("{\"error\":\"filtersJson must be a JSON array like [{\\\"property\\\":\\\"Name\\\",\\\"value\\\":\\\"GA\\\"}]\"}");
                    return true;
                }

                var api = new TeklaDrawingQueryApi();
                var drawings = api.FindDrawingsByProperties(filters).Select(d => new
                {
                    guid = d.Guid,
                    name = d.Name,
                    mark = d.Mark,
                    type = d.Type,
                    status = d.Status
                });

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
                var drawingProperties = args.Length > 1 && !string.IsNullOrWhiteSpace(args[1]) ? args[1] : "standard";
                var openDrawing = true;
                if (args.Length > 2 && !string.IsNullOrWhiteSpace(args[2]) && bool.TryParse(args[2], out var parsedOpen))
                    openDrawing = parsedOpen;

                var viewName = args.Length > 3 ? args[3] : string.Empty;
                if (string.IsNullOrWhiteSpace(viewName))
                {
                    _output.WriteLine("{\"error\":\"viewName is required for this Tekla version. Pass a saved model view name.\"}");
                    return true;
                }

                var api = new TeklaDrawingCreationApi(_model);
                var result = api.CreateGaDrawing(viewName, drawingProperties, openDrawing);
                if (!result.Created)
                {
                    _output.WriteLine(JsonSerializer.Serialize(new { error = "Failed to create GA drawing", details = result.ErrorDetails, viewName }));
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
                var parseResult = DrawingCommandParsers.ParseModelObjectDrawingCreationRequest(
                    args.Length > 1 ? args[1] : string.Empty,
                    args.Length > 2 ? args[2] : string.Empty,
                    args.Length > 3 ? args[3] : string.Empty);
                if (!parseResult.IsValid)
                {
                    _output.WriteLine(JsonSerializer.Serialize(new { error = parseResult.Error }));
                    return true;
                }

                var api = new TeklaDrawingCreationApi(_model);
                var result = api.CreateSinglePartDrawing(
                    parseResult.Request.ModelObjectId,
                    parseResult.Request.DrawingProperties,
                    parseResult.Request.OpenDrawing);
                _output.WriteLine(JsonSerializer.Serialize(new
                {
                    created = result.Created,
                    opened = result.Opened,
                    drawingId = result.DrawingId,
                    drawingType = result.DrawingType,
                    modelObjectId = result.ModelObjectId,
                    drawingProperties = result.DrawingProperties
                }));
                return true;
            }

            case "create_assembly_drawing":
            {
                var parseResult = DrawingCommandParsers.ParseModelObjectDrawingCreationRequest(
                    args.Length > 1 ? args[1] : string.Empty,
                    args.Length > 2 ? args[2] : string.Empty,
                    args.Length > 3 ? args[3] : string.Empty);
                if (!parseResult.IsValid)
                {
                    _output.WriteLine(JsonSerializer.Serialize(new { error = parseResult.Error }));
                    return true;
                }

                var api = new TeklaDrawingCreationApi(_model);
                var result = api.CreateAssemblyDrawing(
                    parseResult.Request.ModelObjectId,
                    parseResult.Request.DrawingProperties,
                    parseResult.Request.OpenDrawing);
                _output.WriteLine(JsonSerializer.Serialize(new
                {
                    created = result.Created,
                    opened = result.Opened,
                    drawingId = result.DrawingId,
                    drawingType = result.DrawingType,
                    modelObjectId = result.ModelObjectId,
                    drawingProperties = result.DrawingProperties
                }));
                return true;
            }

            default:
                return false;
        }
    }

    private bool TryHandleDrawingInteractionCommands(string command, string[] args)
    {
        switch (command)
        {
            case "select_drawing_objects":
            {
                if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
                {
                    _output.WriteLine("{\"error\":\"Missing model object IDs. Use comma-separated IDs.\"}");
                    return true;
                }

                var targetModelIds = DrawingCommandParsers.ParseIntList(args[1]).ToHashSet();
                if (targetModelIds.Count == 0)
                {
                    _output.WriteLine("{\"error\":\"No valid model object IDs provided\"}");
                    return true;
                }

                var api = new TeklaDrawingInteractionApi(_model);
                var result = api.SelectObjectsByModelIds(targetModelIds.ToList());
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
                if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
                {
                    _output.WriteLine("{\"error\":\"Missing objectType. Example: Mark, Part, DimensionBase\"}");
                    return true;
                }

                var objectType = args[1];
                var specificType = args.Length > 2 ? args[2] : string.Empty;

                var drawingHandler = new DrawingHandler();
                if (drawingHandler.GetActiveDrawing() == null)
                {
                    _output.WriteLine("{\"error\":\"No drawing is currently open\"}");
                    return true;
                }

                var api = new TeklaDrawingInteractionApi(_model);
                var result = api.FilterObjects(objectType, specificType);
                if (!result.IsKnownType)
                {
                    _output.WriteLine(JsonSerializer.Serialize(new
                    {
                        error = $"Unknown drawing type: {objectType}",
                        hint = "Use Tekla.Structures.Drawing type names, e.g. Mark, Part, DimensionBase, Text."
                    }));
                    return true;
                }

                _output.WriteLine(JsonSerializer.Serialize(result.Objects.Select(x => new
                {
                    id = x.Id,
                    type = x.Type,
                    modelId = x.ModelId
                })));
                return true;
            }

            case "set_mark_content":
            {
                var parseResult = DrawingCommandParsers.ParseSetMarkContentRequest(
                    args.Length > 1 ? args[1] : string.Empty,
                    args.Length > 2 ? args[2] : string.Empty,
                    args.Length > 3 ? args[3] : string.Empty,
                    args.Length > 4 ? args[4] : string.Empty,
                    args.Length > 5 ? args[5] : string.Empty);
                if (!parseResult.IsValid)
                {
                    _output.WriteLine(JsonSerializer.Serialize(new { error = parseResult.Error }));
                    return true;
                }

                var drawingHandler = new DrawingHandler();
                var activeDrawing = drawingHandler.GetActiveDrawing();
                if (activeDrawing == null)
                {
                    _output.WriteLine("{\"error\":\"No drawing is currently open\"}");
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
                var drawingHandler = new DrawingHandler();
                if (drawingHandler.GetActiveDrawing() == null)
                {
                    _output.WriteLine("{\"error\":\"No drawing is currently open\"}");
                    return true;
                }

                var api = new TeklaDrawingInteractionApi(_model);
                var result = api.GetDrawingContext();
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
                    selectedObjects = result.SelectedObjects.Select(x => new
                    {
                        id = x.Id,
                        type = x.Type,
                        modelId = x.ModelId
                    })
                }));
                return true;
            }

            default:
                return false;
        }
    }

    private bool TryHandleViewCommands(string command, string[] args)
    {
        switch (command)
        {
            case "get_drawing_views":
            {
                var api    = new TeklaDrawingViewApi(_model);
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
                if (args.Length < 4)
                {
                    _output.WriteLine("{\"error\":\"Usage: move_view <viewId> <dx> <dy> [abs]\"}");
                    return true;
                }

                if (!int.TryParse(args[1], out var viewId))
                {
                    _output.WriteLine("{\"error\":\"viewId must be an integer\"}");
                    return true;
                }

                if (!double.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var dx) ||
                    !double.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var dy))
                {
                    _output.WriteLine("{\"error\":\"dx and dy must be numbers\"}");
                    return true;
                }

                var absolute = args.Length > 4 && string.Equals(args[4], "abs", StringComparison.OrdinalIgnoreCase);
                var api      = new TeklaDrawingViewApi(_model);
                var result   = api.MoveView(viewId, dx, dy, absolute);
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
                if (args.Length < 3)
                {
                    _output.WriteLine("{\"error\":\"Usage: set_view_scale <viewIdsCsv> <scale>\"}");
                    return true;
                }

                var ids = DrawingCommandParsers.ParseIntList(args[1]);
                if (ids.Count == 0)
                {
                    _output.WriteLine("{\"error\":\"No valid view IDs provided\"}");
                    return true;
                }

                if (!double.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var scale) || scale <= 0)
                {
                    _output.WriteLine("{\"error\":\"scale must be a positive number\"}");
                    return true;
                }

                var api    = new TeklaDrawingViewApi(_model);
                var result = api.SetViewScale(ids, scale);
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
                var margin      = args.Length > 1 && double.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var m)  ? m  : 10.0;
                var gap         = args.Length > 2 && double.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var g)  ? g  : 8.0;
                var titleBlockH = args.Length > 3 && double.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var tb) ? tb : 0.0;

                var api    = new TeklaDrawingViewApi(_model);
                var result = api.FitViewsToSheet(margin, gap, titleBlockH);
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
        switch (command)
        {
            case "get_drawing_dimensions":
            {
                int? viewId = null;
                if (args.Length > 1 && int.TryParse(args[1], out var vid)) viewId = vid;

                var api    = new TeklaDrawingDimensionsApi(_model);
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
                if (args.Length < 3 ||
                    !int.TryParse(args[1], out var dimId) ||
                    !double.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var delta))
                {
                    _output.WriteLine("{\"error\":\"Usage: move_dimension <dimensionId> <delta>\"}");
                    return true;
                }
                var api    = new TeklaDrawingDimensionsApi(_model);
                var result = api.MoveDimension(dimId, delta);
                _output.WriteLine(JsonSerializer.Serialize(new
                {
                    moved       = result.Moved,
                    dimensionId = result.DimensionId,
                    newDistance = result.NewDistance
                }));
                return true;
            }

            case "create_dimension":
            {
                // args: viewId, pointsJson, direction, distance, attributesFile
                if (args.Length < 4 || !int.TryParse(args[1], out var cdViewId))
                {
                    _output.WriteLine("{\"error\":\"Usage: create_dimension <viewId> <pointsJson> <direction> <distance> [attributesFile]\"}");
                    return true;
                }
                var pointsJson    = args.Length > 2 ? args[2] : "[]";
                var cdDirection   = args.Length > 3 ? args[3] : "horizontal";
                var cdDistance    = args.Length > 4 && double.TryParse(args[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 50.0;
                var cdAttrFile    = args.Length > 5 ? args[5] : string.Empty;

                double[] points;
                try { points = JsonSerializer.Deserialize<double[]>(pointsJson) ?? Array.Empty<double>(); }
                catch { _output.WriteLine("{\"error\":\"pointsJson must be a JSON array of numbers\"}"); return true; }

                var api    = new TeklaDrawingDimensionsApi(_model);
                var result = api.CreateDimension(cdViewId, points, cdDirection, cdDistance, cdAttrFile);
                _output.WriteLine(JsonSerializer.Serialize(new
                {
                    created     = result.Created,
                    dimensionId = result.DimensionId,
                    viewId      = result.ViewId,
                    pointCount  = result.PointCount,
                    error       = result.Error
                }));
                return true;
            }

            case "delete_dimension":
            {
                // args: dimensionId
                if (args.Length < 2 || !int.TryParse(args[1], out var ddId))
                {
                    _output.WriteLine("{\"error\":\"Usage: delete_dimension <dimensionId>\"}");
                    return true;
                }

                var api = new TeklaDrawingDimensionsApi(_model);
                var result = api.DeleteDimension(ddId);
                if (!result.HasActiveDrawing)
                {
                    _output.WriteLine("{\"error\":\"No drawing is currently open.\"}");
                    return true;
                }

                _output.WriteLine(JsonSerializer.Serialize(new { deleted = result.Deleted, dimensionId = result.DimensionId }));
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
                // args: viewId, modelId
                if (args.Length < 3
                    || !int.TryParse(args[1], out var pgViewId)
                    || !int.TryParse(args[2], out var pgModelId))
                {
                    _output.WriteLine("{\"error\":\"Usage: get_part_geometry_in_view <viewId> <modelId>\"}");
                    return true;
                }
                var pgApi    = new TeklaDrawingPartGeometryApi(_model);
                var pgResult = pgApi.GetPartGeometryInView(pgViewId, pgModelId);
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
                if (args.Length < 2 || !int.TryParse(args[1], out var gaViewId))
                {
                    _output.WriteLine("{\"error\":\"Usage: get_grid_axes <viewId>\"}");
                    return true;
                }
                var gaApi    = new TeklaDrawingGridApi();
                var gaResult = gaApi.GetGridAxes(gaViewId);
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
        switch (command)
        {
            case "arrange_marks":
            {
                double gap = 2.0;
                if (args.Length > 1)
                {
                    if (!double.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out gap))
                    {
                        _output.WriteLine("{\"error\":\"gap must be a number\"}");
                        return true;
                    }

                    if (gap < 0)
                    {
                        _output.WriteLine("{\"error\":\"gap must be >= 0\"}");
                        return true;
                    }
                }

                var api    = new TeklaDrawingMarkApi(_model);
                var result = api.ArrangeMarks(gap);
                _output.WriteLine(JsonSerializer.Serialize(new
                {
                    marksMovedCount   = result.MarksMovedCount,
                    movedIds          = result.MovedIds,
                    iterations        = result.Iterations,
                    remainingOverlaps = result.RemainingOverlaps
                }));
                return true;
            }

            case "create_part_marks":
            {
                var contentAttributesCsv = args.Length > 1 ? args[1] : string.Empty;
                var markAttributesFile   = args.Length > 2 ? args[2] : string.Empty;
                var frameType            = args.Length > 3 ? args[3] : string.Empty;
                var arrowheadType        = args.Length > 4 ? args[4] : string.Empty;

                var api    = new TeklaDrawingMarkApi(_model);
                var result = api.CreatePartMarks(contentAttributesCsv, markAttributesFile, frameType, arrowheadType);
                _output.WriteLine(JsonSerializer.Serialize(new
                {
                    createdCount     = result.CreatedCount,
                    skippedCount     = result.SkippedCount,
                    createdMarkIds   = result.CreatedMarkIds,
                    attributesLoaded = result.AttributesLoaded
                }));
                return true;
            }

            case "delete_all_marks":
            {
                var activeDrawing = new DrawingHandler().GetActiveDrawing();
                if (activeDrawing == null)
                {
                    _output.WriteLine("{\"error\":\"No active drawing\"}");
                    return true;
                }

                var api = new TeklaDrawingMarkApi(_model);
                var result = api.DeleteAllMarks();
                _output.WriteLine(JsonSerializer.Serialize(new { deletedCount = result.DeletedCount }));
                return true;
            }

            case "resolve_mark_overlaps":
            {
                double margin = 2.0;
                if (args.Length > 1)
                {
                    if (!double.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out margin))
                    {
                        _output.WriteLine("{\"error\":\"margin must be a number\"}");
                        return true;
                    }

                    if (margin < 0)
                    {
                        _output.WriteLine("{\"error\":\"margin must be >= 0\"}");
                        return true;
                    }
                }
                var api    = new TeklaDrawingMarkApi(_model);
                var result = api.ResolveMarkOverlaps(margin);
                _output.WriteLine(JsonSerializer.Serialize(new
                {
                    marksMovedCount   = result.MarksMovedCount,
                    movedIds          = result.MovedIds,
                    iterations        = result.Iterations,
                    remainingOverlaps = result.RemainingOverlaps
                }));
                return true;
            }

            case "get_drawing_marks":
            {
                int? viewId = null;
                if (args.Length > 1 && int.TryParse(args[1], out var vid)) viewId = vid;

                var api    = new TeklaDrawingMarkApi(_model);
                var result = api.GetMarks(viewId);
                _output.WriteLine(JsonSerializer.Serialize(new
                {
                    total    = result.Total,
                    overlaps = result.Overlaps.Select(o => new { idA = o.IdA, idB = o.IdB }),
                    marks    = result.Marks.Select(m => new
                    {
                        id         = m.Id,
                        viewId     = m.ViewId,
                        modelId    = m.ModelId,
                        insertionX = m.InsertionX,
                        insertionY = m.InsertionY,
                        bbox        = new { minX = m.BboxMinX, minY = m.BboxMinY, maxX = m.BboxMaxX, maxY = m.BboxMaxY },
                        placingType = m.PlacingType,
                        placingX    = m.PlacingX,
                        placingY    = m.PlacingY,
                        properties  = m.Properties.Select(p => new { name = p.Name, value = p.Value })
                    })
                }));
                return true;
            }

            default:
                return false;
        }
    }

}
