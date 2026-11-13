using System.Text.Json;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// Turns the frozen bolt previews into chains for <see cref="BoltChainRule"/>: per group, its
/// part, the axes its class allows, the placement side next to the nearer part bound, and the
/// offset of the dimension line. Nothing is decided here.
/// </summary>
internal static class BoltChainCandidates
{
    private static readonly DimensionChainSide[] AllSides =
        [DimensionChainSide.Top, DimensionChainSide.Bottom, DimensionChainSide.Left, DimensionChainSide.Right];

    internal static IReadOnlyList<BoltChainCandidate> Build(JsonElement snapshot, IReadOnlyCollection<int> includedPartIds,
        IReadOnlyCollection<int> mainPartIds, IReadOnlyDictionary<int, IReadOnlyList<OutlineTreeNodeResult>>? contours,
        Func<int, bool> edgesConfirmed, BoltChainSettings settings, DimensionCoordinateSettings? coordinateSettings = null)
    {
        var tolerance = (coordinateSettings ?? new DimensionCoordinateSettings()).ToleranceMm;
        var preview = JsonSerializer.SerializeToElement(BoltDimensionChainPreview.Build(snapshot, includedPartIds,
            AllSides, contours, coordinateSettings, settings));
        var partChains = JsonSerializer.SerializeToElement(BoltPartChainPreview.Build(snapshot, includedPartIds,
            AllSides, contours, tolerance, settings, perGroup: true))
            .EnumerateArray().ToDictionary(chain => chain.GetProperty("proposalId").GetString()!, StringComparer.Ordinal);
        var axes = snapshot.TryGetProperty("boltGroups", out var rows)
            ? rows.EnumerateArray().Select(BoltDimensionChainPreview.ReadGroup)
                .GroupBy(group => group.ModelId).ToDictionary(group => group.Key, group => group.First().Axis)
            : new Dictionary<int, double[]>();
        var result = new List<BoltChainCandidate>();
        foreach (var group in preview.GetProperty("groups").EnumerateArray())
        {
            var groupId = group.GetProperty("boltGroupId").GetInt32();
            var viewClass = (BoltGroupViewClass)Enum.Parse(typeof(BoltGroupViewClass), group.GetProperty("viewClass").GetString()!);
            var parts = group.GetProperty("partCandidates").EnumerateArray().Select(id => id.GetInt32()).ToArray();
            // A group joining the main part and secondary parts is dimensioned on every secondary part;
            // the main part takes it only when nothing else is bolted.
            var secondary = parts.Where(id => !mainPartIds.Contains(id)).OrderBy(id => id).ToArray();
            var dimensioned = secondary.Length > 0 ? secondary : parts.OrderBy(id => id).ToArray();
            var chains = group.GetProperty("chains").EnumerateArray().ToArray();
            var refusal = group.GetProperty("reason").GetString();
            if (dimensioned.Length == 0 || (chains.Length == 0 && refusal != null))
            {
                result.Add(Refused(groupId, dimensioned.Cast<int?>().FirstOrDefault(), viewClass,
                    dimensioned.Length == 0 ? "no included part is bolted by this group" : refusal!));
                continue;
            }
            foreach (var axis in Axes(viewClass, axes.TryGetValue(groupId, out var a) ? a : []))
            {
                var letter = axis == 0 ? "X" : "Y";
                var across = 1 - axis;
                var center = group.GetProperty("points").EnumerateArray()
                    .Select(point => point.GetProperty("point")[across].GetDouble()).DefaultIfEmpty(0).Average();
                foreach (var partId in dimensioned)
                {
                    var placement = Placement(contours, partId, axis, center);
                    var partKey = $"bolt-part-{partId}-groups-{groupId}-{placement.Side}";
                    if (!partChains.TryGetValue(partKey, out var part)) continue;
                    var points = part.GetProperty("points").EnumerateArray()
                        .Select(point => point.EnumerateArray().Select(value => value.GetDouble()).ToArray()).ToArray();
                    result.Add(new BoltChainCandidate(partKey, BoltChainCandidate.PartKind, groupId, partId,
                        mainPartIds.Contains(partId), viewClass, axis, RulePoints(partKey, groupId, points),
                        part.GetProperty("segments").EnumerateArray().Select(value => value.GetDouble()),
                        placement.Refusal ?? (part.GetProperty("state").GetString() == "Candidate" ? null : part.GetProperty("reason").GetString()),
                        edgesConfirmed(partId), placement.Normal, points.Length > 0 ? placement.Offset(points[0]) : 0,
                        part.GetProperty("projectedPositions").Clone()));
                }
                // The steps of a group are the same whichever part it bolts: offered once, beside the first part.
                var first = Placement(contours, dimensioned[0], axis, center);
                foreach (var chain in chains.Where(chain => chain.GetProperty("axis").GetString() == letter))
                {
                    var key = chain.GetProperty("proposalId").GetString()!;
                    var points = chain.GetProperty("points").EnumerateArray()
                        .Select(point => point.GetProperty("point").EnumerateArray().Select(value => value.GetDouble()).ToArray()).ToArray();
                    result.Add(new BoltChainCandidate(key, BoltChainCandidate.InternalKind, groupId, dimensioned[0],
                        mainPartIds.Contains(dimensioned[0]), viewClass, axis, RulePoints(key, groupId, points),
                        chain.GetProperty("segments").EnumerateArray().Select(value => value.GetDouble()),
                        first.Refusal ?? (chain.GetProperty("state").GetString() == "Candidate" ? null : chain.GetProperty("reason").GetString()),
                        edgesConfirmed(dimensioned[0]), first.Normal, points.Length > 0 ? first.Offset(points[0]) : 0,
                        chain.GetProperty("points").EnumerateArray().Select(point => point.GetProperty("sourceIndices").Clone()).ToArray()));
                }
            }
        }
        return result;
    }

