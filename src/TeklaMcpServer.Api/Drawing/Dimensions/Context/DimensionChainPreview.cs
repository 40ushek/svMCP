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
        var ctx = new Ctx(alongX, side is DimensionChainSide.Top or DimensionChainSide.Right ? 1 : -1, mainPartIds);

        // Across the main part the location chain already spans the whole width, so no overall.
        var overall = alongX == mainAlongX
            ? Overall(side, line, ctx)
            : Empty(side, "overall", "across the main part the location chain already spans the overall");
        var location = alongX == mainAlongX
            ? AlongMain(side, line, ctx, minSegmentLength)
            : AcrossMain(side, line, all, main, mainAlongX, ctx, minSegmentLength);
        return [location, overall];
    }

    private sealed class Ctx(bool alongX, int outer, IReadOnlyCollection<int> mainIds)
    {
        public bool AlongX { get; } = alongX;
        public int Outer { get; } = outer;
        public IReadOnlyCollection<int> MainIds { get; } = mainIds;
        public double Along(DimensionPoint p) => AlongX ? p.X : p.Y;
        public double Across(DimensionPoint p) => AlongX ? p.Y : p.X;
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
        picks.AddRange(Ends(line, ctx, "extreme", 1));
        var main = line.Where(ctx.IsMain).ToArray();
        if (main.Length > 0) picks.AddRange(Ends(main, ctx, "main-end", 2));

        var partIds = line.SelectMany(ctx.Parts).Where(id => !ctx.MainIds.Contains(id)).Distinct().ToArray();
        foreach (var id in partIds)
        {
            var own = line.Where(p => ctx.Parts(p).Contains(id)).ToArray();
            var first = own.Min(ctx.Along);
            picks.Add(new Pick(Best(own.Where(p => Math.Abs(ctx.Along(p) - first) <= SameCoordinate), ctx), "part-edge", 1));
        }
        return Result(side, "location", picks, ctx, [], minLength);
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
        IReadOnlyCollection<int> skippedParts, double? minLength, int minPoints = 0, string? tooFewNote = null)
    {
        // One point per coordinate: the strongest role wins, all roles are kept for the report.
        var chosen = new List<(DimensionPoint Point, List<string> Roles)>();
        foreach (var group in ClusterPicks(picks, ctx))
        {
            var winner = group.OrderByDescending(x => x.Priority)
                .ThenByDescending(x => ctx.Outer * ctx.Across(x.Point))
                .ThenBy(x => x.Point.Id, StringComparer.Ordinal).First();
            chosen.Add((winner.Point, group.Select(x => x.Role).Distinct().ToList()));
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
        var kept = chosen.SelectMany(x => ctx.Parts(x.Point)).ToHashSet();
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
            droppedShortPointIds = dropped.Select(p => p.Id).ToArray(),
            note = (string?)null
        };
    }

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
