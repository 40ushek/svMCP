using System;
using System.Collections;
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
            {
                var drawingHandler = new DrawingHandler();
                var drawingEnumerator = drawingHandler.GetDrawings();
                var drawings = new List<object>();

                while (drawingEnumerator.MoveNext())
                {
                    var drawing = drawingEnumerator.Current;
                    drawings.Add(new
                    {
                        guid = drawing.GetIdentifier().GUID.ToString(),
                        name = drawing.Name,
                        mark = drawing.Mark,
                        type = drawing.GetType().Name
                    });
                }

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

                var drawingHandler = new DrawingHandler();
                var drawingEnumerator = drawingHandler.GetDrawings();
                var drawings = new List<object>();

                while (drawingEnumerator.MoveNext())
                {
                    var drawing = drawingEnumerator.Current;
                    if (!ContainsIgnoreCase(drawing.Name, nameContains)) continue;
                    if (!ContainsIgnoreCase(drawing.Mark, markContains)) continue;

                    drawings.Add(new
                    {
                        guid = drawing.GetIdentifier().GUID.ToString(),
                        name = drawing.Name,
                        mark = drawing.Mark,
                        type = drawing.GetType().Name
                    });
                }

                _output.WriteLine(JsonSerializer.Serialize(drawings));
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

                Directory.CreateDirectory(outputDir);

                var drawingHandler = new DrawingHandler();
                var drawingEnumerator = drawingHandler.GetDrawings();
                var exportedFiles = new List<string>();
                var failedToExport = new List<string>();
                var foundGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                var printAttributes = new DPMPrinterAttributes
                {
                    PrinterName = "Microsoft Print to PDF",
                    OutputType = DotPrintOutputType.PDF
                };

                while (drawingEnumerator.MoveNext())
                {
                    var drawing = drawingEnumerator.Current;
                    var guid = drawing.GetIdentifier().GUID.ToString();
                    if (!requestedGuids.Contains(guid)) continue;

                    foundGuids.Add(guid);
                    var fileName = $"{SanitizeFileName(drawing.Name)}_{SanitizeFileName(drawing.Mark)}.pdf";
                    var filePath = Path.Combine(outputDir, fileName);

                    if (drawingHandler.PrintDrawing(drawing, printAttributes, filePath))
                        exportedFiles.Add(filePath);
                    else
                        failedToExport.Add(guid);
                }

                var missingGuids = requestedGuids.Where(g => !foundGuids.Contains(g)).ToList();

                _output.WriteLine(JsonSerializer.Serialize(new
                {
                    exportedCount = exportedFiles.Count,
                    exportedFiles,
                    failedToExport,
                    missingGuids,
                    outputDirectory = outputDir
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

                var filters = ParseDrawingFilters(args[1]);
                if (filters.Count == 0)
                {
                    _output.WriteLine("{\"error\":\"filtersJson must be a JSON array like [{\\\"property\\\":\\\"Name\\\",\\\"value\\\":\\\"GA\\\"}]\"}");
                    return true;
                }

                var drawingHandler = new DrawingHandler();
                var drawingEnumerator = drawingHandler.GetDrawings();
                var drawings = new List<object>();

                while (drawingEnumerator.MoveNext())
                {
                    var drawing = drawingEnumerator.Current;
                    if (!MatchesAllFilters(drawing, filters)) continue;

                    drawings.Add(new
                    {
                        guid = drawing.GetIdentifier().GUID.ToString(),
                        name = drawing.Name,
                        mark = drawing.Mark,
                        type = drawing.GetType().Name,
                        status = drawing.UpToDateStatus.ToString()
                    });
                }

                _output.WriteLine(JsonSerializer.Serialize(drawings));
                return true;
            }

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

                if (!CreateGaDrawingViaMacro(viewName, drawingProperties, openDrawing, out var macroError))
                {
                    _output.WriteLine(JsonSerializer.Serialize(new { error = "Failed to create GA drawing", details = macroError, viewName }));
                    return true;
                }

                _output.WriteLine(JsonSerializer.Serialize(new { created = true, drawingType = "GA", viewName, drawingProperties, openDrawing }));
                return true;
            }

            case "select_drawing_objects":
            {
                if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
                {
                    _output.WriteLine("{\"error\":\"Missing model object IDs. Use comma-separated IDs.\"}");
                    return true;
                }

                var activeDrawing = new DrawingHandler().GetActiveDrawing();
                if (activeDrawing == null)
                {
                    _output.WriteLine("{\"error\":\"No drawing is currently open\"}");
                    return true;
                }

                var targetModelIds = ParseIntList(args[1]).ToHashSet();
                if (targetModelIds.Count == 0)
                {
                    _output.WriteLine("{\"error\":\"No valid model object IDs provided\"}");
                    return true;
                }

                var drawingObjectsToSelect = new ArrayList();
                var selectedDrawingObjectIds = new List<int>();
                var selectedModelIds = new List<int>();
                var allDrawingObjects = activeDrawing.GetSheet().GetAllObjects();
                while (allDrawingObjects.MoveNext())
                {
                    if (allDrawingObjects.Current is Tekla.Structures.Drawing.ModelObject drawingModelObject &&
                        targetModelIds.Contains(drawingModelObject.ModelIdentifier.ID))
                    {
                        drawingObjectsToSelect.Add(drawingModelObject);
                        selectedDrawingObjectIds.Add(drawingModelObject.GetIdentifier().ID);
                        selectedModelIds.Add(drawingModelObject.ModelIdentifier.ID);
                    }
                }

                if (drawingObjectsToSelect.Count == 0)
                {
                    _output.WriteLine("{\"error\":\"None of the specified model IDs were found in the active drawing\"}");
                    return true;
                }

                var drawingHandler = new DrawingHandler();
                var selector = drawingHandler.GetDrawingObjectSelector();
                selector.SelectObjects(drawingObjectsToSelect, false);
                activeDrawing.CommitChanges("(MCP) SelectDrawingObjects");

                _output.WriteLine(JsonSerializer.Serialize(new { selectedCount = drawingObjectsToSelect.Count, selectedDrawingObjectIds, selectedModelIds }));
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
                var targetType = ResolveDrawingType(objectType);
                if (targetType == null)
                {
                    _output.WriteLine(JsonSerializer.Serialize(new
                    {
                        error = $"Unknown drawing type: {objectType}",
                        hint = "Use Tekla.Structures.Drawing type names, e.g. Mark, Part, DimensionBase, Text."
                    }));
                    return true;
                }

                var drawingHandler = new DrawingHandler();
                var activeDrawing = drawingHandler.GetActiveDrawing();
                if (activeDrawing == null)
                {
                    _output.WriteLine("{\"error\":\"No drawing is currently open\"}");
                    return true;
                }

                var results = new List<object>();
                var drawingObjects = activeDrawing.GetSheet().GetAllObjects();
                while (drawingObjects.MoveNext())
                {
                    var drawingObject = drawingObjects.Current;
                    if (drawingObject == null || !targetType.IsInstanceOfType(drawingObject)) continue;

                    if (drawingObject is Mark mark && !string.IsNullOrWhiteSpace(specificType))
                    {
                        var markType = GetMarkType(mark);
                        if (!string.Equals(markType, specificType, StringComparison.OrdinalIgnoreCase)) continue;
                    }

                    results.Add(new
                    {
                        id = drawingObject.GetIdentifier().ID,
                        type = drawingObject.GetType().Name,
                        modelId = drawingObject is Tekla.Structures.Drawing.ModelObject dm ? dm.ModelIdentifier.ID : (int?)null
                    });
                }

                _output.WriteLine(JsonSerializer.Serialize(results));
                return true;
            }

            case "set_mark_content":
            {
                if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
                {
                    _output.WriteLine("{\"error\":\"Missing element IDs (drawing IDs or model IDs)\"}");
                    return true;
                }

                var targetIds = ParseIntList(args[1]).ToHashSet();
                if (targetIds.Count == 0)
                {
                    _output.WriteLine("{\"error\":\"No valid IDs provided\"}");
                    return true;
                }

                var contentElementsCsv = args.Length > 2 ? args[2] : string.Empty;
                var fontName = args.Length > 3 ? args[3] : string.Empty;
                var fontColorRaw = args.Length > 4 ? args[4] : string.Empty;
                var fontHeightRaw = args.Length > 5 ? args[5] : string.Empty;

                var requestedContentElements = string.IsNullOrWhiteSpace(contentElementsCsv)
                    ? new List<string>()
                    : contentElementsCsv.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();

                var updateContent = requestedContentElements.Count > 0;
                var updateFontName = !string.IsNullOrWhiteSpace(fontName);
                var updateFontColor = !string.IsNullOrWhiteSpace(fontColorRaw);
                var updateFontHeight = !string.IsNullOrWhiteSpace(fontHeightRaw);

                if (!updateContent && !updateFontName && !updateFontColor && !updateFontHeight)
                {
                    _output.WriteLine("{\"error\":\"No changes requested. Provide content elements and/or font attributes.\"}");
                    return true;
                }

                var parsedFontHeight = 0.0;
                if (updateFontHeight && (!double.TryParse(fontHeightRaw, out parsedFontHeight) || parsedFontHeight <= 0))
                {
                    _output.WriteLine("{\"error\":\"fontHeight must be a positive number\"}");
                    return true;
                }

                var parsedColor = DrawingColors.Black;
                if (updateFontColor && !Enum.TryParse(fontColorRaw, true, out parsedColor))
                {
                    _output.WriteLine("{\"error\":\"Invalid fontColor. Use DrawingColors enum values, e.g. Black, Red, Blue\"}");
                    return true;
                }

                var drawingHandler = new DrawingHandler();
                var activeDrawing = drawingHandler.GetActiveDrawing();
                if (activeDrawing == null)
                {
                    _output.WriteLine("{\"error\":\"No drawing is currently open\"}");
                    return true;
                }

                var updatedObjectIds = new List<int>();
                var failedObjectIds = new List<int>();
                var errors = new List<string>();
                var drawingObjects = activeDrawing.GetSheet().GetAllObjects();
                while (drawingObjects.MoveNext())
                {
                    if (drawingObjects.Current is not Mark mark) continue;

                    var drawingId = mark.GetIdentifier().ID;
                    var matches = targetIds.Contains(drawingId);

                    if (!matches)
                    {
                        var related = mark.GetRelatedObjects();
                        while (related.MoveNext())
                        {
                            if (related.Current is Tekla.Structures.Drawing.ModelObject relatedModelObject &&
                                targetIds.Contains(relatedModelObject.ModelIdentifier.ID))
                            {
                                matches = true;
                                break;
                            }
                        }
                    }

                    if (!matches) continue;

                    try
                    {
                        var content = mark.Attributes.Content;
                        var existingFont = default(FontAttributes);
                        var contentEnumerator = content.GetEnumerator();
                        if (contentEnumerator.MoveNext() && contentEnumerator.Current is PropertyElement existingProperty && existingProperty.Font != null)
                            existingFont = (FontAttributes)existingProperty.Font.Clone();

                        var newFont = existingFont != null ? (FontAttributes)existingFont.Clone() : new FontAttributes();
                        var fontChanged = false;

                        if (updateFontName && !string.Equals(newFont.Name, fontName, StringComparison.Ordinal)) { newFont.Name = fontName; fontChanged = true; }
                        if (updateFontHeight && Math.Abs(newFont.Height - parsedFontHeight) > 0.01) { newFont.Height = parsedFontHeight; fontChanged = true; }
                        if (updateFontColor && newFont.Color != parsedColor) { newFont.Color = parsedColor; fontChanged = true; }

                        if (updateContent)
                        {
                            content.Clear();
                            foreach (var attribute in requestedContentElements)
                            {
                                var element = CreateMarkPropertyElement(attribute);
                                if (element == null) { errors.Add($"Object {drawingId}: unsupported content attribute '{attribute}'."); continue; }
                                element.Font = (FontAttributes)newFont.Clone();
                                content.Add(element);
                            }
                        }
                        else if (fontChanged)
                        {
                            var existingElements = content.GetEnumerator();
                            while (existingElements.MoveNext())
                                if (existingElements.Current is PropertyElement propElement)
                                    propElement.Font = (FontAttributes)newFont.Clone();
                        }

                        if (mark.Modify()) updatedObjectIds.Add(drawingId);
                        else failedObjectIds.Add(drawingId);
                    }
                    catch (Exception markEx)
                    {
                        failedObjectIds.Add(drawingId);
                        errors.Add($"Object {drawingId}: {markEx.Message}");
                    }
                }

                if (updatedObjectIds.Count > 0)
                    activeDrawing.CommitChanges("(MCP) SetMarkContent");

                _output.WriteLine(JsonSerializer.Serialize(new { updatedCount = updatedObjectIds.Count, failedCount = failedObjectIds.Count, updatedObjectIds, failedObjectIds, errors }));
                return true;
            }

            case "get_drawing_context":
            {
                var drawingHandler = new DrawingHandler();
                var activeDrawing = drawingHandler.GetActiveDrawing();
                if (activeDrawing == null)
                {
                    _output.WriteLine("{\"error\":\"No drawing is currently open\"}");
                    return true;
                }

                var selectedObjects = new List<object>();
                var selector = drawingHandler.GetDrawingObjectSelector();
                var selected = selector.GetSelected();
                while (selected.MoveNext())
                {
                    var selectedObject = selected.Current;
                    if (selectedObject == null) continue;
                    selectedObjects.Add(new
                    {
                        id = selectedObject.GetIdentifier().ID,
                        type = selectedObject.GetType().Name,
                        modelId = selectedObject is Tekla.Structures.Drawing.ModelObject dm ? dm.ModelIdentifier.ID : (int?)null
                    });
                }

                _output.WriteLine(JsonSerializer.Serialize(new
                {
                    drawing = new
                    {
                        guid = activeDrawing.GetIdentifier().GUID.ToString(),
                        name = activeDrawing.Name,
                        mark = activeDrawing.Mark,
                        type = activeDrawing.GetType().Name,
                        status = activeDrawing.UpToDateStatus.ToString()
                    },
                    selectedCount = selectedObjects.Count,
                    selectedObjects
                }));
                return true;
            }

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

                var ids = ParseIntList(args[1]);
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

            case "get_drawing_marks":
            {
                int? viewId = null;
                if (args.Length > 1 && int.TryParse(args[1], out var vid)) viewId = vid;

                var api    = new TeklaDrawingMarkApi(_model);
                var result = api.GetMarks(viewId);
                _output.WriteLine(JsonSerializer.Serialize(new
                {
                    total = result.Total,
                    marks = result.Marks.Select(m => new
                    {
                        id         = m.Id,
                        modelId    = m.ModelId,
                        properties = m.Properties.Select(p => new { name = p.Name, value = p.Value })
                    })
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

    // ── Private helpers ────────────────────────────────────────────────────

    private sealed class DrawingPropertyFilter
    {
        public string Property { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    private static bool ContainsIgnoreCase(string? source, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        return !string.IsNullOrWhiteSpace(source)
            && source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string SanitizeFileName(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            value = value.Replace(c, '_');
        return value;
    }

    private static List<int> ParseIntList(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return new List<int>();
        return csv.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => int.TryParse(x, out _))
            .Select(int.Parse)
            .Distinct()
            .ToList();
    }

    private static List<DrawingPropertyFilter> ParseDrawingFilters(string filtersJson)
    {
        var result = new List<DrawingPropertyFilter>();
        if (string.IsNullOrWhiteSpace(filtersJson)) return result;
        try
        {
            using var doc = JsonDocument.Parse(filtersJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return result;
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var property = item.TryGetProperty("property", out var p) ? (p.GetString() ?? string.Empty) : string.Empty;
                var value = item.TryGetProperty("value", out var v) ? (v.GetString() ?? string.Empty) : string.Empty;
                if (!string.IsNullOrWhiteSpace(property))
                    result.Add(new DrawingPropertyFilter { Property = property, Value = value });
            }
        }
        catch { }
        return result;
    }

    private static bool MatchesAllFilters(Drawing drawing, List<DrawingPropertyFilter> filters)
    {
        foreach (var filter in filters)
        {
            var key = filter.Property.Trim().ToLowerInvariant();
            var value = filter.Value ?? string.Empty;
            var match = key switch
            {
                "name"   => string.Equals(drawing.Name ?? string.Empty, value, StringComparison.OrdinalIgnoreCase),
                "mark"   => string.Equals(drawing.Mark ?? string.Empty, value, StringComparison.OrdinalIgnoreCase),
                "type"   => string.Equals(drawing.GetType().Name, value, StringComparison.OrdinalIgnoreCase),
                "status" => string.Equals(drawing.UpToDateStatus.ToString(), value, StringComparison.OrdinalIgnoreCase),
                _        => false
            };
            if (!match) return false;
        }
        return true;
    }

    private static Type? ResolveDrawingType(string objectType)
    {
        if (string.IsNullOrWhiteSpace(objectType)) return null;
        return Type.GetType($"Tekla.Structures.Drawing.{objectType}, Tekla.Structures.Drawing", false, true);
    }

    private string GetMarkType(Mark mark)
    {
        var associatedObjects = mark.GetRelatedObjects();
        foreach (object associated in associatedObjects)
        {
            if (associated is not Tekla.Structures.Drawing.ModelObject drawingModelObject) continue;
            var modelObject = _model.SelectModelObject(drawingModelObject.ModelIdentifier);
            if (modelObject == null) continue;
            if (modelObject is Tekla.Structures.Model.Part) return "Part Mark";
            if (modelObject is BoltGroup) return "Bolt Mark";
            if (modelObject is RebarGroup || modelObject is SingleRebar) return "Reinforcement Mark";
            if (modelObject is Tekla.Structures.Model.Weld) return "Weld Mark";
            if (modelObject is Assembly) return "Assembly Mark";
            if (modelObject is Tekla.Structures.Model.Connection) return "Connection Mark";
        }
        return "Unknown Mark Type";
    }

    private static PropertyElement? CreateMarkPropertyElement(string attributeName)
    {
        return (attributeName ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "PART_POS" or "PARTPOSITION"
                => new PropertyElement(PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.PartPosition()),
            "PROFILE" or "PART_PROFILE"
                => new PropertyElement(PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.Profile()),
            "MATERIAL" or "PART_MATERIAL"
                => new PropertyElement(PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.Material()),
            "ASSEMBLY_POS" or "PART_PREFIX" or "ASSEMBLYPOSITION"
                => new PropertyElement(PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.AssemblyPosition()),
            "NAME"     => new PropertyElement(PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.Name()),
            "CLASS"    => new PropertyElement(PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.Class()),
            "SIZE"     => new PropertyElement(PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.Size()),
            "CAMBER"   => new PropertyElement(PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.Camber()),
            _          => null
        };
    }

    private static bool CreateGaDrawingViaMacro(string viewName, string gaAttribute, bool openGaDrawing, out string error)
    {
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(viewName)) { error = "View name is required."; return false; }

        string macroDirs = string.Empty;
        if (!TeklaStructuresSettings.GetAdvancedOption("XS_MACRO_DIRECTORY", ref macroDirs))
        {
            error = "XS_MACRO_DIRECTORY is not defined.";
            return false;
        }

        string? modelingDir = null;
        foreach (var path in macroDirs.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var cleanPath = path.Trim();
            var subDir = Path.Combine(cleanPath, "modeling");
            if (Directory.Exists(subDir)) { modelingDir = subDir; break; }
            if (Directory.Exists(cleanPath)) { modelingDir = cleanPath; break; }
        }

        if (modelingDir == null) { error = "Valid modeling macro directory not found."; return false; }

        var macroName = $"_tmp_ga_{Guid.NewGuid():N}.cs";
        var macroPath = Path.Combine(modelingDir, macroName);
        var safeViewName = viewName.Replace("\\", "\\\\").Replace("\"", "\\\"");
        var attrLine = string.IsNullOrWhiteSpace(gaAttribute) ? string.Empty
            : $"            akit.ValueChange(\"Create GA-drawing\", \"dia_attr_name\", \"{gaAttribute}\");{Environment.NewLine}";
        var openFlag = openGaDrawing ? "1" : "0";

        var macroSource =
$@"
            namespace Tekla.Technology.Akit.UserScript
            {{
                public sealed class Script
                {{
                    public static void Run(Tekla.Technology.Akit.IScript akit)
                    {{
                        akit.Callback(""acmd_create_dim_general_assembly_drawing"", """", ""main_frame"");
{attrLine}            akit.ListSelect(""Create GA-drawing"", ""dia_view_name_list"", ""{safeViewName}"");
                        akit.ValueChange(""Create GA-drawing"", ""dia_creation_mode"", ""0"");
                        akit.ValueChange(""Create GA-drawing"", ""dia_open_drawing"", ""{openFlag}"");
                        akit.PushButton(""Pushbutton_127"", ""Create GA-drawing"");
                    }}
                }}
            }}";

        File.WriteAllText(macroPath, macroSource);
        try
        {
            if (!Tekla.Structures.Model.Operations.Operation.RunMacro(macroName))
            {
                error = "RunMacro returned false.";
                return false;
            }

            var timeout = DateTime.Now.AddSeconds(30);
            while (Tekla.Structures.Model.Operations.Operation.IsMacroRunning())
            {
                if (DateTime.Now > timeout) { error = "Macro timeout exceeded."; return false; }
                System.Threading.Thread.Sleep(100);
            }

            return true;
        }
        finally
        {
            if (File.Exists(macroPath)) File.Delete(macroPath);
        }
    }
}
