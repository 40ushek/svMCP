using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;

namespace TeklaMcpServer.Tools;

public static partial class ModelTools
{
    [McpServerTool, Description("Get all views in the active drawing with position, scale, size, and sheet dimensions (sheetWidth/sheetHeight in mm)")]
    public static string GetDrawingViews()
    {
        var json = RunBridge("get_drawing_views");
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("error", out var err))
                return $"Error: {err.GetString()}";

            if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() == 0)
                return "No views found in the active drawing.";

            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return $"Bridge error: {json}";
        }
    }

    [McpServerTool, Description("Move a drawing view by relative offset (dx/dy) or to an absolute position")]
    public static string MoveView(
        [Description("Drawing view ID (from get_drawing_views)")] int viewId,
        [Description("X offset (relative) or X coordinate (absolute)")] double dx,
        [Description("Y offset (relative) or Y coordinate (absolute)")] double dy,
        [Description("If true, dx/dy are treated as absolute coordinates. Default: false (relative offset)")] bool absolute = false)
    {
        var json = RunBridge(
            "move_view",
            viewId.ToString(CultureInfo.InvariantCulture),
            dx.ToString(CultureInfo.InvariantCulture),
            dy.ToString(CultureInfo.InvariantCulture),
            absolute ? "abs" : string.Empty);
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("error", out var err))
                return $"Error: {err.GetString()}";

            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return $"Bridge error: {json}";
        }
    }

    [McpServerTool, Description("Set the scale of one or more drawing views by ID")]
    public static string SetViewScale(
        [Description("Comma-separated drawing view IDs (from get_drawing_views)")] string viewIdsCsv,
        [Description("New scale value, e.g. 50 for 1:50")] double scale)
    {
        if (string.IsNullOrWhiteSpace(viewIdsCsv))
            return "Error: 'viewIdsCsv' is required and cannot be empty.";

        if (scale <= 0)
            return "Error: 'scale' must be a positive number.";

        var json = RunBridge(
            "set_view_scale",
            viewIdsCsv,
            scale.ToString(CultureInfo.InvariantCulture));
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("error", out var err))
                return $"Error: {err.GetString()}";

            var updatedCount = doc.RootElement.TryGetProperty("updatedCount", out var u) ? u.GetInt32() : 0;
            return $"Updated scale to 1:{scale} for {updatedCount} view(s).\n{JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true })}";
        }
        catch
        {
            return $"Bridge error: {json}";
        }
    }
}
