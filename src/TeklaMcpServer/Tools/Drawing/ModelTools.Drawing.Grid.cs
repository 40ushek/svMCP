using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace TeklaMcpServer.Tools;

public static partial class ModelTools
{
    [McpServerTool, Description(
        "Get all grid axes visible in a drawing view. " +
        "Returns each axis label, direction ('X' = vertical line, 'Y' = horizontal line), " +
        "coordinate along the perpendicular axis, and start/end points — all in view-local coordinates (mm). " +
        "Use these coordinates to place dimension chains between grid lines with create_dimension.")]
    public static string GetGridAxes(
        [Description("ID of the drawing view (from get_drawing_views)")] int viewId)
    {
        var json = RunBridge("get_grid_axes", viewId.ToString());
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
}
