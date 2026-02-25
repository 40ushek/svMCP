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
            if (doc.RootElement.TryGetProperty("error", out var err))
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
            if (doc.RootElement.TryGetProperty("error", out var err))
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
            if (doc.RootElement.TryGetProperty("error", out var err))
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

    [McpServerTool, Description("Find drawings by multiple properties using JSON filters")]
    public static string FindDrawingsByProperties(
        [Description("JSON array of filters. Example: [{\"property\":\"Name\",\"value\":\"GA-001\"},{\"property\":\"Mark\",\"value\":\"A1\"}]")] string drawingPropertyFiltersJson)
    {
        if (string.IsNullOrWhiteSpace(drawingPropertyFiltersJson))
            return "Error: 'drawingPropertyFiltersJson' is required and cannot be empty.";

        var json = RunBridge("find_drawings_by_properties", drawingPropertyFiltersJson);
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var err))
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
