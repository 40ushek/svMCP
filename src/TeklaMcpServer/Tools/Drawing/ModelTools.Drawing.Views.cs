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

    [McpServerTool, Description("Get all marks on the active drawing with their property names and computed values (e.g. PART_POS='Б1-1', PROFILE='HEA200'). Optionally filter to a single view by viewId.")]
    public static string GetDrawingMarks(
        [Description("View ID to read marks from (from get_drawing_views). Omit to get all marks on the drawing.")] int? viewId = null)
    {
        var arg = viewId.HasValue ? viewId.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;
        var json = RunBridge("get_drawing_marks", arg);
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

    [McpServerTool, Description("Fit all views to the sheet: auto-calculates the optimal standard scale (1:1, 1:5, 1:10, 1:20...) and arranges views without overlaps")]
    public static string FitViewsToSheet(
        [Description("Margin from sheet edges in mm. Default: 10")] double margin = 10,
        [Description("Gap between views in mm. Default: 8")] double gap = 8,
        [Description("Height of title block at bottom of sheet in mm. Default: 0")] double titleBlockHeight = 0)
    {
        var json = RunBridge(
            "fit_views_to_sheet",
            margin.ToString(CultureInfo.InvariantCulture),
            gap.ToString(CultureInfo.InvariantCulture),
            titleBlockHeight.ToString(CultureInfo.InvariantCulture));
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("error", out var err))
                return string.Concat("Error: ", err.GetString());

            var optScale = doc.RootElement.TryGetProperty("optimalScale", out var s) ? s.GetDouble() : 0;
            var count    = doc.RootElement.TryGetProperty("arranged",     out var a) ? a.GetInt32()  : 0;
            var details  = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
            return string.Concat("Arranged ", count, " views at scale 1:", optScale, System.Environment.NewLine, details);
        }
        catch
        {
            return string.Concat("Bridge error: ", json);
        }
    }
}
