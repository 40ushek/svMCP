namespace TeklaMcpServer.Api.Drawing;

/// <summary>Shared view-axis geometry and source metadata used by bolt-chain builders.</summary>
internal static class BoltChainGeometry
{
    // A Plan group is measured along both view axes; a Side group only across its bolt axis:
    // along the bolt axis the view shows the shanks, not a spacing.
    internal static IEnumerable<int> Axes(BoltGroupViewClass viewClass, double[] boltAxis) => viewClass switch {
        BoltGroupViewClass.Plan => [0, 1],
        BoltGroupViewClass.Side when boltAxis.Length == 3 => [Math.Abs(boltAxis[0]) >= Math.Abs(boltAxis[1]) ? 1 : 0],
        _ => []
    };

    // The bolt and the hole in the given part of every group a chain measures.
    internal static object[] Holes(IEnumerable<int> groupIds, IReadOnlyDictionary<int, BoltGroupGeometry> geometries, int partId) =>
        groupIds.Where(geometries.ContainsKey).Select(id => geometries[id]).Select(group => (group, hole: group.HoleIn(partId)))
            .Select(item => (object)new {
                boltGroupId = item.group.ModelId, partId, boltStandard = item.group.BoltStandard, boltSize = item.group.BoltSize,
                holeDiameter = item.group.HoleDiameter, holeOnly = item.group.HoleOnly, specialHole = item.hole.Special,
                holeType = item.hole.HoleType, slottedHoleX = item.hole.SlottedHoleX, slottedHoleY = item.hole.SlottedHoleY
            }).ToArray();

    // Every center of each set has a center of the other within the tolerance.
    internal static bool SamePositions(double[][] a, double[][] b, double tolerance)
        => Covers(a, b, tolerance) && Covers(b, a, tolerance);

    internal static bool Covers(IReadOnlyList<double[]> sources, IReadOnlyList<double[]> coveredBy, double tolerance) =>
        sources.Count > 0 && sources.All(point => coveredBy.Any(other =>
            Math.Abs(point[0] - other[0]) <= tolerance && Math.Abs(point[1] - other[1]) <= tolerance));

    internal static (double[] A, double[] B)[] Edges(IEnumerable<OutlineTreeNodeResult> nodes) =>
        Flatten(nodes).Where(node => !node.IsHole)
            .SelectMany(node => node.Polygon.Select((point, index) =>
                (A: point, B: node.Polygon[(index + 1) % node.Polygon.Count]))).ToArray();

    // Cluster against the first coordinate, not transitively against the preceding bolt.
    // Preserve traversal direction and select the source bolt nearest the dimension line.
    internal static double[][] Representatives(IEnumerable<double[]> bolts, int axis, int outward,
        double tolerance, int forward = 1) => bolts.OrderBy(point => forward * point[axis])
        .Aggregate(new List<List<double[]>>(), (rows, point) => {
            if (rows.Count == 0 || Math.Abs(point[axis] - rows[rows.Count - 1][0][axis]) > tolerance) rows.Add([]);
            rows[rows.Count - 1].Add(point);
            return rows;
        }).Select(row => row.OrderByDescending(point => outward * point[1 - axis]).First()).ToArray();

    internal static IEnumerable<DimensionRulePoint> RulePoints(string key, int groupId, double[][] points) =>
        points.Select((point, index) => new DimensionRulePoint($"{key}-{index}", point[0], point[1],
            [new DimensionPointSource("bolt-group", groupId, key, index)]));

    internal static (double[] Min, double[] Max)? Bounds(IReadOnlyDictionary<int, IReadOnlyList<OutlineTreeNodeResult>>? contours, int partId)
    {
        if (contours == null || !contours.TryGetValue(partId, out var nodes)) return null;
        var points = Flatten(nodes).Where(node => !node.IsHole).SelectMany(node => node.Polygon)
            .Where(point => point.Length >= 2).ToArray();
        if (points.Length == 0) return null;
        return ([points.Min(p => p[0]), points.Min(p => p[1])], [points.Max(p => p[0]), points.Max(p => p[1])]);
    }

    internal static IEnumerable<OutlineTreeNodeResult> Flatten(IEnumerable<OutlineTreeNodeResult> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Children)) yield return child;
        }
    }
}
