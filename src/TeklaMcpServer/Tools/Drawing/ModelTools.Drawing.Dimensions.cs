using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;

namespace TeklaMcpServer.Tools;

public static partial class ModelTools
{
    [McpServerTool, Description(
        "Capture one read-only dimension observation for a drawing view. " +
        "Combines drawing identity, view context and dimension contexts in one payload; " +
        "all geometry is in the view coordinate system. Does not modify the drawing or write files.")]
    public static string CaptureDimensionObservation(
        [Description("View ID to capture (from get_drawing_views)." )] int viewId)
    {
        var json = RunBridge("capture_dimension_observation", viewId.ToString(CultureInfo.InvariantCulture));
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
        "Get dimension contexts for one drawing view. " +
        "Returns reduced dimension-item contexts with geometry, role classification, source associations and annotation geometry. " +
        "Use together with get_drawing_view_context for external reasoning about dimensions.")]
    public static string GetDimensionContexts(
        [Description("View ID to read dimension contexts from (from get_drawing_views).")] int viewId)
    {
        var json = RunBridge("get_dimension_contexts", viewId.ToString(CultureInfo.InvariantCulture));
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
        "Check one dimension against a specific anchor question: whether its points land on real part geometry. " +
        "Read-only; use in review mode for a targeted anchor check, not as a mandatory pre-placement scan. " +
        "Reports each point as matched, on a fallback (fallbackOnly), or missing entirely, per point.")]
    public static string GetDimensionChainCoverage(
        [Description("ID of the drawing view (from get_drawing_views)")] int viewId,
        [Description("ID of the dimension set to check (from get_drawing_dimensions)")] int dimensionId,
        [Description("Match tolerance in view units. Default: 1.0")] double tolerance = 1.0)
    {
        var json = RunBridge(
            "get_dimension_chain_coverage",
            viewId.ToString(CultureInfo.InvariantCulture),
            dimensionId.ToString(CultureInfo.InvariantCulture),
            tolerance.ToString(CultureInfo.InvariantCulture));
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

    [McpServerTool, Description(
        "Read raw AngleDimension geometry and radius candidates for debugging manual angle dimension movement. " +
        "Returns Origin/Point1/Point2, Distance, view scale, angle type, and candidate points for Distance, Distance*Scale, and Distance/Scale.")]
    public static string GetAngleDimensionDebug(
        [Description("Optional drawing view ID. Omit to scan the whole sheet.")] int? viewId = null,
        [Description("Optional AngleDimension ID to limit output to one dimension.")] int? dimensionId = null)
    {
        var json = RunBridge(
            "get_angle_dimension_debug",
            viewId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            dimensionId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
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
        "Draw compact debug overlay geometry for AngleDimension objects. " +
        "If one or more AngleDimensions are selected, draws only selected dimensions; otherwise uses viewId/dimensionId or scans the sheet. " +
        "Shows angle rays, bisector, and point-average radius candidates with scale variants.")]
    public static string DrawAngleDimensionDebugGeometry(
        [Description("Optional drawing view ID. Omit to scan the whole sheet.")] int? viewId = null,
        [Description("Optional AngleDimension ID to limit output to one dimension.")] int? dimensionId = null,
        [Description("Overlay group name. Default: angle-dimension-debug")] string group = "angle-dimension-debug")
    {
        var json = RunBridge(
            "draw_angle_dimension_debug_geometry",
            viewId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            dimensionId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            group ?? "angle-dimension-debug");
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
        "Move an AngleDimension by changing its Distance offset. " +
        "deltaPaper is added directly to AngleDimension.Distance in paper millimeters; positive values move the angle arc/text outward. " +
        "AngleAtVertex is reported as not moved because Tekla saves Distance but does not move it visually.")]
    public static string MoveAngleDimension(
        [Description("AngleDimension ID from get_angle_dimension_debug or get_drawing_dimensions.")] int dimensionId,
        [Description("Delta to add to AngleDimension.Distance, in paper millimeters. Positive = outward, negative = inward.")] double deltaPaper)
    {
        var json = RunBridge(
            "move_angle_dimension",
            dimensionId.ToString(CultureInfo.InvariantCulture),
            deltaPaper.ToString(CultureInfo.InvariantCulture));
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
        "Arrange existing straight dimensions in the active drawing by analyzing parallel line stacks and increasing spacing where needed. " +
        "Optionally limit to one viewId. targetGap is in paper units; internally it is translated using the owning view scale. " +
        "When allowInwardCorrectionFromPartsBounds=true, the nearest chain may also be pulled toward the overall parts box to restore the exact target gap.")]
    public static string ArrangeDimensions(
        [Description("Optional drawing view ID. Omit to process all dimensions on the active drawing.")] int? viewId = null,
        [Description("Desired minimum gap between neighboring dimension lines in paper units. Default: 8")] double? targetGap = null,
        [Description("When true, also pull the nearest chain toward PartsBounds if it is farther than the target gap. Default: false")] bool allowInwardCorrectionFromPartsBounds = false)
    {
        if (targetGap.HasValue && (double.IsNaN(targetGap.Value) || double.IsInfinity(targetGap.Value) || targetGap < 0))
            return "Error: 'targetGap' must be a non-negative number.";

        var json = RunBridge(
            "arrange_dimensions",
            viewId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            targetGap?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            allowInwardCorrectionFromPartsBounds.ToString(CultureInfo.InvariantCulture));
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
        "Combine compatible existing straight dimensions into replacement dimension sets. " +
        "Uses the current packet-level combine analysis; optionally limit to one viewId or a comma-separated subset of dimension IDs. " +
        "Set previewOnly=true to inspect combine candidates without modifying the drawing.")]
    public static string CombineDimensions(
        [Description("Optional drawing view ID. Omit to scan the whole active drawing.")] int? viewId = null,
        [Description("Optional comma-separated list of dimension IDs. Only packets fully contained in this set are considered.")] string dimensionIds = "",
        [Description("When true, return combine candidates and previews without modifying the drawing. Default: false")] bool previewOnly = false)
    {
        var json = RunBridge(
            "combine_dimensions",
            viewId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            dimensionIds ?? string.Empty,
            previewOnly.ToString());
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
        "ADD points to an existing dimension chain without rebuilding it. Style and offset carry over, so " +
        "this is cheaper and safer than recreate_dimension when points are only being added. " +
        "IMPORTANT — Tekla RENUMBERS the merged chain: the id passed in stops resolving and the new one comes " +
        "back as mergedDimensionId; use that from then on. " +
        "At least 2 points must be supplied; to add a single point, pass it together with a point the chain already has.")]
    public static string AddDimensionPoints(
        [Description("ID of the dimension set to extend (from get_drawing_dimensions or get_dimension_contexts). STALE after a successful call — the merged chain is renumbered, so switch to mergedDimensionId.")] int dimensionId,
        [Description("Flat JSON array of model-space coordinates to merge in: [x0,y0,z0, x1,y1,z1, ...]. Minimum 2 points (6 numbers).")] string points,
        [Description("REQUIRED, no default — a wrong value builds the points along a different axis than the target chain. 'horizontal' (offset along Y), 'vertical' (offset along X), or a 'dx,dy,dz' vector for inclined chains. Read dimensionType from get_dimension_contexts for the chain being extended.")] string direction)
    {
        var json = RunBridge("add_dimension_points",
            dimensionId.ToString(CultureInfo.InvariantCulture),
            points,
            direction);
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var err) && err.GetString() is { Length: > 0 } e)
                return $"Error: {e}";

            var added = doc.RootElement.TryGetProperty("added", out var a) && a.GetBoolean();
            var after = doc.RootElement.TryGetProperty("pointCountAfter", out var p) ? p.GetInt32() : 0;
            var mergedId = doc.RootElement.TryGetProperty("mergedDimensionId", out var m) ? m.GetInt32() : 0;
            return added
                ? $"Merged points into dimension {dimensionId}; it now has {after} points and was renumbered to {mergedId} — use {mergedId} from now on.\n{JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true })}"
                : $"Failed to add points.\n{json}";
        }
        catch
        {
            return $"Bridge error: {json}";
        }
    }

    [McpServerTool, Description(
        "REBUILD a dimension chain from a new point list, carrying over its style and offset. " +
        "Use this only when points must be REMOVED: Tekla Open API cannot drop a point from an existing " +
        "chain, so the chain is deleted and recreated. IMPORTANT — the id CHANGES, and the old id stops " +
        "working; use newDimensionId from the response afterwards. If the replacement fails verification, the tool tries to " +
        "delete it and leave the original: that is guaranteed only when writeState.newDimensionRemoved is true; " +
        "if the cleanup failed the error says the replacement may still be on the sheet. Once the original delete has started " +
        "nothing is undone: an error then can leave both on the sheet or an uncertain state — re-read the view " +
        "before doing anything. To add points, use add_dimension_points instead.")]
    public static string RecreateDimension(
        [Description("ID of the dimension set to rebuild. Replaced only after verification; inspect writeState on failure.")] int dimensionId,
        [Description("Flat JSON array of view-local coordinates for the new chain: [x0,y0,z0, x1,y1,z1, ...]. Minimum 2 points (6 numbers).")] string points,
        [Description("REQUIRED, no default — passing the wrong one rebuilds a vertical chain as horizontal. 'horizontal' (offset along Y), 'vertical' (offset along X), or a custom 'dx,dy,dz' vector for inclined chains. Read dimensionType from get_dimension_contexts for the chain being rebuilt.")] string direction,
        [Description("Non-negative offset. Omit to reuse original Distance. Direction sets the side. Replacement offset is corrected and read back before deleting the original. On failure inspect both IDs and writeState; do not retry blindly.")] double? distance = null)
    {
        var json = RunBridge("recreate_dimension",
            dimensionId.ToString(CultureInfo.InvariantCulture),
            points,
            direction,
            distance?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var err) && err.GetString() is { Length: > 0 } e)
                return $"Error: {e}\n{json}";

            var ok = doc.RootElement.TryGetProperty("recreated", out var r) && r.GetBoolean();
            var newId = doc.RootElement.TryGetProperty("newDimensionId", out var n) ? n.GetInt32() : 0;
            var kept = doc.RootElement.TryGetProperty("attributesKept", out var k) && k.GetBoolean();
            return ok
                ? $"Recreated dimension {dimensionId} as {newId} (original attributes supplied: {kept}). Use {newId} from now on.\n{JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true })}"
                : $"Failed to recreate dimension.\n{json}";
        }
        catch
        {
            return $"Bridge error: {json}";
        }
    }

    [McpServerTool, Description("Create and read back a straight dimension set from view-local points [x0,y0,z0, ...]. By default calculates distance so the line sits 8 paper mm beyond the assembly outline; paperGapMm overrides that. Supply distance to use an explicit view-unit distance instead. If read-back fails, the new set is deleted and its absence confirmed (writeState.newDimensionRemoved, dimensionId 0); if that cleanup itself fails the error says the set may still be on the sheet, so re-read the view before retrying.")]
    public static string CreateDimension(
        [Description("ID of the drawing view to place the dimension in")] int viewId,
        [Description("Flat JSON array of view-local coordinates: [x0,y0,z0, x1,y1,z1, ...]. Minimum 2 points (6 numbers).")] string points,
        [Description("Direction of the dimension offset: 'horizontal' (offset up, dimension left-right), 'vertical' (offset right, dimension up-down), or custom 'dx,dy,dz' vector. Default: horizontal")] string direction = "horizontal",
        [Description("Optional explicit offset distance in view units. Omit to calculate it from the assembly outline and paperGapMm.")] double? distance = null,
        [Description("Dimension attributes file name (style). Default: standard")] string attributesFile = "standard",
[Description("Paper-space gap beyond the assembly outline in mm when distance is omitted. Default: 8.")] double? paperGapMm = null,
        [Description("Excluded prefixes, as in the candidate query. Empty means none.")] string excludePrefixes = "",
        [Description("Excluded material substrings, as in the candidate query. Empty means none.")] string excludeMaterials = "")
    {
        var json = RunBridge("create_dimension",
            viewId.ToString(CultureInfo.InvariantCulture),
            points,
            direction,
            distance?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            attributesFile,
            paperGapMm?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            excludePrefixes, excludeMaterials);
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var err) && err.GetString() is { Length: > 0 } e)
                return $"Error: {e}\n{json}";

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

    [McpServerTool, Description("Place radius dimensions on arc (rounded chamfer) segments of the single ContourPlate in a single-part drawing view. Uses GetContourPolycurve to identify arc segments (CHAMFER_ROUNDING / CHAMFER_ARC) and places a RadiusDimension on each. If viewId is omitted, FrontView is preferred, otherwise the largest view is used.")]
    public static string PlaceContourRadiusDimensions(
        [Description("Optional target view ID. Omit to use main view auto-selection.")] int? viewId = null,
        [Description("Radius dimension line distance from the arc in mm. Default: 0")] double distance = 0.0,
        [Description("Radius dimension attributes file name (style). Default: standard")] string attributesFile = "standard")
    {
        if (distance < 0)
            return "Error: 'distance' must be a non-negative number.";

        var json = RunBridge(
            "place_contour_radius_dimensions",
            viewId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            distance.ToString(CultureInfo.InvariantCulture),
            attributesFile ?? "standard");
        try
        {
            var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return $"Bridge error: {json}";
        }
    }

    [McpServerTool, Description("Place interior angle dimensions at every vertex of the single ContourPlate in a single-part drawing view. If viewId is omitted, FrontView is preferred, otherwise the largest view is used. Detects plate/view normal flip to keep selecting the interior angle.")]
    public static string PlaceContourAngleDimensions(
        [Description("Optional target view ID. Omit to use main view auto-selection.")] int? viewId = null,
        [Description("Angle dimension arc radius (offset from vertex) in mm. Default: 4")] double distance = 4.0,
        [Description("Angle dimension attributes file name (style). Default: standard")] string attributesFile = "standard",
        [Description("Skip right angles (~90°): do not dimension square corners. Default: true")] bool skipRightAngles = true)
    {
        if (distance < 0)
            return "Error: 'distance' must be a non-negative number.";

        var json = RunBridge(
            "place_contour_angle_dimensions",
            viewId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            distance.ToString(CultureInfo.InvariantCulture),
            attributesFile ?? "standard",
            skipRightAngles ? "true" : "false");
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
