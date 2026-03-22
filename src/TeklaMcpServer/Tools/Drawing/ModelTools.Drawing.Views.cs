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

    [McpServerTool, Description("Get section placement sides (Top/Bottom/Left/Right) for all section views in the active drawing, based on coordinate system analysis")]
    public static string GetDrawingSectionSides()
    {
        var json = RunBridge("get_drawing_section_sides");
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

            var updatedCount = doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("updatedCount", out var u) ? u.GetInt32() : 0;
            return $"Updated scale to 1:{scale} for {updatedCount} view(s).\n{JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true })}";
        }
        catch
        {
            return $"Bridge error: {json}";
        }
    }

    [McpServerTool, Description("Get all StraightDimensionSet objects from the active drawing (or a specific view). The response keeps the old segment points/distance fields and also includes view ownership, orientation, and bounding boxes for sets and segments.")]
    public static string GetDrawingDimensions(
        [Description("View ID to read dimensions from (from get_drawing_views). Omit to get all dimensions on the drawing.")] int? viewId = null)
    {
        var arg = viewId.HasValue ? viewId.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;
        var json = RunBridge("get_drawing_dimensions", arg);
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

    [McpServerTool, Description("Move a StraightDimensionSet by changing its dimension line offset (Distance). Positive delta moves the line away from measured points, negative — closer. dimensionId from get_drawing_dimensions.")]
    public static string MoveDimension(
        [Description("Dimension set ID from get_drawing_dimensions")] int dimensionId,
        [Description("Delta to add to the dimension line offset (mm). Positive = further, negative = closer.")] double delta)
    {
        var json = RunBridge("move_dimension",
            dimensionId.ToString(CultureInfo.InvariantCulture),
            delta.ToString(CultureInfo.InvariantCulture));
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

    [McpServerTool, Description("Get all model objects (parts, assemblies) referenced by the active drawing, with type, PART_POS, ASSEMBLY_POS, PROFILE, MATERIAL, NAME. Uses direct DrawingHandler.GetModelObjectIdentifiers — no sheet enumeration.")]
    public static string GetDrawingParts()
    {
        var json = RunBridge("get_drawing_parts");
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

    [McpServerTool, Description("Automatically resolve overlapping marks on the active drawing by iteratively pushing them apart. Returns how many marks were moved and remaining overlap count.")]
    public static string ResolveMarkOverlaps(
        [Description("Minimum gap between mark bounding boxes after resolution (mm). Default: 2.")] double margin = 2.0)
    {
        var json = RunBridge("resolve_mark_overlaps", margin.ToString(CultureInfo.InvariantCulture));
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

    [McpServerTool, Description("Arrange all marks on the active drawing to optimal positions around their anchor points, minimising overlap and leader line length. Use this when marks are displaced or clustered. For minor overlap fixes use resolve_mark_overlaps instead.")]
    public static string ArrangeMarks(
        [Description("Minimum gap between mark bounding boxes (mm). Default: 2.")] double gap = 2.0)
    {
        var json = RunBridge("arrange_marks", gap.ToString(CultureInfo.InvariantCulture));
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

    [McpServerTool, Description("One command for mark collision cleanup: runs arrange_marks, then resolve_mark_overlaps repeatedly until no overlaps remain or maxPasses is reached.")]
    public static string ArrangeMarksNoCollisions(
        [Description("Minimum gap for arrange_marks (mm). Default: 2.")] double gap = 2.0,
        [Description("Margin for resolve_mark_overlaps (mm). Default: 2.")] double margin = 2.0,
        [Description("Maximum resolve passes after arrange. Default: 3.")] int maxPasses = 3)
    {
        if (gap < 0)
            return "Error: 'gap' must be >= 0.";
        if (margin < 0)
            return "Error: 'margin' must be >= 0.";
        if (maxPasses < 1)
            return "Error: 'maxPasses' must be >= 1.";

        var json = RunBridge(
            "arrange_marks_no_collisions",
            gap.ToString(CultureInfo.InvariantCulture),
            margin.ToString(CultureInfo.InvariantCulture),
            maxPasses.ToString(CultureInfo.InvariantCulture));
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

    [McpServerTool, Description("Delete all marks on the active drawing. Use before recreating marks to start fresh.")]
    public static string DeleteAllMarks()
    {
        var json = RunBridge("delete_all_marks");
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("error", out var err))
                return $"Error: {err.GetString()}";

            var deleted = doc.RootElement.TryGetProperty("deletedCount", out var d) ? d.GetInt32() : 0;
            return $"Deleted {deleted} marks.";
        }
        catch
        {
            return $"Bridge error: {json}";
        }
    }

    [McpServerTool, Description("Fit all views to the sheet: auto-calculates the optimal standard scale and arranges views without overlaps. Set keepScale=true to preserve existing view scales and only rearrange positions. titleBlockHeight is an optional manual bottom reserve for compatibility with older scripts.")]
    public static string FitViewsToSheet(
        [Description("Margin from sheet edges in mm. Default: 0 = auto-read from drawing layout")] double margin = 0,
        [Description("Gap between views in mm. Default: 4")] double gap = 4,
        [Description("Optional manual reserved height at the bottom of the sheet in mm. Default: 0")] double titleBlockHeight = 0,
        [Description("If true, keep existing view scales and only rearrange positions. Default: false")] bool keepScale = false)
    {
        var marginStr         = margin.ToString(CultureInfo.InvariantCulture);
        var gapStr            = gap.ToString(CultureInfo.InvariantCulture);
        var titleBlockStr     = titleBlockHeight.ToString(CultureInfo.InvariantCulture);
        var json = keepScale
            ? RunBridge("fit_views_to_sheet", marginStr, gapStr, titleBlockStr, "keepscale")
            : RunBridge("fit_views_to_sheet", marginStr, gapStr, titleBlockStr);
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("error", out var err))
                return string.Concat("Error: ", err.GetString());

            var optScale = doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("optimalScale", out var s) ? s.GetDouble() : 0;
            var count    = doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("arranged", out var a) ? a.GetInt32() : 0;
            var details  = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
            return string.Concat("Arranged ", count, " views at scale 1:", optScale, System.Environment.NewLine, details);
        }
        catch
        {
            return string.Concat("Bridge error: ", json);
        }
    }

    [McpServerTool, Description("Debug: read reserved areas (tables, title block) as detected by the layout algorithm. Returns both raw per-table geometry and merged reserved rects used for view placement.")]
    public static string GetDrawingReservedAreas()
    {
        var json = RunBridge("get_drawing_reserved_areas");
        try
        {
            var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return string.Concat("Bridge error: ", json);
        }
    }
}
