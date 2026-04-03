using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;

namespace TeklaMcpServer.Tools;

public static partial class ModelTools
{
    [McpServerTool, Description("Delete a straight dimension set from the active drawing by its ID (from get_drawing_dimensions).")]
    public static string DeleteDimension(
        [Description("ID of the StraightDimensionSet to delete")] int dimensionId)
    {
        var json = RunBridge("delete_dimension", dimensionId.ToString());
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var err) && err.GetString() is { Length: > 0 } e)
                return $"Error: {e}";
            var deleted = doc.RootElement.TryGetProperty("deleted", out var d) && d.GetBoolean();
            return deleted ? $"Deleted dimension {dimensionId}." : $"Dimension {dimensionId} not found.";
        }
        catch { return $"Bridge error: {json}"; }
    }

    [McpServerTool, Description(
        "Draw debug polygon rectangles around dimension text in the active drawing. " +
        "Uses measured text geometry and the dimension line direction to draw overlay polygons around each text box. " +
        "Optionally limit to a specific viewId or dimensionId.")]
    public static string DrawDimensionTextBoxes(
        [Description("Optional drawing view ID. Omit to scan the whole sheet.")] int? viewId = null,
        [Description("Optional StraightDimensionSet ID to limit drawing to one dimension.")] int? dimensionId = null,
        [Description("Overlay color. Default: Yellow")] string color = "Yellow",
        [Description("Overlay group name. Default: dimension-text-boxes")] string group = "dimension-text-boxes")
    {
        var json = RunBridge(
            "draw_dimension_text_boxes",
            viewId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            dimensionId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            color ?? "Yellow",
            group ?? "dimension-text-boxes");
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

    [McpServerTool, Description("Place one control diagonal dimension between farthest extreme points of the assembly in the target drawing view. If viewId is omitted, FrontView is preferred, otherwise the largest view is used. Uses real part solid geometry filtered by material type.")]
    public static string PlaceControlDiagonals(
        [Description("Optional target view ID. Omit to use main view auto-selection.")] int? viewId = null,
        [Description("Dimension line offset distance in mm. Default: 60")] double distance = 60.0,
        [Description("Dimension attributes file name (style). Default: standard")] string attributesFile = "standard",
        [Description("Comma-separated MATERIAL_TYPE codes to include (1=Steel,2=Concrete,5=Timber). Default: '1,2,5'. Empty string = include all.")] string includeMaterialTypes = "1,2,5")
    {
        if (distance <= 0)
            return "Error: 'distance' must be a positive number.";

        var json = RunBridge(
            "place_control_diagonals",
            viewId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            distance.ToString(CultureInfo.InvariantCulture),
            attributesFile ?? "standard",
            includeMaterialTypes ?? string.Empty);
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
