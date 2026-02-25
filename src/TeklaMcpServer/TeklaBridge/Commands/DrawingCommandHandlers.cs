using System.Collections;
using System.IO;
using System.Linq;
using System.Text.Json;
using Tekla.Structures.Drawing;
using Tekla.Structures.Drawing.UI;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Model;

namespace TeklaBridge;

internal partial class Program
{
    private static bool TryHandleDrawingCommand(string command, string[] args, Model model, TextWriter realOut)
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

                realOut.WriteLine(JsonSerializer.Serialize(drawings));
                return true;
            }

            case "find_drawings":
            {
                var nameContains = args.Length > 1 ? args[1] : string.Empty;
                var markContains = args.Length > 2 ? args[2] : string.Empty;

                if (string.IsNullOrWhiteSpace(nameContains) && string.IsNullOrWhiteSpace(markContains))
                {
                    realOut.WriteLine("{\"error\":\"Provide at least one filter: nameContains or markContains\"}");
                    return true;
                }

                var drawingHandler = new DrawingHandler();
                var drawingEnumerator = drawingHandler.GetDrawings();
                var drawings = new List<object>();

                while (drawingEnumerator.MoveNext())
                {
                    var drawing = drawingEnumerator.Current;
                    if (!ContainsIgnoreCase(drawing.Name, nameContains))
                        continue;

                    if (!ContainsIgnoreCase(drawing.Mark, markContains))
                        continue;

                    drawings.Add(new
                    {
                        guid = drawing.GetIdentifier().GUID.ToString(),
                        name = drawing.Name,
                        mark = drawing.Mark,
                        type = drawing.GetType().Name
                    });
                }

                realOut.WriteLine(JsonSerializer.Serialize(drawings));
                return true;
            }

            case "export_drawings_pdf":
            {
                if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
                {
                    realOut.WriteLine("{\"error\":\"Missing drawing GUID list (comma-separated)\"}");
                    return true;
                }

                var requestedGuids = args[1]
                    .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                if (requestedGuids.Count == 0)
                {
                    realOut.WriteLine("{\"error\":\"No valid drawing GUIDs provided\"}");
                    return true;
                }

                var modelInfo = model.GetInfo();
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
                    if (!requestedGuids.Contains(guid))
                        continue;

                    foundGuids.Add(guid);

                    var fileName = $"{SanitizeFileName(drawing.Name)}_{SanitizeFileName(drawing.Mark)}.pdf";
                    var filePath = Path.Combine(outputDir, fileName);

                    if (drawingHandler.PrintDrawing(drawing, printAttributes, filePath))
                        exportedFiles.Add(filePath);
                    else
                        failedToExport.Add(guid);
                }

                var missingGuids = requestedGuids
                    .Where(g => !foundGuids.Contains(g))
                    .ToList();

                realOut.WriteLine(JsonSerializer.Serialize(new
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
                    realOut.WriteLine("{\"error\":\"Missing filters JSON\"}");
                    return true;
                }

                var filters = ParseDrawingFilters(args[1]);
                if (filters.Count == 0)
                {
                    realOut.WriteLine("{\"error\":\"filtersJson must be a JSON array like [{\\\"property\\\":\\\"Name\\\",\\\"value\\\":\\\"GA\\\"}]\"}");
                    return true;
                }

                var drawingHandler = new DrawingHandler();
                var drawingEnumerator = drawingHandler.GetDrawings();
                var drawings = new List<object>();

                while (drawingEnumerator.MoveNext())
                {
                    var drawing = drawingEnumerator.Current;
                    var isMatch = true;

                    foreach (var filter in filters)
                    {
                        var key = filter.Property.Trim().ToLowerInvariant();
                        var value = filter.Value ?? string.Empty;

                        switch (key)
                        {
                            case "name":
                                if (!string.Equals(drawing.Name ?? string.Empty, value, StringComparison.OrdinalIgnoreCase))
                                    isMatch = false;
                                break;
                            case "mark":
                                if (!string.Equals(drawing.Mark ?? string.Empty, value, StringComparison.OrdinalIgnoreCase))
                                    isMatch = false;
                                break;
                            case "type":
                                if (!string.Equals(drawing.GetType().Name, value, StringComparison.OrdinalIgnoreCase))
                                    isMatch = false;
                                break;
                            case "status":
                                if (!string.Equals(drawing.UpToDateStatus.ToString(), value, StringComparison.OrdinalIgnoreCase))
                                    isMatch = false;
                                break;
                            default:
                                isMatch = false;
                                break;
                        }

                        if (!isMatch)
                            break;
                    }

                    if (!isMatch)
                        continue;

                    drawings.Add(new
                    {
                        guid = drawing.GetIdentifier().GUID.ToString(),
                        name = drawing.Name,
                        mark = drawing.Mark,
                        type = drawing.GetType().Name,
                        status = drawing.UpToDateStatus.ToString()
                    });
                }

                realOut.WriteLine(JsonSerializer.Serialize(drawings));
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
                    realOut.WriteLine("{\"error\":\"viewName is required for this Tekla version. Pass a saved model view name.\"}");
                    return true;
                }

                if (!CreateGaDrawingViaMacro(viewName, drawingProperties, openDrawing, out var macroError))
                {
                    realOut.WriteLine(JsonSerializer.Serialize(new
                    {
                        error = "Failed to create GA drawing",
                        details = macroError,
                        viewName
                    }));
                    return true;
                }

                realOut.WriteLine(JsonSerializer.Serialize(new
                {
                    created = true,
                    drawingType = "GA",
                    viewName,
                    drawingProperties,
                    openDrawing
                }));
                return true;
            }

            case "select_drawing_objects":
            {
                if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
                {
                    realOut.WriteLine("{\"error\":\"Missing model object IDs. Use comma-separated IDs.\"}");
                    return true;
                }

                var activeDrawing = new DrawingHandler().GetActiveDrawing();
                if (activeDrawing == null)
                {
                    realOut.WriteLine("{\"error\":\"No drawing is currently open\"}");
                    return true;
                }

                var targetModelIds = ParseIntList(args[1]).ToHashSet();
                if (targetModelIds.Count == 0)
                {
                    realOut.WriteLine("{\"error\":\"No valid model object IDs provided\"}");
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
                    realOut.WriteLine("{\"error\":\"None of the specified model IDs were found in the active drawing\"}");
                    return true;
                }

                var drawingHandler = new DrawingHandler();
                var selector = drawingHandler.GetDrawingObjectSelector();
                selector.SelectObjects(drawingObjectsToSelect, false);
                activeDrawing.CommitChanges("(MCP) SelectDrawingObjects");

                realOut.WriteLine(JsonSerializer.Serialize(new
                {
                    selectedCount = drawingObjectsToSelect.Count,
                    selectedDrawingObjectIds,
                    selectedModelIds
                }));
                return true;
            }

            case "filter_drawing_objects":
            {
                if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
                {
                    realOut.WriteLine("{\"error\":\"Missing objectType. Example: Mark, Part, DimensionBase\"}");
                    return true;
                }

                var objectType = args[1];
                var specificType = args.Length > 2 ? args[2] : string.Empty;
                var targetType = ResolveDrawingType(objectType);
                if (targetType == null)
                {
                    realOut.WriteLine(JsonSerializer.Serialize(new
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
                    realOut.WriteLine("{\"error\":\"No drawing is currently open\"}");
                    return true;
                }

                var results = new List<object>();
                var drawingObjects = activeDrawing.GetSheet().GetAllObjects();
                while (drawingObjects.MoveNext())
                {
                    var drawingObject = drawingObjects.Current;
                    if (drawingObject == null || !targetType.IsInstanceOfType(drawingObject))
                        continue;

                    if (drawingObject is Mark mark && !string.IsNullOrWhiteSpace(specificType))
                    {
                        var markType = GetMarkType(mark, model);
                        if (!string.Equals(markType, specificType, StringComparison.OrdinalIgnoreCase))
                            continue;
                    }

                    results.Add(new
                    {
                        id = drawingObject.GetIdentifier().ID,
                        type = drawingObject.GetType().Name,
                        modelId = drawingObject is Tekla.Structures.Drawing.ModelObject dm ? dm.ModelIdentifier.ID : (int?)null
                    });
                }

                realOut.WriteLine(JsonSerializer.Serialize(results));
                return true;
            }

            case "set_mark_content":
            {
                if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
                {
                    realOut.WriteLine("{\"error\":\"Missing element IDs (drawing IDs or model IDs)\"}");
                    return true;
                }

                var targetIds = ParseIntList(args[1]).ToHashSet();
                if (targetIds.Count == 0)
                {
                    realOut.WriteLine("{\"error\":\"No valid IDs provided\"}");
                    return true;
                }

                var contentElementsCsv = args.Length > 2 ? args[2] : string.Empty;
                var fontName = args.Length > 3 ? args[3] : string.Empty;
                var fontColorRaw = args.Length > 4 ? args[4] : string.Empty;
                var fontHeightRaw = args.Length > 5 ? args[5] : string.Empty;

                var requestedContentElements = string.IsNullOrWhiteSpace(contentElementsCsv)
                    ? new List<string>()
                    : contentElementsCsv
                        .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim())
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .ToList();

                var updateContent = requestedContentElements.Count > 0;
                var updateFontName = !string.IsNullOrWhiteSpace(fontName);
                var updateFontColor = !string.IsNullOrWhiteSpace(fontColorRaw);
                var updateFontHeight = !string.IsNullOrWhiteSpace(fontHeightRaw);

                if (!updateContent && !updateFontName && !updateFontColor && !updateFontHeight)
                {
                    realOut.WriteLine("{\"error\":\"No changes requested. Provide content elements and/or font attributes.\"}");
                    return true;
                }

                var parsedFontHeight = 0.0;
                var hasValidFontHeight = !updateFontHeight || (double.TryParse(fontHeightRaw, out parsedFontHeight) && parsedFontHeight > 0);
                if (!hasValidFontHeight)
                {
                    realOut.WriteLine("{\"error\":\"fontHeight must be a positive number\"}");
                    return true;
                }

                var parsedColor = DrawingColors.Black;
                var hasValidColor = !updateFontColor || Enum.TryParse(fontColorRaw, true, out parsedColor);
                if (!hasValidColor)
                {
                    realOut.WriteLine("{\"error\":\"Invalid fontColor. Use DrawingColors enum values, e.g. Black, Red, Blue\"}");
                    return true;
                }

                var drawingHandler = new DrawingHandler();
                var activeDrawing = drawingHandler.GetActiveDrawing();
                if (activeDrawing == null)
                {
                    realOut.WriteLine("{\"error\":\"No drawing is currently open\"}");
                    return true;
                }

                var updatedObjectIds = new List<int>();
                var failedObjectIds = new List<int>();
                var errors = new List<string>();
                var drawingObjects = activeDrawing.GetSheet().GetAllObjects();
                while (drawingObjects.MoveNext())
                {
                    if (drawingObjects.Current is not Mark mark)
                        continue;

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

                    if (!matches)
                        continue;

                    try
                    {
                        var content = mark.Attributes.Content;
                        var existingFont = default(FontAttributes);
                        var contentEnumerator = content.GetEnumerator();
                        if (contentEnumerator.MoveNext() && contentEnumerator.Current is PropertyElement existingProperty && existingProperty.Font != null)
                            existingFont = (FontAttributes)existingProperty.Font.Clone();

                        var newFont = existingFont != null ? (FontAttributes)existingFont.Clone() : new FontAttributes();
                        var fontChanged = false;

                        if (updateFontName && !string.Equals(newFont.Name, fontName, StringComparison.Ordinal))
                        {
                            newFont.Name = fontName;
                            fontChanged = true;
                        }

                        if (updateFontHeight && Math.Abs(newFont.Height - parsedFontHeight) > 0.01)
                        {
                            newFont.Height = parsedFontHeight;
                            fontChanged = true;
                        }

                        if (updateFontColor && newFont.Color != parsedColor)
                        {
                            newFont.Color = parsedColor;
                            fontChanged = true;
                        }

                        if (updateContent)
                        {
                            content.Clear();
                            foreach (var attribute in requestedContentElements)
                            {
                                var element = CreateMarkPropertyElement(attribute);
                                if (element == null)
                                {
                                    errors.Add($"Object {drawingId}: unsupported content attribute '{attribute}'.");
                                    continue;
                                }

                                element.Font = (FontAttributes)newFont.Clone();
                                content.Add(element);
                            }
                        }
                        else if (fontChanged)
                        {
                            var existingElements = content.GetEnumerator();
                            while (existingElements.MoveNext())
                            {
                                if (existingElements.Current is PropertyElement propElement)
                                    propElement.Font = (FontAttributes)newFont.Clone();
                            }
                        }

                        if (mark.Modify())
                            updatedObjectIds.Add(drawingId);
                        else
                            failedObjectIds.Add(drawingId);
                    }
                    catch (Exception markEx)
                    {
                        failedObjectIds.Add(drawingId);
                        errors.Add($"Object {drawingId}: {markEx.Message}");
                    }
                }

                if (updatedObjectIds.Count > 0)
                    activeDrawing.CommitChanges("(MCP) SetMarkContent");

                realOut.WriteLine(JsonSerializer.Serialize(new
                {
                    updatedCount = updatedObjectIds.Count,
                    failedCount = failedObjectIds.Count,
                    updatedObjectIds,
                    failedObjectIds,
                    errors
                }));
                return true;
            }

            case "get_drawing_context":
            {
                var drawingHandler = new DrawingHandler();
                var activeDrawing = drawingHandler.GetActiveDrawing();
                if (activeDrawing == null)
                {
                    realOut.WriteLine("{\"error\":\"No drawing is currently open\"}");
                    return true;
                }

                var selectedObjects = new List<object>();
                var selector = drawingHandler.GetDrawingObjectSelector();
                var selected = selector.GetSelected();
                while (selected.MoveNext())
                {
                    var selectedObject = selected.Current;
                    if (selectedObject == null)
                        continue;

                    selectedObjects.Add(new
                    {
                        id = selectedObject.GetIdentifier().ID,
                        type = selectedObject.GetType().Name,
                        modelId = selectedObject is Tekla.Structures.Drawing.ModelObject dm ? dm.ModelIdentifier.ID : (int?)null
                    });
                }

                realOut.WriteLine(JsonSerializer.Serialize(new
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

            default:
                return false;
        }
    }
}
