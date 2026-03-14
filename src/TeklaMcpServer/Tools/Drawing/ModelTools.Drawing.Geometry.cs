using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace TeklaMcpServer.Tools;

public static partial class ModelTools
{
    [McpServerTool, Description(
        "Get geometry (bboxMin, bboxMax, startPoint, endPoint, axes) for ALL parts in a drawing view in a single call. " +
        "Returns type, name, partPos, profile, material and full view-local coordinates for every part. " +
        "Use instead of calling get_part_geometry_in_view N times — dramatically faster for dimension placement.")]
    public static string GetAllPartsGeometryInView(
        [Description("ID of the drawing view (from get_drawing_views)")] int viewId)
    {
        return RunBridge("get_all_parts_geometry_in_view", viewId.ToString());
    }

    [McpServerTool, Description(
        "Get the geometry of a model part (beam, plate, etc.) expressed in the coordinate system of a specific drawing view. " +
        "Returns start/end points, bounding box, and local axes — all in view-local coordinates (mm). " +
        "Use these coordinates to compute correct dimension points for create_dimension.")]
    public static string GetPartGeometryInView(
        [Description("ID of the drawing view (from get_drawing_views)")] int viewId,
        [Description("Model object ID of the part (from get_drawing_parts)")] int modelId)
    {
        var json = RunBridge("get_part_geometry_in_view",
            viewId.ToString(),
            modelId.ToString());
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var err) && err.GetString() is { Length: > 0 } e)
                return $"Error: {e}";
            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return $"Bridge error: {json}";
        }
    }

    [McpServerTool, Description(
        "Draw developer debug overlay geometry into the active drawing. " +
        "Payload is JSON with group, clearGroupFirst and shapes[]. " +
        "Supported shape kinds: line, rectangle, polyline, polygon, text, cross. " +
        "Optional style fields: color, lineType, textHeight, size. " +
        "Shapes can target a specific viewId or the sheet when omitted. " +
        "Objects are tagged and can later be removed with clear_debug_overlay.")]
    public static string DrawDebugOverlay(
        [Description("JSON payload describing overlay shapes")] string overlayJson)
    {
        return RunBridge("draw_debug_overlay", overlayJson);
    }

    [McpServerTool, Description(
        "Clear previously drawn developer debug overlay objects from the active drawing. " +
        "If group is empty, clears all svMCP debug overlay objects.")]
    public static string ClearDebugOverlay(
        [Description("Optional overlay group name to clear")] string group = "")
    {
        return RunBridge("clear_debug_overlay", group ?? string.Empty);
    }
}
