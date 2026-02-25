using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;

namespace TeklaMcpServer.Tools;

public static partial class ModelTools
{
    [McpServerTool, Description("Create a General Arrangement (GA) drawing from a saved model view")]
    public static string CreateGeneralArrangementDrawing(
        [Description("Name of drawing properties file. Default: standard")] string drawingProperties = "standard",
        [Description("Open drawing after creation. Default: true")] bool openDrawing = true,
        [Description("Saved model view name (required for this bridge version)")] string? viewName = null)
    {
        if (string.IsNullOrWhiteSpace(viewName))
            return "Error: 'viewName' is required (saved model view name).";

        var json = RunBridge(
            "create_ga_drawing",
            drawingProperties ?? "standard",
            openDrawing ? "true" : "false",
            viewName);
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var err))
                return $"Error: {err.GetString()}";

            return $"GA drawing creation command executed:\n{JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true })}";
        }
        catch
        {
            return $"Bridge error: {json}";
        }
    }

    [McpServerTool, Description("Select drawing objects in the active drawing by model object IDs (comma-separated)")]
    public static string SelectDrawingObjects(
        [Description("Comma-separated model object IDs")] string modelObjectIdsCsv)
    {
        if (string.IsNullOrWhiteSpace(modelObjectIdsCsv))
            return "Error: 'modelObjectIdsCsv' is required and cannot be empty.";

        var json = RunBridge("select_drawing_objects", modelObjectIdsCsv);
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var err))
                return $"Error: {err.GetString()}";

            var selectedCount = doc.RootElement.TryGetProperty("selectedCount", out var c) ? c.GetInt32() : 0;
            return $"Selected {selectedCount} drawing objects.\n{JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true })}";
        }
        catch
        {
            return $"Bridge error: {json}";
        }
    }

    [McpServerTool, Description("Filter drawing objects by Tekla drawing type (e.g. Mark, Part, DimensionBase)")]
    public static string FilterDrawingObjects(
        [Description("Drawing object type name, e.g. Mark, Part, DimensionBase, Text")] string objectType,
        [Description("Optional subtype, currently useful for Mark: Part Mark, Bolt Mark, Reinforcement Mark, Weld Mark")] string? specificType = null)
    {
        if (string.IsNullOrWhiteSpace(objectType))
            return "Error: 'objectType' is required and cannot be empty.";

        var json = RunBridge("filter_drawing_objects", objectType, specificType ?? string.Empty);
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var err))
                return $"Error: {err.GetString()}";

            if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() == 0)
                return "No drawing objects matched the filter.";

            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return $"Bridge error: {json}";
        }
    }

    [McpServerTool, Description("Modify existing drawing marks: content and/or font settings")]
    public static string SetMarkContent(
        [Description("Comma-separated IDs. Can be drawing object IDs or model object IDs")] string elementIdsCsv,
        [Description("Optional comma-separated content attributes, e.g. PART_POS,PROFILE,MATERIAL")] string? contentElements = null,
        [Description("Optional font name, e.g. Arial")] string? fontName = null,
        [Description("Optional drawing color enum name, e.g. Black, Red, Blue")] string? fontColor = null,
        [Description("Optional font height > 0")] double? fontHeight = null)
    {
        if (string.IsNullOrWhiteSpace(elementIdsCsv))
            return "Error: 'elementIdsCsv' is required and cannot be empty.";

        var fontHeightArg = fontHeight.HasValue
            ? fontHeight.Value.ToString(CultureInfo.InvariantCulture)
            : string.Empty;

        var json = RunBridge(
            "set_mark_content",
            elementIdsCsv,
            contentElements ?? string.Empty,
            fontName ?? string.Empty,
            fontColor ?? string.Empty,
            fontHeightArg);
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var err))
                return $"Error: {err.GetString()}";

            var updatedCount = doc.RootElement.TryGetProperty("updatedCount", out var u) ? u.GetInt32() : 0;
            return $"Updated {updatedCount} mark objects.\n{JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true })}";
        }
        catch
        {
            return $"Bridge error: {json}";
        }
    }

    [McpServerTool, Description("Get active drawing context and currently selected drawing objects")]
    public static string GetDrawingContext()
    {
        var json = RunBridge("get_drawing_context");
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var err))
                return $"Error: {err.GetString()}";

            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return $"Bridge error: {json}";
        }
    }
}
