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
                && property.Name is "skippedPartIds" or "droppedShortPointIds" or "offeredOnOtherSidePartIds") continue;
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
        var ctx = new Ctx(alongX, side is DimensionChainSide.Top or DimensionChainSide.Right ? 1 : -1, mainPartIds, line, all, main, catalog.LinePoints(Opposite(side)));

        // Across the main part the location chain already spans the whole width, so no overall.
        var overall = alongX == mainAlongX
            ? Overall(side, line, ctx)
            : Empty(side, "overall", "across the main part the location chain already spans the overall");
        var location = alongX == mainAlongX
            ? AlongMain(side, line, ctx, minSegmentLength)
            : AcrossMain(side, line, all, main, mainAlongX, ctx, minSegmentLength);
        return [location, overall];
    }

    private static DimensionChainSide Opposite(DimensionChainSide side) => side switch {
        DimensionChainSide.Top => DimensionChainSide.Bottom, DimensionChainSide.Bottom => DimensionChainSide.Top,
        DimensionChainSide.Left => DimensionChainSide.Right, _ => DimensionChainSide.Left };

    private sealed class Ctx(bool alongX, int outer, IReadOnlyCollection<int> mainIds, IReadOnlyList<DimensionPoint> line,
        IReadOnlyList<DimensionPoint> all, IReadOnlyList<DimensionPoint> main, IReadOnlyList<DimensionPoint> otherLine)
    {
        public bool AlongX { get; } = alongX;
        public int Outer { get; } = outer;
        public IReadOnlyCollection<int> MainIds { get; } = mainIds;
        public IReadOnlyList<DimensionPoint> Line { get; } = line;
        public double Along(DimensionPoint p) => AlongX ? p.X : p.Y;
        public double Across(DimensionPoint p) => AlongX ? p.Y : p.X;

        // A part is located from the side of the main part it reaches. A part that crosses the main
        // part's axis can be located only on sides whose preliminary chain offers its points.
        // The preliminary chains use the whole assembly's middle; they do not guarantee a final pick.
        public bool LiesOnThisSide(int partId)
        {
            var own = all.Where(p => Parts(p).Contains(partId)).Select(Across).ToArray();
            if (own.Length == 0 || main.Count == 0) return true;
            var mainMiddle = (main.Max(Across) + main.Min(Across)) / 2;
            var reachesHigh = own.Max() > mainMiddle + SameCoordinate;
            var reachesLow = own.Min() < mainMiddle - SameCoordinate;
            // A part on the axis itself (seen edge-on) reaches neither side and belongs to both.
            if (!reachesHigh && !reachesLow) return true;
            if (Outer > 0 ? reachesHigh : reachesLow) return true;
            return !otherLine.Any(p => Parts(p).Contains(partId));
        }

        public bool IsMain(DimensionPoint p) => p.Parents.Any(x => x.ModelId is { } id && MainIds.Contains(id));
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
        return Result(side, "overall", picks, ctx, [], null);
    }

    // Chain along the main part: the extremes, the main part's ends and each other part's first edge.
    private static object AlongMain(DimensionChainSide side, IReadOnlyList<DimensionPoint> line, Ctx ctx, double minLength)
    {
        var picks = new List<Pick>();
        if (line.Count == 0) return Empty(side, "location", "no points on this side");
        // Ends come from the half of the view on the dimension line's side, so a raked end does
        // not pull in its far corner and send a witness line across the profile.
        var outer = OuterHalf(line, ctx);
        picks.AddRange(Ends(outer, ctx, "extreme", 1));
        var main = outer.Where(ctx.IsMain).ToArray();
        if (main.Length > 0) picks.AddRange(Ends(main, ctx, "main-end", 2));

        var partIds = line.SelectMany(ctx.Parts).Where(id => !ctx.MainIds.Contains(id)).Distinct().ToArray();
        var offeredOnOtherSide = partIds.Where(id => !ctx.LiesOnThisSide(id)).ToArray();
        foreach (var id in partIds.Where(ctx.LiesOnThisSide))
        {
            var own = line.Where(p => ctx.Parts(p).Contains(id)).ToArray();
            var first = own.Min(ctx.Along);
            picks.Add(new Pick(Best(own.Where(p => Math.Abs(ctx.Along(p) - first) <= SameCoordinate), ctx), "part-edge", 1));
        }
        return Result(side, "location", picks, ctx, [], minLength, offeredOnOtherSide: offeredOnOtherSide);
    }

    // Chain across the main part: the parts that stick out past the main part's end on this
    // side, with the main part's own edges between and around them.
    private static object AcrossMain(DimensionChainSide side, IReadOnlyList<DimensionPoint> line,
        IReadOnlyList<DimensionPoint> all, IReadOnlyList<DimensionPoint> main, bool mainAlongX, Ctx ctx, double minLength)
    {
        double MainAxis(DimensionPoint p) => mainAlongX ? p.X : p.Y;
        var mainLow = main.Min(MainAxis);
        var mainHigh = main.Max(MainAxis);
        var lowEnd = side is DimensionChainSide.Left or DimensionChainSide.Bottom;

        bool Sticks(int id)
        {
            var own = all.Where(p => ctx.Parts(p).Contains(id)).ToArray();
            return own.Length > 0 && (lowEnd ? own.Min(MainAxis) < mainLow - SameCoordinate
                                              : own.Max(MainAxis) > mainHigh + SameCoordinate);
        }

        var partIds = line.SelectMany(ctx.Parts).Where(id => !ctx.MainIds.Contains(id)).Distinct().ToArray();
        var endParts = partIds.Where(Sticks).ToArray();
        var picks = new List<Pick>();
        foreach (var id in endParts)
        {
            var own = line.Where(p => ctx.Parts(p).Contains(id)).ToArray();
            foreach (var group in Cluster(own, ctx))
                picks.Add(new Pick(Best(group, ctx), "end-part-outer", 1));
        }
        var mainOnLine = line.Where(ctx.IsMain).ToArray();
        if (mainOnLine.Length > 0) picks.AddRange(Ends(mainOnLine, ctx, "main-edge", 2));

        var skipped = partIds.Except(endParts).ToArray();
        if (endParts.Length == 0)
            return Empty(side, "location", "no part sticks out past the main part's end on this side", skipped);

        return Result(side, "location", picks, ctx, skipped, minLength, 3,
            "the end part is flush with the main part; nothing to locate");
    }

    // Points on the dimension line's side of the middle of the side's cross-axis extent; the whole
    // set when none qualifies.
    private static IReadOnlyList<DimensionPoint> OuterHalf(IReadOnlyList<DimensionPoint> line, Ctx ctx)
    {
        var middle = (line.Max(ctx.Across) + line.Min(ctx.Across)) / 2;
        var outer = line.Where(p => ctx.Outer * (ctx.Across(p) - middle) >= -SameCoordinate).ToArray();
        return outer.Length > 0 ? outer : line;
    }

    private static IEnumerable<Pick> Ends(IEnumerable<DimensionPoint> points, Ctx ctx, string role, int priority)
    {
        var list = points.ToArray();
        var lo = list.Min(ctx.Along);
        var hi = list.Max(ctx.Along);
        yield return new Pick(Best(list.Where(p => Math.Abs(ctx.Along(p) - lo) <= SameCoordinate), ctx), role, priority);
        yield return new Pick(Best(list.Where(p => Math.Abs(ctx.Along(p) - hi) <= SameCoordinate), ctx), role, priority);
    }

    // The point farthest to the outside; a real part edge beats a bare group extent; then id.
    private static DimensionPoint Best(IEnumerable<DimensionPoint> points, Ctx ctx) => points
        .OrderByDescending(p => ctx.Outer * ctx.Across(p))
        .ThenByDescending(p => p.Parents.Any(x => x.ModelId.HasValue))
        .ThenBy(p => p.Id, StringComparer.Ordinal).First();

    private static IEnumerable<DimensionPoint[]> Cluster(IEnumerable<DimensionPoint> points, Ctx ctx)
    {
        var current = new List<DimensionPoint>();
        foreach (var p in points.OrderBy(ctx.Along))
        {
            if (current.Count > 0 && ctx.Along(p) - ctx.Along(current[current.Count - 1]) > SameCoordinate)
            {
                yield return current.ToArray();
                current = new List<DimensionPoint>();
            }
            current.Add(p);
        }
        if (current.Count > 0) yield return current.ToArray();
    }

    private static object Result(DimensionChainSide side, string kind, List<Pick> picks, Ctx ctx,
        IReadOnlyCollection<int> skippedParts, double? minLength, int minPoints = 0, string? tooFewNote = null,
        IReadOnlyCollection<int>? offeredOnOtherSide = null)
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

        var dropped = new List<DimensionPoint>();
        if (minLength is { } min)
            for (var i = chosen.Count - 2; i >= 1; i--)
                if (ctx.Along(chosen[i + 1].Point) - ctx.Along(chosen[i].Point) < min)
                {
                    dropped.Add(chosen[i].Point);
                    chosen.RemoveAt(i);
                }

        // A part whose only point was dropped for being too close is reported as skipped.
        // A part with a point on a chosen coordinate is located by it, even when another part's point was taken.
        var kept = chosen.SelectMany(x => ctx.Line.Where(p => Math.Abs(ctx.Along(p) - ctx.Along(x.Point)) <= SameCoordinate)
            .SelectMany(ctx.Parts)).ToHashSet();
        var skipped = skippedParts.Concat(dropped.SelectMany(ctx.Parts).Where(id => !kept.Contains(id)))
            .Distinct().ToArray();

        if (chosen.Count < minPoints)
        {
            // Say which cause it was: points dropped for being too close, or nothing to locate.
            var note = dropped.Count > 0
                ? $"fewer than {minPoints} points remain after dropping segments shorter than {minLength} view units"
                : tooFewNote ?? "too few points";
            return Empty(side, kind, note, skipped, dropped.Select(p => p.Id).ToArray());
        }

        var ordered = chosen.OrderBy(x => ctx.Along(x.Point)).ToArray();
        return new {
            side = side.ToString(), kind,
            pointIds = ordered.Select(x => x.Point.Id).ToArray(),
            segments = ordered.Zip(ordered.Skip(1), (a, b) => Math.Round(ctx.Along(b.Point) - ctx.Along(a.Point), 3)).ToArray(),
            points = ordered.Select(x => new {
                pointId = x.Point.Id, roles = x.Roles,
                partIds = ctx.Parts(x.Point).ToArray()
            }).ToArray(),
            skippedPartIds = skipped,
            offeredOnOtherSidePartIds = (offeredOnOtherSide ?? Array.Empty<int>()).ToArray(),
            droppedShortPointIds = dropped.Select(p => p.Id).ToArray(),
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
