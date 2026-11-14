using static TeklaMcpServer.Api.Drawing.OutlineTreeGeometry;
using static TeklaMcpServer.Api.Drawing.BoltChainGeometry;
using static TeklaMcpServer.Api.Drawing.BoltChainPlacement;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>Builds main-part position chains and cross-chain alternatives with their dependencies.
/// Selection decides later whether the position and replacement chains are admitted.</summary>
internal static class BoltMainPartChainBuilder
{
    // Across the main part, the bolts of a secondary part the position chain holds are measured from
    // the main part's edges too, as the plant draws (user, 2026-11-14; M.298: the plate at x 1220
    // gave 54 / 80 / 55 from its own edges where the plate at x 300 gave 60 / 80 / 60 from the beam).
    // One chain per chain of the secondary part across, over all its bolts: only where every one of
    // them lies inside the main part, off its edge, and each end point of the chain stands on a main
    // part edge running straight through it. The new chain needs the position chain created (decided
    // last, with the view frame), and the secondary part's chain is linked to it and left out only
    // once it is created; otherwise that chain stays as it was.
    internal static void AddAcross(List<BoltChainCandidate> result, BoltChainCandidate position, BoltChainGroup[] groups,
        IReadOnlyDictionary<int, IReadOnlyList<OutlineTreeNodeResult>>? contours,
        IReadOnlyDictionary<int, BoltGroupGeometry> geometries, double gap, double tolerance)
    {
        if (position.PartId is not { } mainId || contours == null || !contours.TryGetValue(mainId, out var mainOutline)
            || Bounds(contours, mainId) is not { } box) return;
        var axis = 1 - position.Axis;
        var across = position.Axis;
        var edges = Edges(mainOutline);
        // A point stands on a main part edge that runs along the chain's cross direction through it.
        bool OnEdge(double[] point) => edges.Any(edge => Math.Abs(edge.A[axis] - point[axis]) <= tolerance
            && Math.Abs(edge.B[axis] - point[axis]) <= tolerance
            && Math.Min(edge.A[across], edge.B[across]) <= point[across] + tolerance
            && Math.Max(edge.A[across], edge.B[across]) >= point[across] - tolerance);
        var held = new HashSet<int>(position.GroupIds);
        var byId = groups.ToDictionary(group => group.Id);
        var replaced = result.Where(chain => chain.Kind == BoltChainCandidate.PartKind && !chain.MainPart && chain.Axis == axis
            && chain.BuiltRefusal == null && chain.OverMainPartChain == null && chain.GroupIds.Count > 0
            && chain.GroupIds.All(id => held.Contains(id) && byId.ContainsKey(id))).ToArray();
        var acrossByGroups = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var own in replaced)
        {
            // Several secondary parts may share all the same bolt groups. Reuse their chain by
            // source identity: its projected row points retain only one bolt per row.
            var groupKey = string.Join("-", own.GroupIds.OrderBy(id => id));
            if (acrossByGroups.TryGetValue(groupKey, out var sharedKey))
            {
                result[result.IndexOf(own)] = own.Over(sharedKey);
                continue;
            }
            var bolts = own.GroupIds.SelectMany(id => byId[id].Positions).ToArray();
            if (BoltPartChainPreview.BoltsOnOneContour(mainOutline, bolts, tolerance, out _, out var onEdge) != null || onEdge)
                continue;
            // Bolts standing over the main part's own bolts already have its chain across (bolts over bolts).
            var covering = CoveringMainPart(result, axis, bolts, tolerance, includeDependentChains: true);
            if (covering != null)
            {
                result[result.IndexOf(own)] = own.Over(covering.Key);
                continue;
            }
            var (low, high) = (box.Min[axis], box.Max[axis]);
            var center = bolts.Average(point => point[across]);
            BoltChainSide? best = null;
            var bestReach = double.MaxValue;
            var near = Placement(contours, mainId, axis, center, gap, tolerance);
            foreach (var sign in new[] { near.Sign, -near.Sign })
            {
                // One point per row across, carried by the bolt nearest the line; the main part's edges close it.
                var rows = Representatives(bolts, axis, sign, tolerance);
                var x = rows.Max(point => sign * point[across]) * sign;
                double[] At(double along) => axis == 0 ? new[] { along, x } : new[] { x, along };
                var points = new[] { At(low) }.Concat(rows).Concat([At(high)]).ToArray();
                if (points.Skip(1).Select((point, index) => point[axis] - points[index][axis]).Any(value => value <= tolerance)) continue;
                var placement = Placement(contours, mainId, axis, center, gap, tolerance, sign);
                var key = $"bolt-part-{mainId}-groups-{groupKey}-{placement.Side}";
                var side = Beside(contours, mainId, axis, new BoltChainSide(placement, key, points, null), bolts, gap, tolerance);
                if (side.Placement.Refusal != null || side.Points.Length < 2) continue;
                // The end points as finally placed must stand on the main part's edges.
                if (!OnEdge(side.Points[0]) || !OnEdge(side.Points[side.Points.Length - 1])) continue;
                var reach = Math.Abs(side.Points[0][across] + side.Placement.Sign * side.Placement.Offset(side.Points) - center);
                if (reach < bestReach - tolerance) (best, bestReach) = (side, reach);
            }
            if (best is not { } chosen) continue;
            var line = chosen.Placement;
            var chainKey = chosen.Key;
            var chainPoints = chosen.Points;
            result.Add(new BoltChainCandidate(chainKey, BoltChainCandidate.PartKind, own.GroupIds[0], mainId, true, BoltGroupViewClass.Plan,
                axis, RulePoints(chainKey, own.GroupIds[0], chainPoints),
                chainPoints.Skip(1).Select((point, index) => Math.Round(point[axis] - chainPoints[index][axis], 3)), null, true,
                line.Normal, line.Offset(chainPoints), BoltChainSources.Points(chainPoints),
                Holes(own.GroupIds, geometries, mainId), groupIds: own.GroupIds, requiresChain: position.Key, sourcePositions: bolts));
            acrossByGroups.Add(groupKey, chainKey);
            var index = result.IndexOf(own);
            result[index] = own.Over(chainKey);
        }
    }

    // Tekla's Position bolts to: Main part, measured here from the main part's physical start (the
    // plant's practice; Tekla takes the reference line): one chain along the main part through every
    // bolt column in order, closed on the far end (user, 2026-11-14; M.298:
    // 40 / 260 / 760 / 60 / 100 / 660 / 2800 / 40). A far end that is not square leaves the chain open
    // at its last column and says so.
    // The start is the end the part's start point lies at, so a mirrored view or a member drawn the
    // other way starts at the same end; the datum lies on that end's contour, which must be square.
    // Plan groups of the main part take part, and with Secondary part: By bolt or By both the groups
    // of the assembly's secondary parts too, whatever the bolts join them to: a plate welded to the
    // beam is located by its holes even when they join it to a member outside the assembly (M.298,
    // the plate at x 1220). The parts are those of the group's model links included on the view.
    // A group standing past the main part's ends does not lie along it. Null when no group takes part.
    internal static BoltChainCandidate? BuildPosition(BoltChainGroup[] groups, IReadOnlyCollection<int> mainPartIds,
        IReadOnlyDictionary<int, IReadOnlyList<OutlineTreeNodeResult>>? contours, double[] start, double[] end,
        BoltChainSettings settings, IReadOnlyDictionary<int, BoltGroupGeometry> geometries, double gap, double tolerance)
    {
        var bySecondary = settings.SecondaryPart is BoltSecondaryPartPosition.ByBolt or BoltSecondaryPartPosition.ByBoth;
        var main = mainPartIds.Count == 1 ? mainPartIds.First() : (int?)null;
        if (main is not { } id || Bounds(contours, id) is not { } span) return null;
        // Every bolt of a group in one outer contour of one of its parts, by the same check as its
        // part chains: a group whose bolts stand outside its parts, or each in another contour, is
        // left out (a missing usable edge does not matter here).
        // A Plan group with a center on its part's edge is refused by its part chains (provisional);
        // it stays out of the position chain too.
        bool OnOnePart(BoltChainGroup group) => contours != null && group.Parts.Any(part => contours.TryGetValue(part, out var outline)
            && BoltPartChainPreview.BoltsOnOneContour(outline, group.Positions, tolerance, out _, out var onEdge) == null && !onEdge);
        var taking = groups.Where(group => group.ViewClass == BoltGroupViewClass.Plan
            && group.Admitted && group.Positions.Length > 0 && group.Parts.Length > 0
            && (group.Parts.Contains(id) || bySecondary) && OnOnePart(group)).ToArray();
        if (taking.Length == 0) return null;
        var groupIds = taking.Select(group => group.Id).OrderBy(groupId => groupId).ToArray();
        var mainId = id;
        BoltChainCandidate Refused(string reason) => new($"bolt-position-{groupIds[0]}", BoltChainCandidate.PositionKind,
            groupIds[0], mainId, true, BoltGroupViewClass.Plan, 0, [], [], reason, false, new DimensionDirection(0, 1), 0,
            BoltChainSources.Empty, Holes(groupIds, geometries, mainId), groupIds: groupIds);
        var (dx, dy) = (end[0] - start[0], end[1] - start[1]);
        var axis = Math.Abs(dx) >= Math.Abs(dy) ? 0 : 1;
        var across = 1 - axis;
        var run = axis == 0 ? dx : dy;
        if (Math.Abs(run) <= tolerance || Math.Abs(axis == 0 ? dy : dx) > Math.Max(tolerance, Math.Abs(run) * 1e-3))
            return Refused("the main part does not run along a view axis");
        var forward = run > 0 ? 1 : -1;
        var box = span;
        taking = taking.Where(group => group.Positions.All(point => point[axis] >= box.Min[axis] - tolerance
            && point[axis] <= box.Max[axis] + tolerance)).ToArray();
        if (taking.Length == 0) return null;
        groupIds = taking.Select(group => group.Id).OrderBy(groupId => groupId).ToArray();
        var datum = forward > 0 ? box.Min[axis] : box.Max[axis];
        var far = forward > 0 ? box.Max[axis] : box.Min[axis];
        var outline = Edges(contours![mainId]);
        // The square edges of an end of the main part: across it, at that end's coordinate.
        (double[] A, double[] B)[] EndAt(double at) => outline.Where(edge => Math.Abs(edge.A[axis] - at) <= tolerance
            && Math.Abs(edge.B[axis] - at) <= tolerance && Math.Abs(edge.A[across] - edge.B[across]) > tolerance).ToArray();
        var endEdges = EndAt(datum);
        if (endEdges.Length == 0) return Refused("the start end of the main part is not square on this view");
        var bolts = taking.SelectMany(group => group.Positions).ToArray();
        // The line stands on the side of the main part the bolts stand nearer to.
        var sign = bolts.Average(point => point[across]) >= (box.Min[across] + box.Max[across]) / 2 ? 1 : -1;
        double Outer((double[] A, double[] B)[] edges) => sign > 0 ? edges.Max(edge => Math.Max(edge.A[across], edge.B[across]))
            : edges.Min(edge => Math.Min(edge.A[across], edge.B[across]));
        var datumAcross = Outer(endEdges);
        // One point per column; the column's bolt nearest the line carries it.
        var columns = Representatives(bolts, axis, sign, tolerance, forward);
        double[] At(double along, double acrossAt) => axis == 0 ? new[] { along, acrossAt } : new[] { acrossAt, along };
        var points = new[] { At(datum, datumAcross) }.Concat(columns).ToArray();
        if (points.Skip(1).Select((point, index) => Math.Abs(point[axis] - points[index][axis])).Any(value => value <= tolerance))
            return Refused("a bolt column stands on the start end of the main part");
        // Closed on the far end, on the same side as the datum; a column on that end closes it already.
        string? remark = null;
        if (Math.Abs(far - points[points.Length - 1][axis]) > tolerance)
        {
            var farEdges = EndAt(far);
            if (farEdges.Length > 0) points = points.Concat([At(far, Outer(farEdges))]).ToArray();
            else remark = "the far end of the main part is not square on this view: the chain is not closed";
        }
        var segments = points.Skip(1).Select((point, index) => Math.Round(Math.Abs(point[axis] - points[index][axis]), 3)).ToArray();
        var center = bolts.Average(point => point[across]);
        // The line runs the whole main part, end to end: every part standing out of it along the way
        // is in the way, a part reaching back into the main part included (a plate at the far end).
        var placement = Placement(contours, mainId, axis, center, gap, tolerance, sign, skipUnder: false);
        var key = $"bolt-position-{placement.Side}";
        return new BoltChainCandidate(key, BoltChainCandidate.PositionKind, groupIds[0], mainId, true,
            BoltGroupViewClass.Plan, axis, RulePoints(key, groupIds[0], points), segments, placement.Refusal, true,
            placement.Normal, placement.Offset(points),
            BoltChainSources.Points(points.Skip(1)), Holes(groupIds, geometries, mainId),
            groupIds: groupIds, remark: remark, sourcePositions: bolts);
    }

}
