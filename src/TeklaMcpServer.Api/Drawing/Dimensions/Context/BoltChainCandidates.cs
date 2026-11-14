using System.Text.Json;
using static TeklaMcpServer.Api.Drawing.BoltChainGeometry;
using static TeklaMcpServer.Api.Drawing.BoltChainPlacement;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>Builds typed bolt-chain alternatives from the frozen snapshot. Group merging,
/// main-part geometry and placement have separate owners; final selection happens afterwards.</summary>
internal static class BoltChainCandidates
{
    private static readonly DimensionChainSide[] AllSides =
        [DimensionChainSide.Top, DimensionChainSide.Bottom, DimensionChainSide.Left, DimensionChainSide.Right];

    internal static IReadOnlyList<BoltChainCandidate> Build(JsonElement snapshot, IReadOnlyCollection<int> includedPartIds,
        IReadOnlyCollection<int> mainPartIds, IReadOnlyDictionary<int, IReadOnlyList<OutlineTreeNodeResult>>? contours,
        Func<int, int, double[], bool> edgeConfirmed, BoltChainSettings settings, DimensionCoordinateSettings? coordinateSettings = null,
        double lineGap = 0, double[]? mainStart = null, double[]? mainEnd = null)
    {
        var tolerance = (coordinateSettings ?? new DimensionCoordinateSettings()).ToleranceMm;
        var preview = BoltDimensionChainPreview.BuildGroups(snapshot, includedPartIds,
            AllSides, contours, tolerance, settings);
        var geometries = preview.Select(group => group.Geometry).GroupBy(group => group.ModelId)
            .ToDictionary(group => group.Key, group => group.First());
        var groups = preview.Select(group => BoltChainGroup.Read(group, mainPartIds)).ToArray();
        var (twins, units) = BoltGroupMergePlanner.Build(groups, settings.GroupMerge, tolerance, settings.CombineMinDistance ?? lineGap);
        var partChains = BoltPartChainPreview.BuildChains(snapshot, includedPartIds,
            AllSides, contours, tolerance, settings, perGroup: true, units)
            .ToDictionary(chain => chain.Key, StringComparer.Ordinal);
        var result = new List<BoltChainCandidate>();
        var gap = Math.Max(lineGap, tolerance);
        // The side of a part chain, decided once per merged chain, axis and part, so every group of
        // the merged chain (its steps included) stands on the same side whatever the group order.
        var sideOf = new Dictionary<(int Leader, int Axis, int PartId), BoltChainSide?>();
        BoltChainSide? Side(
            int partId, int axis, int leader, IReadOnlyDictionary<int, int> axisUnits, double center, double[][] bolts)
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
                .Select(side => {
                    var chain = PartChainPoints(partChains[side.Key], end => edgeConfirmed(partId, axis, end), axis, tolerance);
                    return new BoltChainSide(side.Placement, side.Key, chain.Points, chain.Refusal);
                })
                // Without a paper gap there is no measure of "far": the line stays past the part.
                .Select(side => lineGap > 0 ? Beside(contours, partId, axis, side, bolts, gap, tolerance) : side)
                .ToArray();
            // How far the line stands from the bolts; a side giving no chain is not compared.
            double Reach(BoltChainSide side) =>
                side.Placement.Refusal == null && side.Refusal == null && side.Points.Length > 0
                    ? Math.Abs(side.Points[0][across] + side.Placement.Sign * side.Placement.Offset(side.Points) - center)
                    : double.MaxValue;
            BoltChainSide? best = sides.Length == 0 ? null
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
                var across = 1 - axis;
                var bolts = groups.Where(other => merged.Contains(other.Id)).SelectMany(other => other.Positions).ToArray();
                var center = bolts.Select(point => point[across]).DefaultIfEmpty(0).Average();
                // Whether the edges of each part chain on this axis are confirmed; the steps
                // of the group follow the chain of the first part.
                var confirmed = new Dictionary<int, bool>();
                foreach (var partId in leader == groupId ? dimensioned : Array.Empty<int>())
                {
                    if (Side(partId, axis, leader, axisUnits, center, bolts) is not { } best) continue;
                    var placement = best.Placement;
                    var partKey = best.Key;
                    var points = best.Points;
                    var chainRefusal = best.Refusal;
                    var part = partChains[partKey];
                    confirmed[partId] = points.Length >= 2;
                    result.Add(new BoltChainCandidate(partKey, BoltChainCandidate.PartKind, groupId, partId,
                        mainPartIds.Contains(partId), viewClass, axis, RulePoints(partKey, groupId, points),
                        points.Skip(1).Select((point, index) => point[axis] - points[index][axis]),
                        placement.Refusal ?? chainRefusal,
                        confirmed[partId], placement.Normal, points.Length > 0 ? placement.Offset(points) : 0,
                        BoltChainSources.Captured(part.ProjectedPositions), Holes(merged, geometries, partId), groupIds: merged, sourcePositions: bolts));
                }
                // The steps of a group are the same whichever part it bolts: offered once, beside the first part.
                // A group merged into another one's chain takes the side chosen for that chain.
                var first = Side(dimensioned[0], axis, leader, axisUnits, center, bolts)?.Placement
                    ?? Placement(contours, dimensioned[0], axis, center, gap, tolerance);
                foreach (var chain in group.Chains.Where(chain => chain.Axis == axis))
                {
                    var key = chain.Key;
                    var points = chain.Points.Select(point => point.Point).ToArray();
                    result.Add(new BoltChainCandidate(key, BoltChainCandidate.InternalKind, groupId, dimensioned[0],
                        mainPartIds.Contains(dimensioned[0]), viewClass, axis, RulePoints(key, groupId, points),
                        chain.Segments,
                        first.Refusal ?? (chain.Candidate ? null : chain.Reason),
                        confirmed.TryGetValue(dimensioned[0], out var firstConfirmed) && firstConfirmed, first.Normal, points.Length > 0 ? first.Offset(points) : 0,
                        BoltChainSources.Indices(chain.Points.Select(point => point.SourceIndices)),
                        Holes([groupId], geometries, dimensioned[0])));
                }
            }
        }
        // The position chain is offered with the others; whether it replaces them is decided last.
        // A main part with no axis (a plate) offers no position chain at all.
        if (mainStart is { Length: >= 2 } && mainEnd is { Length: >= 2 } && settings.PositionBoltsTo != BoltPositionTo.None
            && BoltMainPartChainBuilder.BuildPosition(groups, mainPartIds, contours, mainStart, mainEnd, settings, geometries, gap, tolerance) is { } position)
        {
            result.Add(position);
            if (position.BuiltRefusal == null)
                BoltMainPartChainBuilder.AddAcross(result, position, groups, contours, geometries, gap, tolerance);
        }
        return OverMainPartBolts(result, tolerance);
    }

    // Bolts of a secondary part standing on the very points of the main part's bolts (a plate bolted
    // over the beam's holes, two groups in the model) are dimensioned once, from the main part: the
    // plate has its own drawing (M.298). Here the plate's chain is only linked to the main part's
    // chain on that axis; it is left out where that chain is finally created, after the settings
    // and the view frame have had their say.
    private static List<BoltChainCandidate> OverMainPartBolts(List<BoltChainCandidate> result, double tolerance)
    {
        return result.Select(chain => chain.Kind != BoltChainCandidate.PartKind || chain.MainPart
                || chain.SourcePositions.Count == 0
                || CoveringMainPart(result, chain.Axis, chain.SourcePositions, tolerance) is not { } by
            ? chain : chain.Over(by.Key)).ToList();
    }

    // Edge - bolts - edge with each end kept on its own when the view confirms it. One end left
    // gives edge - bolts; none, a refusal. Blocked bolt sources and split contours refuse as before.
    private static (double[][] Points, string? Refusal) PartChainPoints(BoltPartChainGeometry part, Func<double[], bool> confirmed,
        int axis, double tolerance)
    {
        var reason = part.Reason;
        if (part.BlockedSources.Length > 0) return ([], reason);
        if (part.BoltRefusal != null) return ([], part.BoltRefusal);
        var bolts = part.ProjectedPositions.Select(position => position.Point).OrderBy(point => point[axis]).ToArray();
        double[]? End(double[]? point) => point != null && confirmed(point) ? point : null;
        var min = End(part.MinEnd);
        var max = End(part.MaxEnd);
        if (bolts.Length == 0) return ([], reason ?? "no bolt position");
        if (min == null && max == null) return ([], "no part edge is confirmed on this view" + (reason != null ? "; " + reason : ""));
        var points = (min == null ? bolts : new[] { min }.Concat(bolts)).Concat(max == null ? [] : new[] { max }).ToArray();
        var bad = points.Skip(1).Select((point, index) => point[axis] - points[index][axis]).Any(value => value <= tolerance);
        return (points, bad ? "edge endpoints do not bound all projected bolt coordinates" : null);
    }

    private static BoltChainCandidate Refused(int groupId, int? partId, BoltGroupViewClass viewClass, string reason, IEnumerable<BoltHoleGeometry> holes) =>
        new($"bolt-{groupId}", BoltChainCandidate.GroupKind, groupId, partId, false, viewClass, 0, [], [], reason,
            false, new DimensionDirection(0, -1), 0, BoltChainSources.Empty, holes);

}
