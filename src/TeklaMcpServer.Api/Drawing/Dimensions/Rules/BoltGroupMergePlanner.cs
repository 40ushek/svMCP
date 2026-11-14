using static TeklaMcpServer.Api.Drawing.BoltChainGeometry;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>Groups admitted source bolts into twins and per-axis merge units, before geometry is built.</summary>
internal static class BoltGroupMergePlanner
{
    // Twins: two groups in the same holes of the same part (bolts and a hole group, say) give the
    // same chains, so the part is dimensioned once, by the first group. Units: alike groups of the
    // same part, class and measured axes share one edge chain; each group maps to the first of its unit.
    // Only groups whose centers are all admitted take part: a blocked one would refuse the chain it joins.
    // Merging is decided per chain axis: groups share a chain along an axis when they stand one beside
    // the other along it, or when their bolts stand at the same coordinates on it. Groups that overlap
    // along the chain with different spacings keep their own chains: one chain would mix both spacings.
    internal static (Dictionary<int, int> Twins, IReadOnlyDictionary<int, int>[] Units) Build(BoltChainGroup[] groups,
        BoltGroupMerge merge, double tolerance, double combineDistance)
    {
        var twins = new Dictionary<int, int>();
        var units = new[] { new Dictionary<int, int>(), new Dictionary<int, int>() };
        var taken = new List<BoltChainGroup>();
        foreach (var group in groups.Where(group => !group.Unbuilt && group.Admitted))
        {
            // Same part, class and measured axes: a Side group is measured across its own bolt axis.
            bool Alike(BoltChainGroup other) => other.ViewClass == group.ViewClass && other.Dimensioned.SequenceEqual(group.Dimensioned)
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
                        .All(member => Fit(member.Positions, group.Positions, axis, tolerance, combineDistance)));
                axisUnits[group.Id] = leader == null ? group.Id : axisUnits[leader.Id];
            }
            taken.Add(group);
        }
        return (twins, units);
    }

    // Two groups fit one chain along an axis when one stands wholly beyond the other along it, or
    // when their bolts stand at the same coordinates on it; and only when they stand no farther apart
    // across it than the combine distance: the chain stands beside each of them (M.298, equal groups
    // at the two ends of a beam keep a cross chain each).
    private static bool Fit(double[][] a, double[][] b, int axis, double tolerance, double combineDistance)
    {
        var across = 1 - axis;
        var apart = Math.Max(a.Min(point => point[across]), b.Min(point => point[across]))
            - Math.Min(a.Max(point => point[across]), b.Max(point => point[across]));
        if (apart > combineDistance + tolerance) return false;
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

}
