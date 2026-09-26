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
    public static object Short(object chain)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(chain));
        var result = new Dictionary<string, object?>();
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            var value = property.Value;
            if (property.Name is "points" or "side") continue;
            if (value.ValueKind == System.Text.Json.JsonValueKind.Null) continue;
            if (value.ValueKind == System.Text.Json.JsonValueKind.Array && value.GetArrayLength() == 0
                && property.Name is "skippedPartIds" or "droppedShortPointIds" or "offeredOnOtherSidePartIds"
                    or "unlocatedPartIds" or "locatedByProfilePartIds" or "mergedNearbyPartIds") continue;
            // Numbers go through decimal so the answer prints 1088.417, not its binary tail.
            result[property.Name] = value.ValueKind == System.Text.Json.JsonValueKind.Array
                ? value.EnumerateArray().Select(e => e.ValueKind == System.Text.Json.JsonValueKind.Number
                    ? (object)Math.Round((decimal)e.GetDouble(), 3) : e.Clone()).ToArray()
                : value.Clone();
        }
        return result;
    }

    /// <summary>Both chains of a side, refused with a reason instead of built.</summary>
    public static object[] Refused(DimensionChainSide side, string note) =>
        [Empty(side, "location", note), Empty(side, "overall", note)];

    public static object[] Build(DimensionPointCatalog catalog, DimensionChainSide side,
        IReadOnlyCollection<int> mainPartIds, double minSegmentLength)
    {
        var all = catalog.AllPoints.ToArray();
        var line = catalog.LinePoints(side);
        var main = all.Where(p => p.Parents.Any(x => x.ModelId is { } id && mainPartIds.Contains(id))).ToArray();
        if (main.Length == 0)
            return [Empty(side, "location", "main part has no dimension points")];

        var alongX = side is DimensionChainSide.Top or DimensionChainSide.Bottom;
        var mainAlongX = main.Max(p => p.X) - main.Min(p => p.X) >= main.Max(p => p.Y) - main.Min(p => p.Y);
        var ctx = new Ctx(alongX,
            side is DimensionChainSide.Top or DimensionChainSide.Right ? 1 : -1, line);

        // Across the main part the location chain already spans the whole width, so no overall.
        var overall = alongX == mainAlongX
            ? Overall(side, line, ctx)
            : Empty(side, "overall", "across the main part the location chain already spans the overall");
        var ruleContext = DimensionRuleContext.FromCatalog(catalog,
            new SteelPartLocationInput(catalog, mainPartIds));
        var locationResult = new DimensionRuleSet(new SteelPartLocationRule(
            new SteelPartLocationSettings(side, minSegmentLength, SameCoordinate)))
            .Calculate(ruleContext).Results.Single();
        var location = SteelLocation(side, locationResult);
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

    private sealed class Ctx(bool alongX, int outer, IReadOnlyList<DimensionPoint> line)
    {
        public bool AlongX { get; } = alongX;
        public int Outer { get; } = outer;
        public IReadOnlyList<DimensionPoint> Line { get; } = line;
        public double Along(DimensionPoint p) => AlongX ? p.X : p.Y;
        public double Across(DimensionPoint p) => AlongX ? p.Y : p.X;
        public IEnumerable<int> Parts(DimensionPoint p) =>
            p.Parents.Where(x => x.ModelId.HasValue).Select(x => x.ModelId!.Value).Distinct();
    }

    private sealed class Pick(DimensionPoint point, string role, int priority)
    {
        public DimensionPoint Point { get; } = point;
        public string Role { get; } = role;
        public int Priority { get; } = priority;
    }

    private static object Overall(DimensionChainSide side, IReadOnlyList<DimensionPoint> line, Ctx ctx)
    {
        if (line.Count < 2) return Empty(side, "overall", "fewer than two points on this side");
        var picks = new List<Pick> {
            new(Best(line.Where(p => Math.Abs(ctx.Along(p) - line.Min(ctx.Along)) <= SameCoordinate), ctx), "extreme", 1),
            new(Best(line.Where(p => Math.Abs(ctx.Along(p) - line.Max(ctx.Along)) <= SameCoordinate), ctx), "extreme", 1)
        };
        return Result(side, picks, ctx);
    }

    // The point farthest to the outside; a real part edge beats a bare group extent; then id.
    private static DimensionPoint Best(IEnumerable<DimensionPoint> points, Ctx ctx) => points
        .OrderByDescending(p => ctx.Outer * ctx.Across(p))
        .ThenByDescending(p => p.Parents.Any(x => x.ModelId.HasValue))
        .ThenBy(p => p.Id, StringComparer.Ordinal).First();

    private static object Result(DimensionChainSide side, List<Pick> picks, Ctx ctx)
    {
        // One point per coordinate: the point farthest toward the dimension line wins, so the
        // witness line does not run along a part outline; then the strongest role. All roles are kept.
        var chosen = new List<(DimensionPoint Point, List<string> Roles)>();
        foreach (var group in ClusterPicks(picks, ctx))
        {
            var winner = group.OrderByDescending(x => ctx.Outer * ctx.Across(x.Point))
                .ThenByDescending(x => x.Priority)
                .ThenBy(x => x.Point.Id, StringComparer.Ordinal).First();
            chosen.Add((Outermost(winner.Point, ctx), group.Select(x => x.Role).Distinct().ToList()));
        }

        var ordered = chosen.OrderBy(x => ctx.Along(x.Point)).ToArray();
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

    // The witness line starts at the point farthest toward the dimension line on the same coordinate,
    // whichever part it belongs to, so it does not run along a part outline.
    private static DimensionPoint Outermost(DimensionPoint point, Ctx ctx) => ctx.Line
        .Where(p => Math.Abs(ctx.Along(p) - ctx.Along(point)) <= SameCoordinate)
        .OrderByDescending(p => ctx.Outer * ctx.Across(p))
        .ThenBy(p => p.Id == point.Id ? 0 : 1).ThenBy(p => p.Id, StringComparer.Ordinal)
        .FirstOrDefault() ?? point;

    private static IEnumerable<List<Pick>> ClusterPicks(List<Pick> picks, Ctx ctx)
    {
        var current = new List<Pick>();
        foreach (var pick in picks.OrderBy(p => ctx.Along(p.Point)))
        {
            if (current.Count > 0 && ctx.Along(pick.Point) - ctx.Along(current[current.Count - 1].Point) > SameCoordinate)
            {
                yield return current;
                current = new List<Pick>();
            }
            current.Add(pick);
        }
        if (current.Count > 0) yield return current;
    }

    private static object Empty(DimensionChainSide side, string kind, string note,
        IReadOnlyCollection<int>? skipped = null, IReadOnlyCollection<string>? dropped = null) => new {
        side = side.ToString(), kind, pointIds = Array.Empty<string>(), segments = Array.Empty<double>(),
        points = Array.Empty<object>(), skippedPartIds = (skipped ?? Array.Empty<int>()).ToArray(),
        droppedShortPointIds = (dropped ?? Array.Empty<string>()).ToArray(), note
    };
}
