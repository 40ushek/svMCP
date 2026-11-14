using static TeklaMcpServer.Api.Drawing.OutlineTreeGeometry;

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
    internal static BoltHoleGeometry[] Holes(IEnumerable<int> groupIds,
        IReadOnlyDictionary<int, BoltGroupGeometry> geometries, int partId) =>
        groupIds.Where(geometries.ContainsKey).Select(id => new BoltHoleGeometry(geometries[id], partId)).ToArray();

    // Every center of each set has a center of the other within the tolerance.
    internal static bool SamePositions(double[][] a, double[][] b, double tolerance)
        => Covers(a, b, tolerance) && Covers(b, a, tolerance);

    internal static bool Covers(IReadOnlyList<double[]> sources, IReadOnlyList<double[]> coveredBy, double tolerance) =>
        sources.Count > 0 && sources.All(point => coveredBy.Any(other =>
            Math.Abs(point[0] - other[0]) <= tolerance && Math.Abs(point[1] - other[1]) <= tolerance));

    internal static BoltChainCandidate? CoveringMainPart(IEnumerable<BoltChainCandidate> chains, int axis,
        IReadOnlyList<double[]> sources, double tolerance, bool includeDependentChains = false) =>
        chains.FirstOrDefault(chain => chain.Kind == BoltChainCandidate.PartKind && chain.MainPart && chain.Axis == axis
            && (includeDependentChains || chain.RequiresChain == null) && Covers(sources, chain.SourcePositions, tolerance));

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

}
