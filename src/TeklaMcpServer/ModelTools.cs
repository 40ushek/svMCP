using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace TeklaMcpServer.Tools;

[McpServerToolType]
public static class ModelTools
{
    private static readonly string BridgePath = Path.Combine(
        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
        "bridge", "TeklaBridge.exe");

    private static string RunBridge(params string[] args)
    {
        if (!File.Exists(BridgePath))
            return $"Error: TeklaBridge.exe not found at {BridgePath}";

        var startInfo = new ProcessStartInfo
        {
            FileName = BridgePath,
            WorkingDirectory = Path.GetDirectoryName(BridgePath)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        using var proc = new Process
        {
            StartInfo = startInfo
        };

        proc.Start();
        var output = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        var result = output.Trim();
        if (string.IsNullOrEmpty(result))
            result = JsonSerializer.Serialize(new { error = "TeklaBridge produced no output", stderr = stderr.Trim() });
        return result;
    }

    [McpServerTool, Description("Check connection to running Tekla Structures instance")]
    public static string CheckConnection()
    {
        var json = RunBridge("check_connection");
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out _))
                return $"Not connected. Bridge response:\n{JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true })}";
            var model = doc.RootElement.GetProperty("modelName").GetString();
            var path = doc.RootElement.GetProperty("modelPath").GetString();
            return $"Connected. Model: {model}, Path: {path}";
        }
        catch
        {
            return $"Bridge error: {json}";
        }
    }

    [McpServerTool, Description("Get properties of selected elements in Tekla model (GUID, name, profile, material, class, weight)")]
    public static string GetSelectedElementsProperties()
    {
        var json = RunBridge("get_selected_properties");
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var err))
                return $"Error: {err.GetString()}";
            if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() == 0)
                return "No parts selected. Please select elements in Tekla first.";
            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return $"Bridge error: {json}";
        }
    }

    [McpServerTool, Description("Select elements in Tekla model by class number (Tekla class field)")]
    public static string SelectElementsByClass(
        [Description("Tekla class number, e.g. 1, 2, 99")] int classNumber)
    {
        var json = RunBridge("select_by_class", classNumber.ToString());
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var err))
                return $"Error: {err.GetString()}";
            var count = doc.RootElement.GetProperty("count").GetInt32();
            return $"Selected {count} elements with class {classNumber}.";
        }
        catch
        {
            return $"Bridge error: {json}";
        }
    }

    [McpServerTool, Description("Get total weight of selected elements in kg")]
    public static string GetSelectedElementsTotalWeight()
    {
        var json = RunBridge("get_selected_weight");
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var err))
                return $"Error: {err.GetString()}";
            var totalWeight = doc.RootElement.GetProperty("totalWeight").GetDouble();
            var count = doc.RootElement.GetProperty("count").GetInt32();
            if (count == 0) return "No parts selected.";
            return $"Total weight of {count} selected parts: {totalWeight} kg";
        }
        catch
        {
            return $"Bridge error: {json}";
        }
    }

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
}
