using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;

namespace TeklaMcpServer.Tools;

public static partial class ModelTools
{
    // Test-only probe: exposes the Clipper2 result for visual comparison with a live view.
    // Dimension placement will use the outline API directly, not call this MCP tool.
    [McpServerTool, Description(
        "Get geometry (bboxMin, bboxMax, startPoint, endPoint, axes, solid vertices) for ALL parts in a drawing view in a single call. " +
        "Returns type, name, partPos, profile, material and full view-local coordinates for every part. " +
        "Use instead of calling get_part_geometry_in_view N times — dramatically faster for dimension placement.")]
    public static string GetAllPartsGeometryInView(
        [Description("ID of the drawing view (from get_drawing_views)")] int viewId)
    {
        return RunBridge("get_all_parts_geometry_in_view", viewId.ToString());
    }

    [McpServerTool, Description(
        "Get the projected outline of the parts that define an assembly's size in one drawing view - the frame, without insulation, cladding or fixings. " +
        "Parts are chosen by their role, read from their properties, so the caller does not need to know the plant's mark prefixes. " +
        "Reports what it could not classify: an extent over a set containing unknowns is a guess, not a fact. " +
        "Read-only geometry evidence; it does not create or change dimensions.")]
    public static string GetStructuralOutline(
        [Description("ID of the drawing view (from get_drawing_views)")] int viewId)
    {
        return RunBridge("get_structural_outline", viewId.ToString());
    }

    [McpServerTool, Description(
        "Get candidate dimension points where the parts drawn in one view touch each other, in view coordinates. " +
        "Each point names BOTH parts of the contact, because a touching surface belongs to the pair, not to one of them. " +
        "Contacts seen edge-on come back as a line with two ends rather than a patch with four corners - that is the normal case on an elevation. " +
        "Reports what was missing: parts never read, regions that flattened to nothing, shapes whose parts could not be named. " +
        "Set draw=true to paint the shapes and points into the drawing for visual checking. " +
        "Each view gets its own overlay group 'contact_candidates:<viewId>', so drawing a second view does not erase the first; " +
        "clear_debug_overlay contact_candidates clears them all, clear_debug_overlay contact_candidates:<viewId> clears one. " +
        "Read-only evidence; it does not create or change dimensions.")]
    public static string GetContactCandidatePoints(
        [Description("ID of the drawing view (from get_drawing_views)")] int viewId,
        [Description("Draw the shapes and points into the drawing as a debug overlay")] bool draw = false)
    {
        return RunBridge("get_contact_candidate_points", viewId.ToString(), draw ? "true" : "false");
    }

    [McpServerTool, Description(
        "Get the exact projected external outline of all visible parts in one drawing view, or of a chosen subset of them. " +
        "Returns Clipper2 polygon trees: outer contours, holes and disconnected components, plus incomplete-read warnings. " +
        "Pass modelIds to measure over part of the view - the frame alone, say - and the answer reports that it was restricted. " +
        "This is geometry evidence for future dimension placement; it does not create or change dimensions.")]
    public static string GetAssemblyOutline(
        [Description("ID of the drawing view (from get_drawing_views)")] int viewId,
        [Description("Optional comma-separated model IDs to measure over. Omit for every visible part.")] string modelIds = "")
    {
        var json = string.IsNullOrWhiteSpace(modelIds)
            ? RunBridge("get_assembly_outline", viewId.ToString())
            : RunBridge("get_assembly_outline", viewId.ToString(), modelIds);
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var err) && err.GetString() is { Length: > 0 } error)
                return $"Error: {error}";
            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return $"Bridge error: {json}";
        }
    }

    [McpServerTool, Description(
        "Get the shared drawing view context for one drawing view. " +
        "Returns view-local part geometry, deduplicated bolt groups, parts bounds, parts hull, grid IDs, warnings and view scale. " +
        "Use this as the common read context for dimension and mark reasoning.")]
    public static string GetDrawingViewContext(
        [Description("ID of the drawing view (from get_drawing_views)")] int viewId)
    {
        var json = RunBridge("get_drawing_view_context", viewId.ToString());
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
        "Get the geometry of a model part (beam, plate, etc.) expressed in the coordinate system of a specific drawing view. " +
        "Returns start/end points, bounding box, solid vertices and local axes — all in view-local coordinates (mm). " +
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
        "Get characteristic semantic points for ALL parts in a drawing view in a single call. " +
        "Returns axis-based points, bbox-based points, solid vertices, hull vertices, extreme points, center and directional points in view-local coordinates. " +
        "Use this as the canonical source for dimension anchor point discovery.")]
    public static string GetAllPartPointsInView(
        [Description("ID of the drawing view (from get_drawing_views)")] int viewId)
    {
        return RunBridge("get_all_part_points_in_view", viewId.ToString());
    }

    [McpServerTool, Description(
        "Get characteristic semantic points for one model part in one drawing view. " +
        "Returns point kinds such as AxisStart, AxisEnd, AxisMidpoint, Origin, Center, BboxMin, BboxMax, bbox corner points, SolidVertex, HullVertex, ExtremeStart, ExtremeEnd, Left, Right, Top and Bottom. " +
        "All coordinates are returned in the drawing view coordinate system (mm).")]
    public static string GetPartPointsInView(
        [Description("ID of the drawing view (from get_drawing_views)")] int viewId,
        [Description("Model object ID of the part (from get_drawing_parts)")] int modelId)
    {
        var json = RunBridge("get_part_points_in_view",
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
        "Draw a developer debug geometry overlay for all currently selected drawing marks using resolved mark geometry. " +
        "Requires one or more selected Marks. For each mark draws a green polygon, a yellow center cross, and for axis-based marks also a cyan axis line.")]
    public static string DrawSelectedMarkPartAxisGeometry()
    {
        return RunBridge("draw_selected_mark_part_axis_geometry");
    }

    [McpServerTool, Description(
        "Draw debug polygons for text objects found inside the currently selected drawing mark. " +
        "Requires exactly one selected Mark. This probes real text boxes inside the mark instead of mark layout geometry.")]
    public static string DrawSelectedMarkTextBoxes()
    {
        var json = RunBridge("draw_selected_mark_text_boxes");
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

    [McpServerTool, Description(
        "Draw the resolved geometry polygon for the currently selected drawing mark. " +
        "Requires exactly one selected Mark. This uses the same canonical resolved geometry path as mark layout and collision detection.")]
    public static string DrawSelectedMarkResolvedGeometry()
    {
        var json = RunBridge("draw_selected_mark_resolved_geometry");
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

    [McpServerTool, Description(
        "Draw debug mark boxes for marks in the active drawing using native object-aligned text bounding boxes. " +
        "Degenerate marks with zero-size geometry are skipped. " +
        "Optionally limit to a single view by viewId.")]
    public static string DrawMarkBoxes(
        [Description("Optional view ID to limit drawing to one view")] int? viewId = null,
        [Description("Overlay group name. Default: mark-boxes")] string group = "mark-boxes",
        [Description("If true, clear the overlay group before drawing. Default: true")] bool clearFirst = true)
    {
        var json = RunBridge(
            "draw_mark_boxes",
            viewId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            group ?? "mark-boxes",
            clearFirst ? "true" : "false");
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

    [McpServerTool, Description(
        "Clear previously drawn developer debug overlay objects from the active drawing. " +
        "If group is empty, clears all svMCP debug overlay objects.")]
    public static string ClearDebugOverlay(
        [Description("Optional overlay group name to clear")] string group = "")
    {
        return RunBridge("clear_debug_overlay", group ?? string.Empty);
    }

}
