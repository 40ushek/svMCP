using System.Text.Json;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>One projected chain per part and placement side, including both outer edges.</summary>
internal static class BoltPartChainPreview
{
    internal static object[] Build(JsonElement snapshot, IReadOnlyCollection<int> includedParts,
        IReadOnlyCollection<DimensionChainSide> sides,
        IReadOnlyDictionary<int, IReadOnlyList<OutlineTreeNodeResult>>? contours, double tolerance = DimensionCoordinateSettings.DefaultToleranceMm,
        BoltChainSettings? boltSettings = null, bool perGroup = false)
    {
        var settings = boltSettings ?? new BoltChainSettings();
        if (!snapshot.TryGetProperty("boltGroups", out var rows)) return Array.Empty<object>();
        var sources = new List<Source>();
        foreach (var row in rows.EnumerateArray())
        {
            var group = BoltDimensionChainPreview.ReadGroup(row);
            var related = new[] { group.PartToBeBoltedId, group.PartToBoltToId }
                .Where(id => id.HasValue).Select(id => id!.Value).Concat(group.OtherPartIds).Distinct()
                .Where(includedParts.Contains);
            var restriction = row.GetProperty("restriction").EnumerateArray().ToArray();
            var refusal = BoltDimensionChainPreview.PatternRefusal(group,
                BoltGroupViewClassifier.Classify(group.Axis, settings.ClassToleranceDegrees), tolerance);
            foreach (var partId in related)
                foreach (var position in group.Positions)
                {
                    var evidence = restriction.Where(r => r.GetProperty("index").GetInt32() == position.Index).ToArray();
                    var reason = refusal ?? (evidence.Length == 1 && evidence[0].GetProperty("centerState").GetString() == "Inside"
                        ? null : "center restriction is " + (evidence.Length == 1
                            ? evidence[0].GetProperty("centerState").GetString() : "missing or ambiguous"));
                    sources.Add(new Source(partId, group.ModelId, position.Index, position.Point, reason));
                }
        }
        // Per group: one chain per part, group and side (the Bolt row); otherwise one per part and side.
        return sources.GroupBy(source => (source.PartId, GroupId: perGroup ? source.GroupId : 0))
            .OrderBy(unit => unit.Key.PartId).ThenBy(unit => unit.Key.GroupId)
            .SelectMany(unit => sides.Distinct().Select(side => BuildChain(unit.Key.PartId, unit.ToArray(), side, contours, tolerance, perGroup,
                perGroup ? $"bolt-part-{unit.Key.PartId}-groups-{unit.Key.GroupId}-{side}" : $"bolt-part-{unit.Key.PartId}-{side}")))
            .ToArray();
    }

    private static object BuildChain(int partId, Source[] sources, DimensionChainSide side,
        IReadOnlyDictionary<int, IReadOnlyList<OutlineTreeNodeResult>>? contours, double tolerance, bool perGroup, string proposalId)
    {
        var axis = side.RunsAlongX() ? 0 : 1;
        var across = 1 - axis;
        var sign = side.OutwardSign();
        var blockedSources = sources.Where(source => source.Reason != null)
            .OrderBy(source => source.GroupId).ThenBy(source => source.Index)
            .Select(source => new { boltGroupId = source.GroupId, index = source.Index, reason = source.Reason }).ToArray();
        var reason = blockedSources.Length == 0 ? null : "one or more bolt sources are blocked; see blockedSources";
        var clusters = new List<List<Source>>();
        if (reason == null)
            foreach (var source in sources.OrderBy(source => source.Point[axis]).ThenBy(source => source.GroupId).ThenBy(source => source.Index))
            {
                if (clusters.Count == 0 || source.Point[axis] - clusters[clusters.Count - 1][0].Point[axis]
                    > tolerance) clusters.Add(new List<Source>());
                clusters[clusters.Count - 1].Add(source);
            }
        var representatives = clusters.Select(cluster => cluster.OrderByDescending(source => sign * source.Point[across])
            .ThenBy(source => source.GroupId).ThenBy(source => source.Index).First()).ToArray();
        double[]? min = null, max = null;
        double[]? minEnd = null, maxEnd = null;
        string? boltRefusal = null;
        var minContour = -1;
        var maxContour = -1;
        IReadOnlyList<OutlineTreeNodeResult>? outline = null;
        contours?.TryGetValue(partId, out outline);
        if (reason == null && representatives.Length > 0)
        {
            var first = representatives[0].Point;
            var last = representatives[representatives.Length - 1].Point;
            var minReason = BoltEdgeDistancePreview.FindEdge(outline, first, axis, -1, out min, out minContour, tolerance);
            var maxReason = BoltEdgeDistancePreview.FindEdge(outline, last, axis, 1, out max, out maxContour, tolerance);
            reason = minReason ?? maxReason;
            if (reason == null && minContour != maxContour) reason = "endpoints belong to different outer contours";
            if (perGroup) boltRefusal = BoltsOnOneContour(outline, sources.Select(source => source.Point));
            if (perGroup && boltRefusal == null)
            {
                // An edge met at the bolt's height counts only where the part's edge there runs square to
                // the chain; a slanted edge there gives no measure. Then the corner of the part's edge
                // facing the dimension line is the end, as a detailer measures it.
                minEnd = minReason == null && SquareEdge(outline, min!, axis, tolerance) ? min
                    : FacingCorner(outline, first, axis, sign, -1, tolerance);
                maxEnd = maxReason == null && SquareEdge(outline, max!, axis, tolerance) ? max
                    : FacingCorner(outline, last, axis, sign, 1, tolerance);
            }
        }
        var points = reason == null && min != null && max != null
            ? new[] { min }.Concat(representatives.Select(source => source.Point)).Concat(new[] { max }).ToArray()
            : Array.Empty<double[]>();
        var segments = points.Skip(1).Select((point, index) => point[axis] - points[index][axis]).ToArray();
        if (reason == null && (segments.Length == 0 || segments.Any(value => value <= tolerance)))
            reason = "edge endpoints do not bound all projected bolt coordinates";
        return new {
            proposalId, kind = "part", partId,
            axis = axis == 0 ? "X" : "Y", placementSide = side.ToString(),
            state = reason == null ? "Candidate" : "Blocked", reason,
            creationMode = "explicitSelectionOnly", blockedSources,
            points, segments,
            minEnd, maxEnd, boltRefusal,
            projectedPositions = clusters.Select((cluster, index) => new {
                point = representatives[index].Point,
                sources = cluster.Select(source => new { boltGroupId = source.GroupId, index = source.Index }).ToArray()
            }).ToArray(),
            contourSource = "captured full-solid projected outer contour; section clipping and visibility are unverified"
        };
    }

