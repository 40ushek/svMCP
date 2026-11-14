using System;
using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// Preview of the standard chains for one side, chosen from the context's dimension points.
/// Read-only: it selects point ids and reports why; it never writes a dimension.
/// </summary>
internal static class DimensionChainPreview
{
    private const double SameCoordinate = 0.01;

    /// <summary>
    /// The default answer: side, kind, ids and segments, plus notes and lists only when they
    /// have content. Roles and part ids stay behind the detailed question.
    /// </summary>
    public static object Short(object chain, bool detailed = false, string? side = null)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(chain));
        var result = new Dictionary<string, object?>();
        if (side != null)
        {
            var kind = doc.RootElement.GetProperty("kind").GetString();
            result["key"] = side + "-" + kind;
            result["direction"] = side switch {
                "Top" => "horizontal", "Bottom" => "horizontal-down",
                "Left" => "vertical-left", "Right" => "vertical",
                _ => throw new ArgumentException("Unknown preview side")
            };
            result["attributesFile"] = kind == "overall" ? "overall" : "standard";
        }
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            var value = property.Value;
            if (property.Name == "row" || (!detailed && (property.Name is "points" or "side"))) continue;
            if (value.ValueKind == System.Text.Json.JsonValueKind.Null) continue;
            if (!detailed && value.ValueKind == System.Text.Json.JsonValueKind.Array && value.GetArrayLength() == 0
                && property.Name is "skippedPartIds" or "droppedShortPointIds" or "offeredOnOtherSidePartIds"
                    or "unlocatedPartIds" or "locatedByProfilePartIds" or "mergedNearbyPartIds") continue;
            // Numbers go through decimal so the answer prints 1088.417, not its binary tail.
            result[property.Name] = value.ValueKind == System.Text.Json.JsonValueKind.Array
                ? value.EnumerateArray().Select(e => e.ValueKind == System.Text.Json.JsonValueKind.Number
                    ? (object)Math.Round((decimal)e.GetDouble(), 3) : e.Clone()).ToArray()
                : value.Clone();
        }
        if (!result.ContainsKey("incomplete"))
        {
            var incomplete = doc.RootElement.GetProperty("pointIds").GetArrayLength() < 2;
            result["incomplete"] = incomplete;
            if (incomplete)
                result["incompleteReason"] = doc.RootElement.TryGetProperty("note", out var note)
                    && note.ValueKind == System.Text.Json.JsonValueKind.String
                    ? note.GetString()! : "fewer than two selected points";
        }
        return result;
    }

    /// <summary>Both chains of a side, refused with a reason instead of built.</summary>
    public static object[] Refused(DimensionChainSide side, string note) =>
        [Empty(side, "location", note), Empty(side, "overall", note)];

    public static object[] Build(DimensionPointCatalog catalog, DimensionChainSide side,
        IReadOnlyCollection<int> mainPartIds, double minSegmentLength,
        DimensionPartAnchor partAnchor = DimensionPartAnchor.LowFace,
        DimensionPartAnchor endPartAnchor = DimensionPartAnchor.BothFaces,
        IReadOnlyCollection<int>? inclinedPartIds = null, IReadOnlyCollection<int>? faceOnPartIds = null,
        IReadOnlyDictionary<int, double>? partThickness = null, IReadOnlyCollection<int>? carriedPartIds = null)
    {
        var all = catalog.AllPoints.ToArray();
        var line = catalog.LinePoints(side);
        var main = all.Where(p => p.Parents.Any(x => x.ModelId is { } id && mainPartIds.Contains(id))).ToArray();
        if (main.Length == 0)
            return [Empty(side, "location", "main part has no dimension points")];

        var alongX = side.RunsAlongX();
        var mainAlongX = main.Max(p => p.X) - main.Min(p => p.X) >= main.Max(p => p.Y) - main.Min(p => p.Y);
        var ctx = new ChainAxis(alongX, side.OutwardSign(), line, SameCoordinate);

        // Across the main part the location chain already spans the whole width, so no overall.
        var overall = alongX == mainAlongX
            ? Overall(side, line, ctx)
            : Empty(side, "overall", "across the main part the location chain already spans the overall");
        var ruleContext = DimensionRuleContext.FromCatalog(catalog,
            new SteelPartLocationInput(catalog, mainPartIds, inclinedPartIds, faceOnPartIds, partThickness, carriedPartIds));
        var locationResult = new DimensionRuleSet(new SteelPartLocationRule(
            new SteelPartLocationSettings(side, minSegmentLength, SameCoordinate, partAnchor, endPartAnchor)))
            .Calculate(ruleContext).Results.Single();
        var location = SteelLocation(side, locationResult);
        // A location chain of just the two extremes is the overall itself, whatever the shape of
        // the main part (a stub shorter than its height): one dimension, not two.
        if (locationResult.Note == null && locationResult.Points.Count == 2 && line.Count >= 2)
        {
            var along = locationResult.Points.Select(point => alongX ? point.X : point.Y).ToArray();
            var (from, to) = (along.Min(), along.Max());
            if (Math.Abs(from - line.Min(ctx.Along)) <= SameCoordinate && Math.Abs(to - line.Max(ctx.Along)) <= SameCoordinate)
                overall = Empty(side, "overall", "the location chain already spans the overall");
        }
        return [location, overall];
    }

    private static object SteelLocation(DimensionChainSide side, DimensionRuleResult result)
    {
        var skipped = (int[])result.Evidence["skippedPartIds"];
        var dropped = (string[])result.Evidence["droppedShortPointIds"];
        if (result.Note != null)
            return Empty(side, result.Kind, result.Note, skipped, dropped);
        var roles = (string[][])result.Evidence["roles"];
        var partIds = (int[][])result.Evidence["partIds"];
        return new {
            side = side.ToString(), kind = result.Kind,
            pointIds = result.Points.Select(point => point.Id).ToArray(),
            segments = result.Segments,
            points = result.Points.Select((point, index) => new {
                pointId = point.Id, roles = roles[index], partIds = partIds[index]
            }).ToArray(),
            skippedPartIds = skipped,
            offeredOnOtherSidePartIds = (int[])result.Evidence["offeredOnOtherSidePartIds"],
            droppedShortPointIds = dropped,
            note = (string?)null
        };
    }

    private static object Overall(DimensionChainSide side, IReadOnlyList<DimensionPoint> line, ChainAxis ctx)
    {
        if (line.Count < 2) return Empty(side, "overall", "fewer than two points on this side");
        var picks = new List<ChainPick> {
            new(ctx.Best(line.Where(p => Math.Abs(ctx.Along(p) - line.Min(ctx.Along)) <= SameCoordinate)), "extreme", 1),
            new(ctx.Best(line.Where(p => Math.Abs(ctx.Along(p) - line.Max(ctx.Along)) <= SameCoordinate)), "extreme", 1)
        };
        return Result(side, picks, ctx);
    }

    private static object Result(DimensionChainSide side, List<ChainPick> picks, ChainAxis ctx)
    {
        var ordered = ctx.Settle(picks).OrderBy(x => ctx.Along(x.Point)).ToArray();
        return new {
            side = side.ToString(), kind = "overall",
            pointIds = ordered.Select(x => x.Point.Id).ToArray(),
            segments = ordered.Zip(ordered.Skip(1), (a, b) => Math.Round(ctx.Along(b.Point) - ctx.Along(a.Point), 3)).ToArray(),
            points = ordered.Select(x => new {
                pointId = x.Point.Id, roles = x.Roles,
                partIds = ctx.Parts(x.Point).ToArray()
            }).ToArray(),
            skippedPartIds = Array.Empty<int>(),
            offeredOnOtherSidePartIds = Array.Empty<int>(),
            droppedShortPointIds = Array.Empty<string>(),
            note = (string?)null
        };
    }

    private static object Empty(DimensionChainSide side, string kind, string note,
        IReadOnlyCollection<int>? skipped = null, IReadOnlyCollection<string>? dropped = null) => new {
        side = side.ToString(), kind, pointIds = Array.Empty<string>(), segments = Array.Empty<double>(),
        points = Array.Empty<object>(), skippedPartIds = (skipped ?? Array.Empty<int>()).ToArray(),
        droppedShortPointIds = (dropped ?? Array.Empty<string>()).ToArray(), note
    };
}
