using System.Text.Json;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// Turns the frozen bolt previews into chains for <see cref="BoltChainRule"/>: per group, its
/// part, the axes its class allows, the placement side (next to the nearer part bound, or the other
/// one when the line stands nearer the bolts there, past the parts in the way on each), and the
/// offset of the dimension line. Nothing is decided here.
/// </summary>
internal static class BoltChainCandidates
{
    private static readonly DimensionChainSide[] AllSides =
        [DimensionChainSide.Top, DimensionChainSide.Bottom, DimensionChainSide.Left, DimensionChainSide.Right];

    internal static IReadOnlyList<BoltChainCandidate> Build(JsonElement snapshot, IReadOnlyCollection<int> includedPartIds,
        IReadOnlyCollection<int> mainPartIds, IReadOnlyDictionary<int, IReadOnlyList<OutlineTreeNodeResult>>? contours,
        Func<int, int, double[], bool> edgeConfirmed, BoltChainSettings settings, DimensionCoordinateSettings? coordinateSettings = null,
        double lineGap = 0)
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
        var gap = Math.Max(lineGap, tolerance);
        // The side of a part chain, decided once per merged chain, axis and part, so every group of
        // the merged chain (its steps included) stands on the same side whatever the group order.
        var sideOf = new Dictionary<(int Leader, int Axis, int PartId), (LinePlacement Placement, string Key, (double[][] Points, string? Refusal) Chain)?>();
        (LinePlacement Placement, string Key, (double[][] Points, string? Refusal) Chain)? Side(
            int partId, int axis, int leader, IReadOnlyDictionary<int, int> axisUnits, double center)
        {
            if (sideOf.TryGetValue((leader, axis, partId), out var known)) return known;
            var across = 1 - axis;
            // The side beside the nearer bound of the part, unless the line stands nearer its
            // points on the other one: a plate at the left end of a beam has its right bound
            // nearer its bolts, but the whole beam stands beyond it.
            var near = Placement(contours, partId, axis, center, gap, tolerance);
            var sides = new[] { near, Placement(contours, partId, axis, center, gap, tolerance, -near.Sign) }
                .Select(placement => (Placement: placement, Key: BoltPartChainPreview.Key(partId, leader, axisUnits, placement.Side)))
                .Where(side => partChains.ContainsKey(side.Key))
                .Select(side => (side.Placement, side.Key, Chain: PartChainPoints(partChains[side.Key],
                    end => edgeConfirmed(partId, axis, end), axis, tolerance)))
                .ToArray();
            // How far the line stands from the bolts; a side giving no chain is not compared.
            double Reach((LinePlacement Placement, string Key, (double[][] Points, string? Refusal) Chain) side) =>
                side.Placement.Refusal == null && side.Chain.Refusal == null && side.Chain.Points.Length > 0
                    ? Math.Abs(side.Chain.Points[0][across] + side.Placement.Sign * side.Placement.Offset(side.Chain.Points) - center)
                    : double.MaxValue;
            (LinePlacement, string, (double[][], string?))? best = sides.Length == 0 ? null
                : sides.Length == 2 && Reach(sides[0]) < double.MaxValue && Reach(sides[1]) < Reach(sides[0]) - tolerance
                    ? sides[1] : sides[0];
            sideOf[(leader, axis, partId)] = best;
            return best;
        }
        foreach (var group in groups)
        {
            var (groupId, viewClass, dimensioned) = (group.Id, group.ViewClass, group.Dimensioned);
            if (group.Unbuilt)
            {
                result.Add(Refused(groupId, dimensioned.Cast<int?>().FirstOrDefault(), viewClass,
                    dimensioned.Length == 0 ? "no included part is bolted by this group" : group.Refusal!, Holes([groupId], geometries, dimensioned.FirstOrDefault())));
                continue;
            }
            if (twins.TryGetValue(groupId, out var twin))
            {
                result.Add(Refused(groupId, dimensioned[0], viewClass,
                    $"same projected bolt positions on the same part as bolt group {twin}; dimensioned once", Holes([groupId], geometries, dimensioned[0])));
                continue;
            }
            foreach (var axis in Axes(viewClass, group.Geometry?.Axis ?? []))
            {
                // A group merged into another one's chain on this axis gives no edge chain of its own
                // there; its steps stay its own.
                var axisUnits = units[axis];
                var leader = BoltPartChainPreview.Leader(groupId, axisUnits);
                var merged = axisUnits.Where(unit => unit.Value == leader).Select(unit => unit.Key).DefaultIfEmpty(groupId).OrderBy(id => id).ToArray();
                var letter = axis == 0 ? "X" : "Y";
                var across = 1 - axis;
                var center = groups.Where(other => merged.Contains(other.Id)).SelectMany(other => other.Positions)
                    .Select(point => point[across]).DefaultIfEmpty(0).Average();
                // Whether the edges of each part chain on this axis are confirmed; the steps
                // of the group follow the chain of the first part.
                var confirmed = new Dictionary<int, bool>();
                foreach (var partId in leader == groupId ? dimensioned : Array.Empty<int>())
                {
                    if (Side(partId, axis, leader, axisUnits, center) is not { } best) continue;
                    var (placement, partKey, (points, chainRefusal)) = best;
                    var part = partChains[partKey];
                    confirmed[partId] = points.Length >= 2;
                    result.Add(new BoltChainCandidate(partKey, BoltChainCandidate.PartKind, groupId, partId,
                        mainPartIds.Contains(partId), viewClass, axis, RulePoints(partKey, groupId, points),
                        points.Skip(1).Select((point, index) => point[axis] - points[index][axis]),
                        placement.Refusal ?? chainRefusal,
                        confirmed[partId], placement.Normal, points.Length > 0 ? placement.Offset(points) : 0,
                        part.GetProperty("projectedPositions").Clone(), Holes(merged, geometries, partId)));
                }
                // The steps of a group are the same whichever part it bolts: offered once, beside the first part.
                // A group merged into another one's chain takes the side chosen for that chain.
                var first = Side(dimensioned[0], axis, leader, axisUnits, center)?.Placement
                    ?? Placement(contours, dimensioned[0], axis, center, gap, tolerance);
                foreach (var chain in group.Chains.Where(chain => chain.GetProperty("axis").GetString() == letter))
                {
                    var key = chain.GetProperty("proposalId").GetString()!;
                    var points = chain.GetProperty("points").EnumerateArray()
                        .Select(point => point.GetProperty("point").EnumerateArray().Select(value => value.GetDouble()).ToArray()).ToArray();
                    result.Add(new BoltChainCandidate(key, BoltChainCandidate.InternalKind, groupId, dimensioned[0],
                        mainPartIds.Contains(dimensioned[0]), viewClass, axis, RulePoints(key, groupId, points),
                        chain.GetProperty("segments").EnumerateArray().Select(value => value.GetDouble()),
                        first.Refusal ?? (chain.GetProperty("state").GetString() == "Candidate" ? null : chain.GetProperty("reason").GetString()),
                        confirmed.TryGetValue(dimensioned[0], out var firstConfirmed) && firstConfirmed, first.Normal, points.Length > 0 ? first.Offset(points) : 0,
                        chain.GetProperty("points").EnumerateArray().Select(point => point.GetProperty("sourceIndices").Clone()).ToArray(),
                        Holes([groupId], geometries, dimensioned[0])));
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

    private sealed class LinePlacement(DimensionChainSide side, int sign, DimensionDirection normal, string? refusal,
        Func<double[][], double> offset)
    {
        public DimensionChainSide Side { get; } = side;
        /// <summary>Outward along the axis across the chain: +1 toward Top or Right.</summary>
        public int Sign { get; } = sign;
        public DimensionDirection Normal { get; } = normal;
        public string? Refusal { get; } = refusal;
        /// <summary>From the chain's first point to the bound the line stands one gap beyond.</summary>
        public Func<double[][], double> Offset { get; } = offset;
    }

    // The line stands beside the nearer bound of the part, outside it, and beyond every other part
    // it would otherwise cross or come within one gap of along the chain's span (a part only touching
    // an end of the span is not crossed): an end plate
    // standing past the end of a beam pushes the line of the beam's bolts out with it.
    private static LinePlacement Placement(IReadOnlyDictionary<int, IReadOnlyList<OutlineTreeNodeResult>>? contours,
        int partId, int axis, double center, double gap, double touch, int? outward = null)
    {
        var across = 1 - axis;
        var bounds = Bounds(contours, partId);
        var sign = outward ?? (bounds is { } b && b.Max[across] - center > center - b.Min[across] ? -1 : 1);
        var side = axis == 0 ? (sign > 0 ? DimensionChainSide.Top : DimensionChainSide.Bottom)
            : (sign > 0 ? DimensionChainSide.Right : DimensionChainSide.Left);
        var others = contours == null ? [] : contours.Keys.Where(id => id != partId)
            .Select(id => Bounds(contours, id)).Where(box => box != null).Select(box => box!.Value).ToArray();
        double Clear(double[][] points)
        {
            var box = bounds!.Value;
            var (from, to) = (points.Min(point => point[axis]), points.Max(point => point[axis]));
            var bound = sign > 0 ? box.Max[across] : box.Min[across];
            // Under the chain or beside it is judged against the part's own bound, not the moved one.
            var own = sign * bound;
            // Outward: the near side of a part in the way stands less than one gap past the bound,
            // and its far side past it; the bound moves to that far side until nothing is in the way.
            for (var moved = true; moved;)
            {
                moved = false;
                foreach (var other in others.Where(other => other.Min[axis] < to - touch && other.Max[axis] > from + touch))
                {
                    var (near, far) = sign > 0 ? (other.Min[across], other.Max[across]) : (-other.Max[across], -other.Min[across]);
                    // A part starting on the line itself is in the way too: the line would lie on its edge.
                    // A part reaching back inside the bound lies under the chain (the beam under a fin
                    // plate), not beside it: the line cannot clear it and must not try.
                    if (sign * bound >= far || near > sign * bound + gap + touch || near < own - touch) continue;
                    bound = sign * far;
                    moved = true;
                }
            }
            return Math.Max(0, sign * (bound - points[0][across]));
        }
        return new LinePlacement(side, sign, new DimensionDirection(axis == 0 ? 0 : sign, axis == 0 ? sign : 0),
            bounds == null ? "part has no captured contour to place the line beside" : null,
            points => bounds == null ? 0 : Clear(points));
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
    // Merging is decided per chain axis: groups share a chain along an axis when they stand one beside
    // the other along it, or when their bolts stand at the same coordinates on it. Groups that overlap
    // along the chain with different spacings keep their own chains: one chain would mix both spacings.
    private static (Dictionary<int, int> Twins, IReadOnlyDictionary<int, int>[] Units) Units(GroupInfo[] groups,
        BoltGroupMerge merge, double tolerance)
    {
        var twins = new Dictionary<int, int>();
        var units = new[] { new Dictionary<int, int>(), new Dictionary<int, int>() };
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
            var key = MergeKey(merge, group.Geometry, group.Dimensioned);
            for (var axis = 0; axis < 2; axis++)
            {
                var axisUnits = units[axis];
                var leader = key == null ? null : taken.FirstOrDefault(other => Alike(other)
                    && MergeKey(merge, other.Geometry, other.Dimensioned) == key
                    && taken.Where(member => axisUnits[member.Id] == axisUnits[other.Id])
                        .All(member => Fit(member.Positions, group.Positions, axis, tolerance)));
                axisUnits[group.Id] = leader == null ? group.Id : axisUnits[leader.Id];
            }
            taken.Add(group);
        }
        return (twins, units);
    }

    // Two groups fit one chain along an axis when one stands wholly beyond the other along it, or
    // when their bolts stand at the same coordinates on it.
    private static bool Fit(double[][] a, double[][] b, int axis, double tolerance)
    {
        var (aMin, aMax) = (a.Min(point => point[axis]), a.Max(point => point[axis]));
        var (bMin, bMax) = (b.Min(point => point[axis]), b.Max(point => point[axis]));
        if (aMax < bMin - tolerance || bMax < aMin - tolerance) return true;
        bool Covered(double[][] from, double[][] by) => from.All(p => by.Any(q => Math.Abs(p[axis] - q[axis]) <= tolerance));
        return Covered(a, b) && Covered(b, a);
    }

    // What makes two groups alike for one chain; null keeps the group apart. The hole is compared in
    // each dimensioned part: a slot set on the group but switched off for that part does not count.
    private static string? MergeKey(BoltGroupMerge merge, BoltGroupGeometry? group, int[] parts) => group == null ? null : merge switch {
        BoltGroupMerge.SameHole => string.Join(";", parts.Select(part => HoleKey(group, part))),
        BoltGroupMerge.SameBoltSize => Math.Round(group.BoltSize, 3).ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        _ => null
    };

    private static string HoleKey(BoltGroupGeometry group, int partId)
    {
        var hole = group.HoleIn(partId);
        return FormattableString.Invariant(
            $"{Math.Round(group.HoleDiameter, 3)}|{hole.HoleType}|{Math.Round(hole.SlottedHoleX, 3)}|{Math.Round(hole.SlottedHoleY, 3)}");
    }

    // The bolt and the hole in the given part of every group a chain measures.
    private static object[] Holes(IEnumerable<int> groupIds, IReadOnlyDictionary<int, BoltGroupGeometry> geometries, int partId) =>
        groupIds.Where(geometries.ContainsKey).Select(id => geometries[id]).Select(group => (group, hole: group.HoleIn(partId)))
            .Select(item => (object)new {
                boltGroupId = item.group.ModelId, partId, boltStandard = item.group.BoltStandard, boltSize = item.group.BoltSize,
                holeDiameter = item.group.HoleDiameter, holeOnly = item.group.HoleOnly, specialHole = item.hole.Special,
                holeType = item.hole.HoleType, slottedHoleX = item.hole.SlottedHoleX, slottedHoleY = item.hole.SlottedHoleY
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