    private sealed class LinePlacement(DimensionChainSide side, DimensionDirection normal, string? refusal,
        Func<double[], double> offset)
    {
        public DimensionChainSide Side { get; } = side;
        public DimensionDirection Normal { get; } = normal;
        public string? Refusal { get; } = refusal;
        public Func<double[], double> Offset { get; } = offset;
    }

    // The line stands beside the nearer bound of the part, outside it.
    private static LinePlacement Placement(IReadOnlyDictionary<int, IReadOnlyList<OutlineTreeNodeResult>>? contours,
        int partId, int axis, double center)
    {
        var across = 1 - axis;
        var bounds = Bounds(contours, partId);
        var sign = bounds is { } b && b.Max[across] - center > center - b.Min[across] ? -1 : 1;
        var side = axis == 0 ? (sign > 0 ? DimensionChainSide.Top : DimensionChainSide.Bottom)
            : (sign > 0 ? DimensionChainSide.Right : DimensionChainSide.Left);
        return new LinePlacement(side, new DimensionDirection(axis == 0 ? 0 : sign, axis == 0 ? sign : 0),
            bounds == null ? "part has no captured contour to place the line beside" : null,
            first => bounds is { } box
                ? Math.Max(0, sign > 0 ? box.Max[across] - first[across] : first[across] - box.Min[across]) : 0);
    }

    // A Plan group is measured along both view axes; a Side group only across its bolt axis:
    // along the bolt axis the view shows the shanks, not a spacing.
    private static IEnumerable<int> Axes(BoltGroupViewClass viewClass, double[] boltAxis) => viewClass switch {
        BoltGroupViewClass.Plan => [0, 1],
        BoltGroupViewClass.Side when boltAxis.Length == 3 => [Math.Abs(boltAxis[0]) >= Math.Abs(boltAxis[1]) ? 1 : 0],
        _ => []
    };

    private static BoltChainCandidate Refused(int groupId, int? partId, BoltGroupViewClass viewClass, string reason) =>
        new($"bolt-{groupId}", BoltChainCandidate.GroupKind, groupId, partId, false, viewClass, 0, [], [], reason,
            false, new DimensionDirection(0, -1), 0, Array.Empty<object>());

    private static IEnumerable<DimensionRulePoint> RulePoints(string key, int groupId, double[][] points) =>
        points.Select((point, index) => new DimensionRulePoint($"{key}-{index}", point[0], point[1],
            [new DimensionPointSource("bolt-group", groupId, key, index)]));

    private static (double[] Min, double[] Max)? Bounds(IReadOnlyDictionary<int, IReadOnlyList<OutlineTreeNodeResult>>? contours, int partId)
    {
        if (contours == null || !contours.TryGetValue(partId, out var nodes)) return null;
        var points = Flatten(nodes).Where(node => !node.IsHole).SelectMany(node => node.Polygon)
            .Where(point => point.Length >= 2).ToArray();
        if (points.Length == 0) return null;
        return ([points.Min(p => p[0]), points.Min(p => p[1])], [points.Max(p => p[0]), points.Max(p => p[1])]);
    }

    private static IEnumerable<OutlineTreeNodeResult> Flatten(IEnumerable<OutlineTreeNodeResult> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Children)) yield return child;
        }
    }
}
