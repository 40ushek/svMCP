using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace TeklaMcpServer.Tools;

public static partial class ModelTools
{
    [McpServerTool, Description("List all drawings from the current Tekla model")]
    public static string ListDrawings()
    {
        var json = RunBridge("list_drawings");
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("error", out var err))
                return $"Error: {err.GetString()}";

            if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() == 0)
                return "No drawings found in the current model.";

            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return $"Bridge error: {json}";
        }
    }

    [McpServerTool, Description("Find drawings by name and/or mark (case-insensitive contains search)")]
    public static string FindDrawings(
        [Description("Optional drawing name filter (contains match)")] string? nameContains = null,
        [Description("Optional drawing mark filter (contains match)")] string? markContains = null)
    {
        var json = RunBridge("find_drawings", nameContains ?? string.Empty, markContains ?? string.Empty);
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("error", out var err))
                return $"Error: {err.GetString()}";

            if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() == 0)
                return "No drawings match the provided filters.";

            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return $"Bridge error: {json}";
        }
    }

    [McpServerTool, Description("Export drawings to PDF by comma-separated drawing GUIDs")]
    public static string ExportDrawingsToPdf(
        [Description("Comma-separated drawing GUIDs")] string drawingGuidsCsv,
        [Description("Optional target folder path for PDFs. If empty: <ModelPath>\\\\PlotFiles")] string? outputDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(drawingGuidsCsv))
            return "Error: 'drawingGuidsCsv' is required and cannot be empty.";

        var json = RunBridge("export_drawings_pdf", drawingGuidsCsv, outputDirectory ?? string.Empty);
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("error", out var err))
                return $"Error: {err.GetString()}";

            var exportedCount = doc.RootElement.GetProperty("exportedCount").GetInt32();
            var outputDir = doc.RootElement.GetProperty("outputDirectory").GetString();

            return $"Exported {exportedCount} drawings to PDF. Output directory: {outputDir}\n"
                 + JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return $"Bridge error: {json}";
        }
    }

    [McpServerTool, Description("Open (activate) drawing by GUID in Tekla Drawing Editor")]
    public static string OpenDrawing(
        [Description("Drawing GUID from list_drawings or find_drawings")] string drawingGuid)
    {
        if (string.IsNullOrWhiteSpace(drawingGuid))
            return "Error: 'drawingGuid' is required and cannot be empty.";

        var json = RunBridge("open_drawing", drawingGuid);
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("error", out var err))
                return $"Error: {err.GetString()}";

            return $"Drawing opened.\n{JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true })}";
        }
        catch
        {
            return $"Bridge error: {json}";
        }
    }

    [McpServerTool, Description("Close the currently active drawing in Tekla Drawing Editor")]
    public static string CloseDrawing()
    {
        var json = RunBridge("close_drawing");
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("error", out var err))
                return $"Error: {err.GetString()}";

            return $"Drawing closed.\n{JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true })}";
        }
        catch
        {
            return $"Bridge error: {json}";
        }
    }

    [McpServerTool, Description("Update a drawing by GUID through Tekla DrawingHandler.UpdateDrawing")]
    public static string UpdateDrawing(
        [Description("Drawing GUID from list_drawings or find_drawings_by_properties")] string drawingGuid)
    {
        if (string.IsNullOrWhiteSpace(drawingGuid))
            return "Error: 'drawingGuid' is required and cannot be empty.";

        var json = RunBridge("update_drawing", drawingGuid);
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("error", out var err))
                return $"Error: {err.GetString()}";

            return $"Drawing update command executed.\n{JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true })}";
        }
        catch
        {
            return $"Bridge error: {json}";
        }
    }

    [McpServerTool, Description("Delete a drawing by GUID. Use with care before recreating a drawing from its source model object.")]
    public static string DeleteDrawing(
        [Description("Drawing GUID from list_drawings or find_drawings_by_properties")] string drawingGuid)
    {
        if (string.IsNullOrWhiteSpace(drawingGuid))
            return "Error: 'drawingGuid' is required and cannot be empty.";

        var json = RunBridge("delete_drawing", drawingGuid);
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("error", out var err))
                return $"Error: {err.GetString()}";

            return $"Drawing delete command executed.\n{JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true })}";
        }
        catch
        {
            return $"Bridge error: {json}";
        }
    }

    [McpServerTool, Description("Find drawings by multiple properties using JSON filters. Supports property/operator/value filters. Properties include name, mark, title1-3, type, drawingType, status, sourceModelObjectId, sourceModelObjectKind, isLocked, isIssued, isIssuedButModified, isFrozen, isReadyForIssue. Operators: equals, not_equals, contains, not_contains, starts_with, ends_with.")]
    public static string FindDrawingsByProperties(
        [Description("JSON array of filters. Example: [{\"property\":\"status\",\"operator\":\"not_equals\",\"value\":\"UpToDate\"},{\"property\":\"type\",\"value\":\"SinglePartDrawing\"}]")] string drawingPropertyFiltersJson)
    {
        if (string.IsNullOrWhiteSpace(drawingPropertyFiltersJson))
            return "Error: 'drawingPropertyFiltersJson' is required and cannot be empty.";

        var json = RunBridge("find_drawings_by_properties", drawingPropertyFiltersJson);
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("error", out var err))
                return $"Error: {err.GetString()}";

            if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() == 0)
                return "No drawings match all provided filters.";

            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return $"Bridge error: {json}";
        }
    }
}
