using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// Finds the chains of a view preview that repeat a chain on the same view or a dimension
/// already on the sheet. Read-only: it names repeats and why, it never drops a chain; whether a
/// repeat is placed is the caller's choice.
/// Rules (the standard run of the dimension skill):
/// - same view: Top repeats Bottom and Right repeats Left, same kind, equal segments;
/// - overall: an overall chain equal to a one-segment dimension of the same measure already on
///   another front, top, bottom or back view (see <see cref="Measure"/>);
/// - mirror: on a bottom or back view, any chain equal, in either order, to a dimension on the
///   top or front view. Sections and end views repeat nothing between views.
/// </summary>
public static class DimensionChainRepeats
{
    /// <summary>Two numbers are equal when they differ by no more than this.</summary>
    public const double Tolerance = 0.5;

    public sealed class Chain(string key, string side, string kind, double[] segments)
    {
        public string Key { get; } = key;
        /// <summary>The preview row: Top, Bottom, Left, Right, Inclined or Local (never Bolt).</summary>
        public string Side { get; } = side;
        public string Kind { get; } = kind;
        public double[] Segments { get; } = segments;
    }

    public sealed class Placed(int dimensionId, int viewId, string viewType, double[] segments, bool? horizontal = null)
    {
        public int DimensionId { get; } = dimensionId;
        public int ViewId { get; } = viewId;
        public string ViewType { get; } = viewType;
        public double[] Segments { get; } = segments;
        /// <summary>True when it measures along X, false along Y, null when inclined.</summary>
        public bool? Horizontal { get; } = horizontal;
    }

    public sealed class Repeat(string key, string reason, string? of = null, int? dimensionId = null, int? viewId = null)
    {
        public string Key { get; } = key;
        /// <summary>sameView, overallOnSheet or mirror.</summary>
        public string Reason { get; } = reason;
        /// <summary>The key kept on the same view (sameView only).</summary>
        public string? Of { get; } = of;
        /// <summary>The dimension on the sheet this chain repeats (overallOnSheet, mirror).</summary>
        public int? DimensionId { get; } = dimensionId;
        public int? ViewId { get; } = viewId;
    }

    public static IReadOnlyList<Repeat> Find(int viewId, string? viewType,
        IReadOnlyList<Chain> chains, IReadOnlyList<Placed> sheet) => Find(viewId, viewType, chains, () => sheet);

    /// <summary>The sheet is read only when a chain could repeat it: an overall chain of a front,
    /// top, bottom or back view not already repeated on its own view, or any chain on a bottom or
    /// back view. Sections and end views never read it.</summary>
    public static IReadOnlyList<Repeat> Find(int viewId, string? viewType,
        IReadOnlyList<Chain> chains, Func<IReadOnlyList<Placed>> readSheet)
    {
        var repeats = new List<Repeat>();
        var mirrorOf = MirrorOf(viewType);
        var needsSheet = mirrorOf != null || chains.Any(chain => OverallMeasure(viewType, chain) != null && SameView(chain, chains) == null);
        var others = needsSheet ? readSheet().Where(dimension => dimension.ViewId != viewId).ToArray() : [];
        foreach (var chain in chains)
        {
            if (SameView(chain, chains) is { } of)
                repeats.Add(new Repeat(chain.Key, "sameView", of.Key));
            else if (OverallMeasure(viewType, chain) is { } measure && others.FirstOrDefault(d =>
                         d.Segments.Length == 1 && d.Horizontal is { } horizontal &&
                         Measure(d.ViewType, horizontal) == measure && Same(d.Segments, chain.Segments)) is { } overall)
                repeats.Add(new Repeat(chain.Key, "overallOnSheet", dimensionId: overall.DimensionId, viewId: overall.ViewId));
            else if (mirrorOf != null && others.FirstOrDefault(d => d.ViewType == mirrorOf &&
                         (Same(d.Segments, chain.Segments) || Same(d.Segments, chain.Segments.Reverse().ToArray()))) is { } mirror)
                repeats.Add(new Repeat(chain.Key, "mirror", dimensionId: mirror.DimensionId, viewId: mirror.ViewId));
        }
        return repeats;
    }

