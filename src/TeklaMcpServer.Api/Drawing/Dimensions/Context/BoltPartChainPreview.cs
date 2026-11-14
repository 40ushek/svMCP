using System.Text.Json;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>One projected chain per part and placement side, including both outer edges.</summary>
internal static class BoltPartChainPreview
{
    internal static object[] Build(JsonElement snapshot, IReadOnlyCollection<int> includedParts,
        IReadOnlyCollection<DimensionChainSide> sides,
        IReadOnlyDictionary<int, IReadOnlyList<OutlineTreeNodeResult>>? contours, double tolerance = DimensionCoordinateSettings.DefaultToleranceMm,
        BoltChainSettings? boltSettings = null, bool perGroup = false, IReadOnlyList<IReadOnlyDictionary<int, int>>? units = null)
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
            var viewClass = BoltGroupViewClassifier.Classify(group.Axis, settings.ClassToleranceDegrees);
            var refusal = BoltDimensionChainPreview.PatternRefusal(group, viewClass, tolerance);
            foreach (var partId in related)
                foreach (var position in group.Positions)
                {
                    var evidence = restriction.Where(r => r.GetProperty("index").GetInt32() == position.Index).ToArray();
                    var reason = refusal ?? (evidence.Length == 1 && evidence[0].GetProperty("centerState").GetString() == "Inside"
                        ? null : "center restriction is " + (evidence.Length == 1
                            ? evidence[0].GetProperty("centerState").GetString() : "missing or ambiguous"));
                    sources.Add(new Source(partId, group.ModelId, position.Index, position.Point, reason, viewClass == BoltGroupViewClass.Side));
                }
        }
        // Per group: one chain per part, group (or groups merged on that side's axis) and side (the
        // Bolt row); otherwise one per part and side. units[axis] maps a group to the first of its merge.
        return sides.Distinct().SelectMany(side => {
            var merge = units?[side.RunsAlongX() ? 0 : 1];
            return sources.GroupBy(source => (source.PartId, GroupId: perGroup ? Leader(source.GroupId, merge) : 0))
                .Select(unit => (unit.Key.PartId, unit.Key.GroupId, Chain: BuildChain(unit.Key.PartId, unit.ToArray(), side, contours, tolerance, perGroup,
                    perGroup ? Key(unit.Key.PartId, unit.Key.GroupId, merge, side) : $"bolt-part-{unit.Key.PartId}-{side}")));
        }).OrderBy(chain => chain.PartId).ThenBy(chain => chain.GroupId).Select(chain => chain.Chain).ToArray();
    }

    // The first group of the merged groups a group belongs to; itself when not merged.
    internal static int Leader(int groupId, IReadOnlyDictionary<int, int>? units) =>
        units != null && units.TryGetValue(groupId, out var leader) ? leader : groupId;

    // The key of a per-group chain names every group it measures: groups-21 or groups-21-22.
    internal static string Key(int partId, int leader, IReadOnlyDictionary<int, int>? units, DimensionChainSide side)
    {
        var groups = units == null ? new[] { leader }
            : units.Where(unit => unit.Value == leader).Select(unit => unit.Key).DefaultIfEmpty(leader).OrderBy(id => id).ToArray();
        return $"bolt-part-{partId}-groups-{string.Join("-", groups)}-{side}";
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
        string? minEndKind = null, maxEndKind = null;
        string? boltRefusal = null;
        var minContour = -1;
        var maxContour = -1;
        IReadOnlyList<OutlineTreeNodeResult>? outline = null;
        contours?.TryGetValue(partId, out outline);
        if (reason == null && representatives.Length > 0)
        {
            var first = representatives[0].Point;
            var last = representatives[representatives.Length - 1].Point;
            var minReason = BoltEdgeDistancePreview.FindEdge(outline, first, axis, -1, out min, out minContour, out var minSearch, tolerance);
            var maxReason = BoltEdgeDistancePreview.FindEdge(outline, last, axis, 1, out max, out maxContour, out var maxSearch, tolerance);
            reason = minReason ?? maxReason;
            if (reason == null && minContour != maxContour) reason = "endpoints belong to different outer contours";
            IReadOnlyList<double[]>? polygon = null;
            var onEdge = false;
            if (perGroup) boltRefusal = BoltsOnOneContour(outline, sources.Select(source => source.Point), tolerance, out polygon, out onEdge);
            if (perGroup && boltRefusal == null && onEdge)
            {
                boltRefusal = EdgeLineEnds(polygon!, sources, first, last, axis, tolerance, out minEnd, out maxEnd);
                minEndKind = maxEndKind = "Boundary";
            }
            else if (perGroup && boltRefusal == null)
            {
                (minEnd, minEndKind) = EdgeEnd(polygon!, minSearch, min, first, axis, sign, -1, tolerance);
                (maxEnd, maxEndKind) = EdgeEnd(polygon!, maxSearch, max, last, axis, sign, 1, tolerance);
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
            minEnd, maxEnd, minEndKind, maxEndKind, boltRefusal,
            projectedPositions = clusters.Select((cluster, index) => new {
                point = representatives[index].Point,
                sources = cluster.Select(source => new { boltGroupId = source.GroupId, index = source.Index }).ToArray()
            }).ToArray(),
            contourSource = "captured full-solid projected outer contour; section clipping and visibility are unverified"
        };
    }

    // Every bolt must stand in one and the same outer contour of the part, whatever ends are found
    // later: a bolt outside the part or on another contour gives no chain. A center within the
    // tolerance of the contour line is on its edge, whichever side of the line rounding put it.
    internal static string? BoltsOnOneContour(IReadOnlyList<OutlineTreeNodeResult>? contours, IEnumerable<double[]> bolts,
        double tolerance, out IReadOnlyList<double[]>? polygon, out bool onEdge)
    {
        polygon = null;
        onEdge = false;
        if (contours == null || contours.Count == 0) return "part has no captured projected contour";
        var outer = BoltEdgeDistancePreview.Flatten(contours).Where(node => !node.IsHole && node.Polygon.Count >= 3
            && node.Polygon.All(point => point.Length >= 2)).ToArray();
        var owners = new HashSet<int>();
        foreach (var bolt in bolts)
        {
            if (BoltEdgeDistancePreview.Owner(outer, bolt, tolerance) is not { } owner)
                return "bolt center is outside or belongs to ambiguous projected outer contours";
            owners.Add(owner.Index);
            polygon = owner.Polygon;
            onEdge |= owner.State == BoltCenterState.Boundary;
        }
        return owners.Count > 1 ? "bolts belong to different outer contours" : null;
    }

    // One end of an edge chain. A crossing found on an edge square to the chain gives that edge's
    // corner; a crossing on a slanted edge gives no measure there, so the corner of the edge facing
    // the dimension line is the end, as a detailer measures it. A search that ended ambiguous or on
    // invalid geometry gives no end: the facing corner must not stand in for an edge that was not found.
    private static (double[]? End, string Kind) EdgeEnd(IReadOnlyList<double[]> polygon, BoltEdgeSearch search,
        double[]? edge, double[] bolt, int axis, int outward, int end, double tolerance)
    {
        if (search != BoltEdgeSearch.Found || edge == null) return (null, search.ToString());
        return SquareEdgeCorner(polygon, edge, axis, outward, tolerance) is { } corner
            ? (corner, "Square")
            : (FacingCorner(polygon, bolt, axis, outward, end, tolerance), "Slanted");
    }

    // Bolt centers on the contour line. Only a group seen from the side may stand there: its centers
    // lie in the face of the part, which the view shows as an edge. All of them must lie on one edge
    // running along the chain; its two ends are the ends of the chain. In plan a center on the edge
    // is not a bolt in this part.
    private static string? EdgeLineEnds(IReadOnlyList<double[]> polygon, Source[] sources, double[] first, double[] last,
        int axis, double tolerance, out double[]? minEnd, out double[]? maxEnd)
    {
        minEnd = maxEnd = null;
        if (sources.Any(source => !source.Side)) return "bolt center lies on the projected part edge";
        var across = 1 - axis;
        // The edges of the contour running along the chain: its segments that stay within the
        // tolerance across it, with pieces that follow one another joined into one edge. A center is
        // on an edge by the same measure that put it on the boundary: its distance to a segment,
        // not to another center and not to a line drawn through the edge.
        var edges = new List<(List<(double[] A, double[] B)> Segments, double[] Lo, double[] Hi)>();
        foreach (var piece in Enumerable.Range(0, polygon.Count).Select(i => (A: polygon[i], B: polygon[(i + 1) % polygon.Count]))
            .Where(segment => Math.Abs(segment.A[across] - segment.B[across]) <= tolerance)
            .Select(segment => segment.A[axis] <= segment.B[axis] ? (Lo: segment.A, Hi: segment.B) : (Lo: segment.B, Hi: segment.A))
            .OrderBy(segment => segment.Lo[axis]))
        {
            var index = edges.FindIndex(edge => Math.Abs(edge.Hi[axis] - piece.Lo[axis]) <= tolerance
                && Math.Abs(edge.Hi[across] - piece.Lo[across]) <= tolerance);
            if (index < 0) edges.Add(([(piece.Lo, piece.Hi)], piece.Lo, piece.Hi));
            else
            {
                edges[index].Segments.Add((piece.Lo, piece.Hi));
                edges[index] = (edges[index].Segments, edges[index].Lo, piece.Hi);
            }
        }
        var owners = edges.Where(edge => sources.All(source => edge.Segments.Any(segment =>
            BoltEdgeDistancePreview.DistanceToSegment(source.Point, segment.A, segment.B) <= tolerance))).ToArray();
        if (owners.Length != 1) return "bolt centers on the part edge do not lie on one edge running along the chain";
        double[]? End(double[] bolt, double[] corner, int end)
        {
            if (end * (corner[axis] - bolt[axis]) <= tolerance) return null;
            var point = (double[])bolt.Clone();
            point[0] = corner[0];
            point[1] = corner[1];
            return point;
        }
        minEnd = End(first, owners[0].Lo, -1);
        maxEnd = End(last, owners[0].Hi, 1);
        return null;
    }

    // Where the contour runs square to the chain axis through this edge point: the corner of that
    // edge nearest the dimension line, so the witness line starts at the part's corner, not at the
    // bolt's height in the middle of the edge. Null when the edge there is not square.
    // Only the contour the bolts stand in is searched: an edge of a neighbouring contour lying within
    // the tolerance must not lend its corner.
    private static double[]? SquareEdgeCorner(IReadOnlyList<double[]> polygon, double[] edge, int axis, int outward, double tolerance)
    {
        var across = 1 - axis;
        var ends = Enumerable.Range(0, polygon.Count).Select(i => (A: polygon[i], B: polygon[(i + 1) % polygon.Count]))
            .Where(segment => Math.Abs(segment.A[axis] - edge[axis]) <= tolerance && Math.Abs(segment.B[axis] - edge[axis]) <= tolerance
                && edge[across] >= Math.Min(segment.A[across], segment.B[across]) - tolerance
                && edge[across] <= Math.Max(segment.A[across], segment.B[across]) + tolerance)
            .SelectMany(segment => new[] { segment.A[across], segment.B[across] }).ToArray();
        if (ends.Length == 0) return null;
        var corner = (double[])edge.Clone();
        corner[across] = outward > 0 ? ends.Max() : ends.Min();
        return corner;
    }

    // The end (min or max along the axis) of the part's outer edge facing the dimension line, when it
    // stands beyond the bolts on that side; null otherwise.
    private static double[]? FacingCorner(IReadOnlyList<double[]> polygon, double[] bolt, int axis,
        int outward, int end, double tolerance)
    {
        var across = 1 - axis;
        var facing = polygon.Max(point => outward * point[across]);
        var edge = polygon.Where(point => facing - outward * point[across] <= tolerance).ToArray();
        var corner = end < 0 ? edge.OrderBy(point => point[axis]).First() : edge.OrderByDescending(point => point[axis]).First();
        if (end * (corner[axis] - bolt[axis]) <= tolerance) return null;
        var result = (double[])bolt.Clone();
        result[0] = corner[0];
        result[1] = corner[1];
        return result;
    }

    private sealed class Source
    {
        internal Source(int partId, int groupId, int index, double[] point, string? reason, bool side)
        { PartId = partId; GroupId = groupId; Index = index; Point = point; Reason = reason; Side = side; }
        /// <summary>The group is seen from the side: its centers lie in the face of the part.</summary>
        internal bool Side { get; }
        internal int PartId { get; }
        internal int GroupId { get; }
        internal int Index { get; }
        internal double[] Point { get; }
        internal string? Reason { get; }
    }
}
