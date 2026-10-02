using System.Text.Json;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>One projected chain per part and placement side, including both outer edges.</summary>
internal static class BoltPartChainPreview
{
    internal static object[] Build(JsonElement snapshot, IReadOnlyCollection<int> includedParts,
        IReadOnlyCollection<DimensionChainSide> sides,
        IReadOnlyDictionary<int, IReadOnlyList<OutlineTreeNodeResult>>? contours)
    {
        if (!snapshot.TryGetProperty("boltGroups", out var rows)) return Array.Empty<object>();
        var sources = new List<Source>();
        foreach (var row in rows.EnumerateArray())
        {
            var group = JsonSerializer.Deserialize<BoltGroupGeometry>(row.GetProperty("geometry").GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            var related = new[] { group.PartToBeBoltedId, group.PartToBoltToId }
                .Where(id => id.HasValue).Select(id => id!.Value).Concat(group.OtherPartIds).Distinct()
                .Where(includedParts.Contains);
            var restriction = row.GetProperty("restriction").EnumerateArray().ToArray();
            var refusal = BoltDimensionChainPreview.PatternRefusal(group);
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
        return sources.GroupBy(source => source.PartId).OrderBy(part => part.Key)
            .SelectMany(part => sides.Distinct().Select(side => BuildChain(part.Key, part.ToArray(), side, contours))).ToArray();
    }

    private static object BuildChain(int partId, Source[] sources, DimensionChainSide side,
        IReadOnlyDictionary<int, IReadOnlyList<OutlineTreeNodeResult>>? contours)
    {
        var axis = side is DimensionChainSide.Top or DimensionChainSide.Bottom ? 0 : 1;
        var across = 1 - axis;
        var sign = side is DimensionChainSide.Top or DimensionChainSide.Right ? 1 : -1;
        var blockedSources = sources.Where(source => source.Reason != null)
            .OrderBy(source => source.GroupId).ThenBy(source => source.Index)
            .Select(source => new { boltGroupId = source.GroupId, index = source.Index, reason = source.Reason }).ToArray();
        var reason = blockedSources.Length == 0 ? null : "one or more bolt sources are blocked; see blockedSources";
        var clusters = new List<List<Source>>();
        if (reason == null)
            foreach (var source in sources.OrderBy(source => source.Point[axis]).ThenBy(source => source.GroupId).ThenBy(source => source.Index))
            {
                if (clusters.Count == 0 || source.Point[axis] - clusters[clusters.Count - 1][0].Point[axis]
                    > BoltDimensionChainPreview.CoordinateTolerance) clusters.Add(new List<Source>());
                clusters[clusters.Count - 1].Add(source);
            }
        var representatives = clusters.Select(cluster => cluster.OrderByDescending(source => sign * source.Point[across])
            .ThenBy(source => source.GroupId).ThenBy(source => source.Index).First()).ToArray();
        double[]? min = null, max = null;
        var minContour = -1;
        var maxContour = -1;
        IReadOnlyList<OutlineTreeNodeResult>? outline = null;
        contours?.TryGetValue(partId, out outline);
        if (reason == null && representatives.Length > 0)
        {
            reason = BoltEdgeDistancePreview.FindEdge(outline, representatives[0].Point, axis, -1, out min, out minContour);
            if (reason == null)
                reason = BoltEdgeDistancePreview.FindEdge(outline, representatives[representatives.Length - 1].Point,
                    axis, 1, out max, out maxContour);
            if (reason == null && minContour != maxContour) reason = "endpoints belong to different outer contours";
        }
        var points = reason == null && min != null && max != null
            ? new[] { min }.Concat(representatives.Select(source => source.Point)).Concat(new[] { max }).ToArray()
            : Array.Empty<double[]>();
        var segments = points.Skip(1).Select((point, index) => point[axis] - points[index][axis]).ToArray();
        if (reason == null && (segments.Length == 0 || segments.Any(value => value <= BoltDimensionChainPreview.CoordinateTolerance)))
            reason = "edge endpoints do not bound all projected bolt coordinates";
        return new {
            proposalId = $"bolt-part-{partId}-{side}", kind = "part", partId,
            axis = axis == 0 ? "X" : "Y", placementSide = side.ToString(),
            state = reason == null ? "Candidate" : "Blocked", reason,
            creationMode = "explicitSelectionOnly", blockedSources,
            points, segments,
            projectedPositions = clusters.Select((cluster, index) => new {
                point = representatives[index].Point,
                sources = cluster.Select(source => new { boltGroupId = source.GroupId, index = source.Index }).ToArray()
            }).ToArray(),
            contourSource = "captured full-solid projected outer contour; section clipping and visibility are unverified"
        };
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
