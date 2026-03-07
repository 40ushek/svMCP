using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;

namespace TeklaMcpServer.Tools;

public static partial class ModelTools
{
    [McpServerTool, Description("Create a straight dimension set in a drawing view from a list of model-space points. Points are passed as a flat JSON array [x0,y0,z0, x1,y1,z1, ...] in model coordinates (mm). Tekla projects them onto the view automatically.")]
    public static string CreateDimension(
        [Description("ID of the drawing view to place the dimension in")] int viewId,
        [Description("Flat JSON array of model-space coordinates: [x0,y0,z0, x1,y1,z1, ...]. Minimum 2 points (6 numbers).")] string points,
        [Description("Direction of the dimension offset: 'horizontal' (offset up, dimension left-right), 'vertical' (offset right, dimension up-down), or custom 'dx,dy,dz' vector. Default: horizontal")] string direction = "horizontal",
        [Description("Offset distance from the part to the dimension line in mm. Default: 50")] double distance = 50.0,
        [Description("Dimension attributes file name (style). Default: standard")] string attributesFile = "standard")
    {
        var json = RunBridge("create_dimension",
            viewId.ToString(CultureInfo.InvariantCulture),
            points,
            direction,
            distance.ToString(CultureInfo.InvariantCulture),
            attributesFile);
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var err) && err.GetString() is { Length: > 0 } e)
                return $"Error: {e}";

            var created = doc.RootElement.TryGetProperty("created", out var c) && c.GetBoolean();
            var dimId   = doc.RootElement.TryGetProperty("dimensionId", out var d) ? d.GetInt32() : 0;
            var pts     = doc.RootElement.TryGetProperty("pointCount",  out var p) ? p.GetInt32() : 0;
            return created
                ? $"Created dimension {dimId} with {pts} points.\n{JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true })}"
                : $"Failed to create dimension.\n{json}";
        }
        catch
        {
            return $"Bridge error: {json}";
        }
    }
}
