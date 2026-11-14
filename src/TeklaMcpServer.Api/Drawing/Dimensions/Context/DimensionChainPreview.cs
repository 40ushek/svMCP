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

    /// <summary>How a reason left out an overall a location chain already gives.</summary>
    internal const string OverallSpanned = "the location chain already spans the overall";

    /// <summary>
    /// The default answer: side, kind, ids and segments, plus notes and lists only when they
    /// have content. Roles and part ids stay behind the detailed question.
    /// </summary>
    public static DimensionPreviewChain Short(StructuralChain chain, bool detailed = false, string? side = null)
    {
        var result = new Dictionary<string, object?>();
        if (side != null)
        {
            result["key"] = side + "-" + chain.Kind;
            result["direction"] = side switch {
                "Top" => "horizontal", "Bottom" => "horizontal-down",
                "Left" => "vertical-left", "Right" => "vertical",
                _ => throw new ArgumentException("Unknown preview side")
            };
            result["attributesFile"] = chain.Kind == "overall" ? "overall" : "standard";
        }
        foreach (var (name, value) in chain.Fields())
        {
            if (!detailed && name is "points" or "side") continue;
            if (value == null) continue;
            if (!detailed && value is Array { Length: 0 }
                && name is "skippedPartIds" or "droppedShortPointIds" or "offeredOnOtherSidePartIds"
                    or "unlocatedPartIds" or "locatedByProfilePartIds" or "mergedNearbyPartIds") continue;
            // Numbers go through decimal so the answer prints 1088.417, not its binary tail.
            result[name] = value is double[] numbers ? numbers.Select(number => (object)Math.Round((decimal)number, 3)).ToArray() : value;
        }
        if (!result.ContainsKey("incomplete"))
        {
            var incomplete = chain.PointIds.Length < 2;
            result["incomplete"] = incomplete;
            if (incomplete) result["incompleteReason"] = chain.Note ?? "fewer than two selected points";
        }
        return DimensionPreviewProjection.Structural(chain, result);
    }

    /// <summary>Both chains of a side, refused with a reason instead of built.</summary>
    public static StructuralChain[] Refused(DimensionChainSide side, string note) =>
        [Empty(side, "location", note), Empty(side, "overall", note)];

    public static StructuralChain[] Build(DimensionPointCatalog catalog, DimensionChainSide side,
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
            : Empty(side, "overall", "across the main part " + OverallSpanned);
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
                overall = Empty(side, "overall", OverallSpanned);
        }
        return [location, overall];
    }

    private static StructuralChain SteelLocation(DimensionChainSide side, DimensionRuleResult result)
    {
        var details = result.RequireDetails<SteelLocationDetails>();
        if (result.Note != null)
            return Empty(side, result.Kind, result.Note, details.SkippedPartIds, details.DroppedShortPointIds);
        return new StructuralChain(result.Kind, result.Points.Select(point => point.Id).ToArray(), result.Segments.ToArray()) {
            Side = side.ToString(),
            Points = result.Points.Select((point, index) => (object)new {
                pointId = point.Id, roles = details.Roles[index], partIds = details.PartIds[index]
            }).ToArray(),
            Details = [("skippedPartIds", details.SkippedPartIds), ("offeredOnOtherSidePartIds", details.OfferedOnOtherSidePartIds),
                ("droppedShortPointIds", details.DroppedShortPointIds)],
            ReportsNote = true
        };
    }

    private static StructuralChain Overall(DimensionChainSide side, IReadOnlyList<DimensionPoint> line, ChainAxis ctx)
    {
        if (line.Count < 2) return Empty(side, "overall", "fewer than two points on this side");
        var picks = new List<ChainPick> {
            new(ctx.Best(line.Where(p => Math.Abs(ctx.Along(p) - line.Min(ctx.Along)) <= SameCoordinate)), "extreme", 1),
            new(ctx.Best(line.Where(p => Math.Abs(ctx.Along(p) - line.Max(ctx.Along)) <= SameCoordinate)), "extreme", 1)
        };
        var ordered = ctx.Settle(picks).OrderBy(x => ctx.Along(x.Point)).ToArray();
        return new StructuralChain("overall", ordered.Select(x => x.Point.Id).ToArray(),
            ordered.Zip(ordered.Skip(1), (a, b) => Math.Round(ctx.Along(b.Point) - ctx.Along(a.Point), 3)).ToArray()) {
            Side = side.ToString(),
            Points = ordered.Select(x => (object)new {
                pointId = x.Point.Id, roles = x.Roles, partIds = ctx.Parts(x.Point).ToArray()
            }).ToArray(),
            Details = [("skippedPartIds", Array.Empty<int>()), ("offeredOnOtherSidePartIds", Array.Empty<int>()),
                ("droppedShortPointIds", Array.Empty<string>())],
            ReportsNote = true
        };
    }

    private static StructuralChain Empty(DimensionChainSide side, string kind, string note,
        IReadOnlyCollection<int>? skipped = null, IReadOnlyCollection<string>? dropped = null) =>
        StructuralChain.Empty(kind, note, side.ToString(),
            ("skippedPartIds", (skipped ?? Array.Empty<int>()).ToArray()),
            ("droppedShortPointIds", (dropped ?? Array.Empty<string>()).ToArray()));
}
