using System;
using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

public static partial class DrawingCommandParsers
{
    public static IReadOnlyCollection<string> ParseViewDimensionReferenceZones(string[] args)
    {
        if (args.Length <= 13 || string.IsNullOrWhiteSpace(args[13])) return Array.Empty<string>();
        string[] zones;
        try { zones = System.Text.Json.JsonSerializer.Deserialize<string[]>(args[13])
            ?? throw new ArgumentException("referenceZones must be a JSON array of strings"); }
        catch (System.Text.Json.JsonException ex) { throw new ArgumentException("referenceZones must be a JSON array of strings", ex); }
        zones = ReferenceZoneOutline.Normalize(zones);
        if (zones.Length > 0 && (args.Length <= 10 || !string.Equals(args[10].Trim(), "panel", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("referenceZones requires ruleSet=panel");
        return zones;
    }

    /// <summary>
    /// Independent of the panel's own excludePrefixes/excludeMaterials (args[4]/args[5]): a part
    /// kept out of the measured layer there must still be eligible as the reference layer, since
    /// it is only excluded from what is measured, not from the panel itself.
    /// </summary>
    public static (string? Prefixes, string? Materials) ParseViewDimensionReferenceExclusions(string[] args) =>
        (args.Length > 14 ? args[14] : null, args.Length > 15 ? args[15] : null);

    public static IReadOnlyList<PartLayerRule> ParseViewDimensionLayerRules(string[] args)
    {
        var json = TeklaMcpServer.Shared.PartLayerRulesJson.Parse(args.Length > 12 ? args[12] : null);
        if (!json.HasValue) return Array.Empty<PartLayerRule>();
        var rules = json.Value.EnumerateArray().Select(rule => new PartLayerRule(
            rule.GetProperty("id").GetString()!, rule.GetProperty("className").GetString()!,
            rule.GetProperty("conditions").EnumerateArray().Select(condition => new PartLayerCondition(
                (PartLayerProperty)Enum.Parse(typeof(PartLayerProperty), condition.GetProperty("property").GetString()!, true),
                condition.GetProperty("value").GetString()!,
                condition.TryGetProperty("matchKind", out var match)
                    ? (PartLayerMatchKind)Enum.Parse(typeof(PartLayerMatchKind), match.GetString()!, true) : PartLayerMatchKind.Equals)),
            rule.TryGetProperty("priority", out var priority) ? priority.GetInt32() : 0,
            rule.TryGetProperty("data", out var data)
                ? data.EnumerateObject().ToDictionary(entry => entry.Name, entry => entry.Value) : null)).ToArray();
        return PartRoleClassifier.ValidateLayerRules(rules);
    }

    public static PartGeometryInViewParseResult ParsePartGeometryInViewRequest(string[] args)
    {
        if (args.Length < 3
            || !int.TryParse(args[1], out var viewId)
            || !int.TryParse(args[2], out var modelId))
        {
            return PartGeometryInViewParseResult.Fail("Usage: get_part_geometry_in_view <viewId> <modelId>");
        }

        return PartGeometryInViewParseResult.Success(new PartGeometryInViewRequest
        {
            ViewId = viewId,
            ModelId = modelId
        });
    }

    public static PartPointsInViewParseResult ParsePartPointsInViewRequest(string[] args)
    {
        if (args.Length < 3
            || !int.TryParse(args[1], out var viewId)
            || !int.TryParse(args[2], out var modelId))
        {
            return PartPointsInViewParseResult.Fail("Usage: get_part_points_in_view <viewId> <modelId>");
        }

        return PartPointsInViewParseResult.Success(new PartPointsInViewRequest
        {
            ViewId = viewId,
            ModelId = modelId
        });
    }

    public static GridAxesParseResult ParseGridAxesRequest(string[] args)
    {
        if (args.Length < 2 || !int.TryParse(args[1], out var viewId))
            return GridAxesParseResult.Fail("Usage: get_grid_axes <viewId>");

        return GridAxesParseResult.Success(new GridAxesRequest
        {
            ViewId = viewId
        });
    }

    public static DrawingViewContextParseResult ParseDrawingViewContextRequest(string[] args)
    {
        if (args.Length < 2 || !int.TryParse(args[1], out var viewId))
            return DrawingViewContextParseResult.Fail("Usage: get_drawing_view_context <viewId>");

        return DrawingViewContextParseResult.Success(new DrawingViewContextRequest
        {
            ViewId = viewId
        });
    }
}
