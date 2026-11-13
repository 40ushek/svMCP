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
        Func<int, int, double[], bool> edgeConfirmed, BoltChainSettings settings, DimensionCoordinateSettings? coordinateSettings = null)
    {
        var tolerance = (coordinateSettings ?? new DimensionCoordinateSettings()).ToleranceMm;
        var preview = JsonSerializer.SerializeToElement(BoltDimensionChainPreview.Build(snapshot, includedPartIds,
            AllSides, contours, coordinateSettings, settings));
        var geometries = snapshot.TryGetProperty("boltGroups", out var rows)
            ? rows.EnumerateArray().Select(BoltDimensionChainPreview.ReadGroup)
                .GroupBy(group => group.ModelId).ToDictionary(group => group.Key, group => group.First())
            : new Dictionary<int, BoltGroupGeometry>();
        var groups = preview.GetProperty("groups").EnumerateArray()
            .Select(group => Read(group, mainPartIds, geometries)).ToArray();
        var (twins, units) = Units(groups, settings.GroupMerge, tolerance);
        var partChains = JsonSerializer.SerializeToElement(BoltPartChainPreview.Build(snapshot, includedPartIds,
            AllSides, contours, tolerance, settings, perGroup: true, units))
            .EnumerateArray().ToDictionary(chain => chain.GetProperty("proposalId").GetString()!, StringComparer.Ordinal);
        var result = new List<BoltChainCandidate>();
        foreach (var group in groups)
        {
            var (groupId, viewClass, dimensioned) = (group.Id, group.ViewClass, group.Dimensioned);
            if (group.Unbuilt)
            {
                result.Add(Refused(groupId, dimensioned.Cast<int?>().FirstOrDefault(), viewClass,
                    dimensioned.Length == 0 ? "no included part is bolted by this group" : group.Refusal!, Holes([groupId], geometries)));
                continue;
            }
            if (twins.TryGetValue(groupId, out var twin))
            {
                result.Add(Refused(groupId, dimensioned[0], viewClass,
                    $"same projected bolt positions on the same part as bolt group {twin}; dimensioned once", Holes([groupId], geometries)));
                continue;
            }
            // A group merged into another one's chain gives no edge chain of its own; its steps stay its own.
            var leader = BoltPartChainPreview.Leader(groupId, units);
            var merged = units.Where(unit => unit.Value == leader).Select(unit => unit.Key).DefaultIfEmpty(groupId).OrderBy(id => id).ToArray();
            foreach (var axis in Axes(viewClass, group.Geometry?.Axis ?? []))
            {
                var letter = axis == 0 ? "X" : "Y";
                var across = 1 - axis;
                var center = groups.Where(other => merged.Contains(other.Id)).SelectMany(other => other.Positions)
                    .Select(point => point[across]).DefaultIfEmpty(0).Average();
                // Whether the edges of each part chain on this axis are confirmed; the steps
                // of the group follow the chain of the first part.
                var confirmed = new Dictionary<int, bool>();
                foreach (var partId in leader == groupId ? dimensioned : Array.Empty<int>())
                {
                    var placement = Placement(contours, partId, axis, center);
                    var partKey = BoltPartChainPreview.Key(partId, leader, units, placement.Side);
                    if (!partChains.TryGetValue(partKey, out var part)) continue;
                    var (points, chainRefusal) = PartChainPoints(part, end => edgeConfirmed(partId, axis, end), axis, tolerance);
                    confirmed[partId] = points.Length >= 2;
                    result.Add(new BoltChainCandidate(partKey, BoltChainCandidate.PartKind, groupId, partId,
                        mainPartIds.Contains(partId), viewClass, axis, RulePoints(partKey, groupId, points),
                        points.Skip(1).Select((point, index) => point[axis] - points[index][axis]),
                        placement.Refusal ?? chainRefusal,
                        confirmed[partId], placement.Normal, points.Length > 0 ? placement.Offset(points[0]) : 0,
                        part.GetProperty("projectedPositions").Clone(), Holes(merged, geometries)));
                }
                // The steps of a group are the same whichever part it bolts: offered once, beside the first part.
                var first = Placement(contours, dimensioned[0], axis, center);
                foreach (var chain in group.Chains.Where(chain => chain.GetProperty("axis").GetString() == letter))
                {
                    var key = chain.GetProperty("proposalId").GetString()!;
                    var points = chain.GetProperty("points").EnumerateArray()
                        .Select(point => point.GetProperty("point").EnumerateArray().Select(value => value.GetDouble()).ToArray()).ToArray();
                    result.Add(new BoltChainCandidate(key, BoltChainCandidate.InternalKind, groupId, dimensioned[0],
                        mainPartIds.Contains(dimensioned[0]), viewClass, axis, RulePoints(key, groupId, points),
                        chain.GetProperty("segments").EnumerateArray().Select(value => value.GetDouble()),
                        first.Refusal ?? (chain.GetProperty("state").GetString() == "Candidate" ? null : chain.GetProperty("reason").GetString()),
                        confirmed.TryGetValue(dimensioned[0], out var firstConfirmed) && firstConfirmed, first.Normal, points.Length > 0 ? first.Offset(points[0]) : 0,
                        chain.GetProperty("points").EnumerateArray().Select(point => point.GetProperty("sourceIndices").Clone()).ToArray(),
                        Holes([groupId], geometries)));
                }
            }
        }
        return result;
    }

    // Edge - bolts - edge with each end kept on its own when the view confirms it. One end left
    // gives edge - bolts; none, a refusal. Blocked bolt sources and split contours refuse as before.
    private static (double[][] Points, string? Refusal) PartChainPoints(JsonElement part, Func<double[], bool> confirmed,
        int axis, double tolerance)
    {
        var reason = part.GetProperty("reason").GetString();
        if (part.GetProperty("blockedSources").GetArrayLength() > 0) return ([], reason);
        if (part.TryGetProperty("boltRefusal", out var bolt) && bolt.ValueKind == JsonValueKind.String)
            return ([], bolt.GetString());
        var bolts = part.GetProperty("projectedPositions").EnumerateArray()
            .Select(position => position.GetProperty("point").EnumerateArray().Select(value => value.GetDouble()).ToArray())
            .OrderBy(point => point[axis]).ToArray();
        double[]? End(string name) => part.TryGetProperty(name, out var end) && end.ValueKind == JsonValueKind.Array
            ? end.EnumerateArray().Select(value => value.GetDouble()).ToArray() is var point && confirmed(point) ? point : null
            : null;
        var min = End("minEnd");
        var max = End("maxEnd");
        if (bolts.Length == 0) return ([], reason ?? "no bolt position");
        if (min == null && max == null) return ([], "no part edge is confirmed on this view" + (reason != null ? "; " + reason : ""));
        var points = (min == null ? bolts : new[] { min }.Concat(bolts)).Concat(max == null ? [] : new[] { max }).ToArray();
        var bad = points.Skip(1).Select((point, index) => point[axis] - points[index][axis]).Any(value => value <= tolerance);
        return (points, bad ? "edge endpoints do not bound all projected bolt coordinates" : null);
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

    private static BoltChainCandidate Refused(int groupId, int? partId, BoltGroupViewClass viewClass, string reason, object holes) =>
        new($"bolt-{groupId}", BoltChainCandidate.GroupKind, groupId, partId, false, viewClass, 0, [], [], reason,
            false, new DimensionDirection(0, -1), 0, Array.Empty<object>(), holes);

    private sealed class GroupInfo(int id, BoltGroupViewClass viewClass, int[] dimensioned, JsonElement[] chains,
        string? refusal, double[][] positions, BoltGroupGeometry? geometry)
    {
        public int Id { get; } = id;
        public BoltGroupViewClass ViewClass { get; } = viewClass;
        public int[] Dimensioned { get; } = dimensioned;
        public JsonElement[] Chains { get; } = chains;
        public string? Refusal { get; } = refusal;
        public double[][] Positions { get; } = positions;
        public BoltGroupGeometry? Geometry { get; } = geometry;
        public bool Unbuilt => Dimensioned.Length == 0 || (Chains.Length == 0 && Refusal != null);
        /// <summary>Every center is admitted on this view; a blocked group neither merges nor makes a twin.</summary>
        public bool Admitted { get; set; }
    }

    private static GroupInfo Read(JsonElement group, IReadOnlyCollection<int> mainPartIds,
        IReadOnlyDictionary<int, BoltGroupGeometry> geometries)
    {
        var groupId = group.GetProperty("boltGroupId").GetInt32();
        var parts = group.GetProperty("partCandidates").EnumerateArray().Select(id => id.GetInt32()).ToArray();
        // A group joining the main part and secondary parts is dimensioned on every secondary part;
        // the main part takes it only when nothing else is bolted.
        var secondary = parts.Where(id => !mainPartIds.Contains(id)).OrderBy(id => id).ToArray();
        var points = group.GetProperty("points").EnumerateArray().ToArray();
        return new GroupInfo(groupId,
            (BoltGroupViewClass)Enum.Parse(typeof(BoltGroupViewClass), group.GetProperty("viewClass").GetString()!),
            secondary.Length > 0 ? secondary : parts.OrderBy(id => id).ToArray(),
            group.GetProperty("chains").EnumerateArray().ToArray(), group.GetProperty("reason").GetString(),
            Projected(group), geometries.TryGetValue(groupId, out var geometry) ? geometry : null) {
            Admitted = points.Length > 0 && points.All(point => point.GetProperty("state").GetString() != "Blocked")
        };
    }

    // Twins: two groups in the same holes of the same part (bolts and a hole group, say) give the
    // same chains, so the part is dimensioned once, by the first group. Units: alike groups of the
    // same part, class and measured axes share one edge chain; each group maps to the first of its unit.
    // Only groups whose centers are all admitted take part: a blocked one would refuse the chain it joins.
    private static (Dictionary<int, int> Twins, Dictionary<int, int> Units) Units(GroupInfo[] groups,
        BoltGroupMerge merge, double tolerance)
    {
        var twins = new Dictionary<int, int>();
        var units = new Dictionary<int, int>();
        var taken = new List<GroupInfo>();
        foreach (var group in groups.Where(group => !group.Unbuilt && group.Admitted))
        {
            // Same part, class and measured axes: a Side group is measured across its own bolt axis.
            bool Alike(GroupInfo other) => other.ViewClass == group.ViewClass && other.Dimensioned.SequenceEqual(group.Dimensioned)
                && Axes(other.ViewClass, other.Geometry?.Axis ?? []).SequenceEqual(Axes(group.ViewClass, group.Geometry?.Axis ?? []));
            if (taken.FirstOrDefault(other => Alike(other) && SamePositions(other.Positions, group.Positions, tolerance)) is { } twin)
            {
                twins[group.Id] = twin.Id;
                continue;
            }
            var key = MergeKey(merge, group.Geometry);
            var leader = key == null ? null : taken.FirstOrDefault(other => Alike(other)
                && Equals(MergeKey(merge, other.Geometry), key));
            units[group.Id] = leader == null ? group.Id : units[leader.Id];
            taken.Add(group);
        }
        return (twins, units);
    }

    // What makes two groups alike for one chain; null keeps the group apart.
    private static object? MergeKey(BoltGroupMerge merge, BoltGroupGeometry? group) => group == null ? null : merge switch {
        BoltGroupMerge.SameHole => (Math.Round(group.HoleDiameter, 3), group.HoleType,
            Math.Round(group.SlottedHoleX, 3), Math.Round(group.SlottedHoleY, 3)),
        BoltGroupMerge.SameBoltSize => Math.Round(group.BoltSize, 3),
        _ => null
    };

    // The bolt and hole of every group a chain measures.
    private static object[] Holes(IEnumerable<int> groupIds, IReadOnlyDictionary<int, BoltGroupGeometry> geometries) =>
        groupIds.Where(geometries.ContainsKey).Select(id => geometries[id]).Select(group => (object)new {
            boltGroupId = group.ModelId, boltStandard = group.BoltStandard, boltSize = group.BoltSize,
            holeDiameter = group.HoleDiameter, holeOnly = group.HoleOnly, holeType = group.HoleType,
            slottedHoleX = group.SlottedHoleX, slottedHoleY = group.SlottedHoleY
        }).ToArray();

    private static double[][] Projected(JsonElement group) => group.GetProperty("points").EnumerateArray()
        .Select(point => point.GetProperty("point")).Select(point => new[] { point[0].GetDouble(), point[1].GetDouble() }).ToArray();

    // Every center of each set has a center of the other within the tolerance.
    private static bool SamePositions(double[][] a, double[][] b, double tolerance)
    {
        bool Covered(double[][] from, double[][] by) => from.All(p => by.Any(q =>
            Math.Abs(p[0] - q[0]) <= tolerance && Math.Abs(p[1] - q[1]) <= tolerance));
        return a.Length > 0 && b.Length > 0 && Covered(a, b) && Covered(b, a);
    }

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
