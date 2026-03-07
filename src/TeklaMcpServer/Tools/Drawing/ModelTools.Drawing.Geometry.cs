using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace TeklaMcpServer.Tools;

public static partial class ModelTools
{
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
}