    private static Chain? SameView(Chain chain, IReadOnlyList<Chain> chains)
    {
        var kept = chain.Side switch { "Top" => "Bottom", "Right" => "Left", _ => null };
        return kept == null ? null : chains.FirstOrDefault(other =>
            other.Side == kept && other.Kind == chain.Kind && Same(other.Segments, chain.Segments));
    }

    private static string? OverallMeasure(string? viewType, Chain chain) => chain.Kind != "overall" ? null : chain.Side switch {
        "Top" or "Bottom" => Measure(viewType, horizontal: true),
        "Left" or "Right" => Measure(viewType, horizontal: false),
        _ => null
    };

    /// <summary>
    /// What a dimension measures, to tell one overall from another with the same number. The
    /// horizontal axis is shared by the front, top, bottom and back views; the vertical one is the
    /// height on front and back and the width on top and bottom. Null on any other view: sections
    /// and end views repeat nothing between views.
    /// </summary>
    private static string? Measure(string? viewType, bool horizontal) => viewType switch {
        "FrontView" or "BackView" => horizontal ? "length" : "height",
        "TopView" or "BottomView" => horizontal ? "length" : "width",
        _ => null
    };

    private static string? MirrorOf(string? viewType) => viewType switch {
        "BottomView" => "TopView", "BackView" => "FrontView", _ => null
    };

    internal static bool Same(double[] a, double[] b) =>
        a.Length > 0 && a.Length == b.Length && a.Zip(b, (x, y) => Math.Abs(x - y) <= Tolerance).All(equal => equal);

    /// <summary>The complete chains of a preview answer; incomplete ones are never placed.</summary>
    public static IReadOnlyList<Chain> FromPreview(JsonElement answer)
    {
        if (!answer.TryGetProperty("chainPreview", out var rows) || rows.ValueKind != JsonValueKind.Array) return [];
        // Bolt chains are compared by segment lengths here, not by their source bolts: equal steps in
        // different groups prove no repeat, so the Bolt row takes no part.
        return rows.EnumerateArray().Where(row => row.GetProperty("side").GetString() != ViewDimensionContext.BoltSide)
            .SelectMany(row => row.GetProperty("chains").EnumerateArray()
            .Where(chain => !(chain.TryGetProperty("incomplete", out var incomplete) && incomplete.GetBoolean()))
            .Select(chain => new Chain(
                chain.GetProperty("key").GetString()!,
                row.GetProperty("side").GetString()!,
                chain.TryGetProperty("kind", out var kind) ? kind.GetString() ?? string.Empty : string.Empty,
                chain.GetProperty("segments").EnumerateArray().Select(value => value.GetDouble()).ToArray())))
            .ToArray();
    }

    /// <summary>Every dimension on the sheet, as segment lengths along its measuring axis.</summary>
    public static IReadOnlyList<Placed> FromSheet(GetDimensionsResult sheet) =>
        sheet.Groups.SelectMany(group => {
            var (ax, ay) = Axis(group);
            bool? horizontal = Math.Abs(ax) > AxisAligned ? true : Math.Abs(ay) > AxisAligned ? false : null;
            return group.Items.Select(item => new Placed(item.Id,
                item.ViewId ?? group.ViewId ?? 0, group.ViewType, Segments(ax, ay, item), horizontal));
        }).ToArray();

    /// <summary>A unit axis is horizontal or vertical when its leading component exceeds this.</summary>
    private const double AxisAligned = 0.999;

    /// <summary>The unit measuring axis: the group direction, which is the dimension line itself,
    /// whatever the type says; the type only when the direction was not read.</summary>
    private static (double X, double Y) Axis(DimensionGroupInfo group)
    {
        if (group.Direction is { } direction)
        {
            var length = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
            if (length > 1e-9) return (direction.X / length, direction.Y / length);
        }
        return group.DimensionType == "Vertical" ? (0d, 1d) : (1d, 0d);
    }

    private static double[] Segments(double ax, double ay, DimensionItemInfo item)
    {
        var along = item.PointList.Select(point => point.X * ax + point.Y * ay).OrderBy(value => value).ToArray();
        return along.Zip(along.Skip(1), (a, b) => Math.Round(b - a, 3)).Where(step => step > 1e-6).ToArray();
    }
}