    // Every bolt must stand inside one and the same outer contour of the part, whatever ends are
    // found later: a bolt outside the part or on another contour gives no chain.
    private static string? BoltsOnOneContour(IReadOnlyList<OutlineTreeNodeResult>? contours, IEnumerable<double[]> bolts)
    {
        if (contours == null || contours.Count == 0) return "part has no captured projected contour";
        var outer = BoltEdgeDistancePreview.Flatten(contours).Where(node => !node.IsHole && node.Polygon.Count >= 3).ToArray();
        var owners = new HashSet<int>();
        foreach (var bolt in bolts)
        {
            var matches = outer.Select((node, index) => (node, index)).Where(item =>
                TeklaMcpServer.Api.Algorithms.Geometry.PolygonGeometry.ContainsPoint(item.node.Polygon, bolt[0], bolt[1])).ToArray();
            if (matches.Length != 1) return "bolt center is outside or belongs to ambiguous projected outer contours";
            owners.Add(matches[0].index);
        }
        return owners.Count > 1 ? "bolts belong to different outer contours" : null;
    }

    // Whether the contour runs square to the chain axis through this edge point.
    private static bool SquareEdge(IReadOnlyList<OutlineTreeNodeResult>? contours, double[] edge, int axis, double tolerance)
    {
        var across = 1 - axis;
        return contours != null && BoltEdgeDistancePreview.Flatten(contours).Where(node => !node.IsHole)
            .Any(node => Enumerable.Range(0, node.Polygon.Count).Any(i => {
                var a = node.Polygon[i];
                var b = node.Polygon[(i + 1) % node.Polygon.Count];
                return Math.Abs(a[axis] - edge[axis]) <= tolerance && Math.Abs(b[axis] - edge[axis]) <= tolerance
                    && edge[across] >= Math.Min(a[across], b[across]) - tolerance
                    && edge[across] <= Math.Max(a[across], b[across]) + tolerance;
            }));
    }

    // The end (min or max along the axis) of the part's outer edge facing the dimension line, when it
    // stands beyond the bolts on that side. Null when the outline is missing or ambiguous.
    private static double[]? FacingCorner(IReadOnlyList<OutlineTreeNodeResult>? contours, double[] bolt, int axis,
        int outward, int end, double tolerance)
    {
        if (contours == null) return null;
        var across = 1 - axis;
        var outer = BoltEdgeDistancePreview.Flatten(contours).Where(node => !node.IsHole && node.Polygon.Count >= 3)
            .Where(node => TeklaMcpServer.Api.Algorithms.Geometry.PolygonGeometry.ContainsPoint(node.Polygon, bolt[0], bolt[1]))
            .ToArray();
        if (outer.Length != 1) return null;
        var points = outer[0].Polygon.Where(point => point.Length >= 2).ToArray();
        var facing = points.Max(point => outward * point[across]);
        var edge = points.Where(point => facing - outward * point[across] <= tolerance).ToArray();
        var corner = end < 0 ? edge.OrderBy(point => point[axis]).First() : edge.OrderByDescending(point => point[axis]).First();
        if (end * (corner[axis] - bolt[axis]) <= tolerance) return null;
        var result = (double[])bolt.Clone();
        result[0] = corner[0];
        result[1] = corner[1];
        return result;
    }

    private sealed class Source
    {
        internal Source(int partId, int groupId, int index, double[] point, string? reason)
        { PartId = partId; GroupId = groupId; Index = index; Point = point; Reason = reason; }
        internal int PartId { get; }
        internal int GroupId { get; }
        internal int Index { get; }
        internal double[] Point { get; }
        internal string? Reason { get; }
    }
}
