using System.Text.Json;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>Pure, provisional internal-spacing and part-edge proposals from the frozen bolt snapshot.</summary>
internal static class BoltDimensionChainPreview
{
    // Model/view units, independent of view scale and display rounding.
    internal const double CoordinateTolerance = DimensionCoordinateSettings.DefaultToleranceMm;

    internal static object Build(JsonElement snapshot, IReadOnlyCollection<int> includedPartIds,
        IReadOnlyCollection<DimensionChainSide> sides,
        IReadOnlyDictionary<int, IReadOnlyList<OutlineTreeNodeResult>>? partContours = null,
        DimensionCoordinateSettings? coordinateSettings = null, BoltChainSettings? boltSettings = null)
    {
        var tolerance = (coordinateSettings ?? new DimensionCoordinateSettings()).ToleranceMm;
        var settings = boltSettings ?? new BoltChainSettings();
        var groups = BuildGroups(snapshot, includedPartIds, sides, partContours, tolerance, settings)
            .Select(group => group.Project()).ToArray();
        return new {
            scope = "bolt-chain-proposals", coordinateSystem = "display",
            coordinateTolerance = tolerance, classToleranceDegrees = settings.ClassToleranceDegrees,
            geometryReadComplete = Flag(snapshot, "isComplete"),
            selectionComplete = Flag(snapshot, "selectionComplete"),
            visibilityVerified = Flag(snapshot, "visibilityVerified"),
            creationMode = "explicitSelectionOnly",
            pending = new[] { "part scope and internal-dimension policy", "selection and visibility verification",
                "placement side and offset", "edge-dimension policy", "group-position datum and chains" },
            unread = snapshot.TryGetProperty("unread", out var unread) ? unread : default(JsonElement?),
            error = snapshot.TryGetProperty("error", out var error) ? error : default(JsonElement?),
            groups,
            partChains = BoltPartChainPreview.Build(snapshot, includedPartIds, sides, partContours, tolerance, settings)
        };
    }

    internal static Group[] BuildGroups(JsonElement snapshot, IReadOnlyCollection<int> includedPartIds,
        IReadOnlyCollection<DimensionChainSide> sides, IReadOnlyDictionary<int, IReadOnlyList<OutlineTreeNodeResult>>? contours,
        double tolerance, BoltChainSettings settings) => snapshot.TryGetProperty("boltGroups", out var rows)
        ? rows.EnumerateArray().Select(row => BuildGroup(row, includedPartIds, sides, contours, tolerance, settings)).ToArray()
        : [];

    private static bool Flag(JsonElement value, string name) =>
        value.TryGetProperty(name, out var flag) && flag.ValueKind == JsonValueKind.True;

    private static Group BuildGroup(JsonElement row, IReadOnlyCollection<int> includedPartIds,
        IReadOnlyCollection<DimensionChainSide> sides,
        IReadOnlyDictionary<int, IReadOnlyList<OutlineTreeNodeResult>>? partContours, double tolerance,
        BoltChainSettings settings)
    {
        var group = ReadGroup(row);
        var viewClass = BoltGroupViewClassifier.Classify(group.Axis, settings.ClassToleranceDegrees);
        var related = new[] { group.PartToBeBoltedId, group.PartToBoltToId }
            .Where(id => id.HasValue).Select(id => id!.Value).Concat(group.OtherPartIds)
            .Distinct().OrderBy(id => id).ToArray();
        var partCandidates = related.Where(includedPartIds.Contains).ToArray();
        var centers = row.GetProperty("restriction").EnumerateArray()
            .GroupBy(value => value.GetProperty("index").GetInt32())
            .ToDictionary(values => values.Key, values => values.Count() == 1
                ? values.First().GetProperty("centerState").GetString() : "Unresolved");
        var reason = PatternRefusal(group, viewClass, tolerance);
        // Why the group gives no steps; its edge chains do not depend on it.
        var stepReason = reason == null ? StepRefusal(group, viewClass, tolerance) : null;
        var chains = new List<StepChain>();
        var edgeChains = new List<object>();
        var candidateChainCount = 0;
        var decisions = new List<GroupPoint>();
        if (reason != null)
        {
            decisions.AddRange(group.Positions.Select(p => new GroupPoint(p.Index, p.Point, "Blocked", reason!)));
        }
        else
        {
            var x = Cluster(group.Positions, 0, tolerance);
            var y = Cluster(group.Positions, 1, tolerance);
            var cells = group.Positions.GroupBy(p => (X: x[p.Index], Y: y[p.Index]))
                .OrderBy(cell => cell.Key.Y).ThenBy(cell => cell.Key.X)
                .Select(cell => new Cell(cell.Key.X, cell.Key.Y, cell.OrderBy(p => p.Index).ToArray())).ToArray();
            if (stepReason == null && cells.Length != x.Values.Distinct().Count() * y.Values.Distinct().Count())
                stepReason = "projected pattern is staggered, irregular or incomplete; rectangular grouping is not justified";
            // Every center stays a source of the edge chains whatever the pattern; only the steps
            // need a rectangular grid along the view axes.
            foreach (var cell in cells)
            {
                foreach (var p in cell.Sources)
                {
                    centers.TryGetValue(p.Index, out var center);
                    decisions.Add(new GroupPoint(p.Index, p.Point,
                        center == "Inside" ? (p == cell.Point ? "Candidate" : "Merged") : "Blocked",
                        center == "Inside" ? (p == cell.Point ? "internal-spacing source" : "duplicate projected center")
                            : "center restriction is " + (center ?? "Unresolved") + "; axial extent is not verified",
                        cell.Point.Index));
                }
            }
            if (stepReason == null && sides.Any(side => side.RunsAlongX()))
            {
                candidateChainCount += AddChains(chains, cells.GroupBy(cell => cell.Y), 0, group.ModelId, centers);
                AddEdgeChains(edgeChains, cells.GroupBy(cell => cell.Y), 0, group.ModelId, centers, partCandidates, partContours, tolerance);
            }
            if (stepReason == null && sides.Any(side => side is DimensionChainSide.Left or DimensionChainSide.Right))
            {
                candidateChainCount += AddChains(chains, cells.GroupBy(cell => cell.X), 1, group.ModelId, centers);
                AddEdgeChains(edgeChains, cells.GroupBy(cell => cell.X), 1, group.ModelId, centers, partCandidates, partContours, tolerance);
            }
        }
        if (reason == null && chains.Count > 0 && candidateChainCount == 0)
            reason = "no internal chain has all source centers admitted by the restriction evidence";
        return new Group(group, viewClass, related, partCandidates, reason, stepReason,
            decisions.ToArray(), chains.ToArray(), edgeChains.ToArray());
    }

    internal sealed class Group(BoltGroupGeometry geometry, BoltGroupViewClass viewClass, int[] related,
        int[] partCandidates, string? reason, string? stepReason, GroupPoint[] points, StepChain[] chains, object[] edgeChains)
    {
        public BoltGroupGeometry Geometry { get; } = geometry;
        public BoltGroupViewClass ViewClass { get; } = viewClass;
        public int[] PartCandidates { get; } = partCandidates;
        public string? Reason { get; } = reason;
        public GroupPoint[] Points { get; } = points;
        public StepChain[] Chains { get; } = chains;

        public object Project() => new {
            boltGroupId = Geometry.ModelId, relatedPartIds = related, partCandidates = PartCandidates,
            partScopeStatus = PartCandidates.Length == 1 ? "single included related part; role/policy still requires review"
                : "unresolved: choose the dimensioned part explicitly",
            shape = Geometry.Shape, viewClass = ViewClass.ToString(),
            status = Reason != null || (Chains.Length > 0 && Chains.All(chain => !chain.Candidate))
                ? "Blocked" : Chains.Length == 0 ? "NoInternalSpacing" : "Candidate",
            candidateChainCount = Chains.Count(chain => chain.Candidate), blockedChainCount = Chains.Count(chain => !chain.Candidate),
            reason = Reason, stepReason, points = Points.Select(point => point.Project()).ToArray(),
            chains = Chains.Select(chain => chain.Project()).ToArray(), edgeChains,
            edgeScopeStatus = PartCandidates.Length == 0 ? "Blocked: no included related part"
                : "separate proposals per related part; choose part and edge policy before creation"
        };
    }

    internal sealed class GroupPoint(int index, double[] point, string state, string reason, int? representativeIndex = null)
    {
        public double[] Point { get; } = point;
        public bool Blocked => state == "Blocked";
        public object Project() => representativeIndex.HasValue
            ? (object)new { index, point = Point, state, reason, representativeIndex = representativeIndex.Value }
            : new { index, state, reason, point = Point };
    }

    internal sealed class StepPoint(double[] point, int[] sourceIndices)
    {
        public double[] Point { get; } = point;
        public int[] SourceIndices { get; } = sourceIndices;
        public object Project() => new { point = Point, sourceIndices = SourceIndices };
    }

    internal sealed class StepChain(string key, int axis, int[] blocked, int startIndex, StepPoint[] points, double[] segments)
    {
        public string Key { get; } = key;
        public int Axis { get; } = axis;
        public bool Candidate => blocked.Length == 0;
        public string Reason => Candidate ? "ordered adjacent bolt centers; policy and visibility are not verified"
            : "restriction evidence does not admit every source; the chain is not shortened";
        public StepPoint[] Points { get; } = points;
        public double[] Segments { get; } = segments;
        public object Project() => new {
            proposalId = Key, kind = "internal", axis = Axis == 0 ? "X" : "Y", direction = Axis == 0 ? "horizontal" : "vertical",
            state = Candidate ? "Candidate" : "Blocked", creationMode = "explicitSelectionOnly", reason = Reason,
            blockedIndices = blocked, startIndex, points = Points.Select(point => point.Project()).ToArray(), segments = Segments
        };
    }

    internal static BoltGroupGeometry ReadGroup(JsonElement row) =>
        JsonSerializer.Deserialize<BoltGroupGeometry>(row.GetProperty("geometry").GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    /// <summary>
    /// Why no chain is built for this group, or null. Plan and Side groups are projected on the
    /// view plane; coinciding projected centers merge later, keeping every source index.
    /// </summary>
    internal static string? PatternRefusal(BoltGroupGeometry group, BoltGroupViewClass viewClass,
        double tolerance = CoordinateTolerance)
    {
        if (group.Positions.Count == 0 || group.Positions.Any(p => !ValidPoint(p.Point)))
            return "missing or invalid bolt positions";
        if (group.Positions.Select(p => p.Index).Distinct().Count() != group.Positions.Count)
            return "source bolt indices are not unique";
        if (group.Shape != "BoltArray") return "only BoltArray is supported by this first preview";
        if (viewClass == BoltGroupViewClass.Unresolved)
            return "bolt axis was not read; the group's turn to the view is unresolved";
        if (viewClass == BoltGroupViewClass.Skewed)
            return "bolt group is skewed to the view; no chain is built for it yet";
        return null;
    }

    /// <summary>
    /// Why the group gives no steps between its bolts, or null. Steps run along the view axes, so
    /// they need a group laid out along them. A group in plan or from the side whose row is turned
    /// in the view plane (bolts along a slanted plate edge) is not skewed: only its steps are left
    /// out, its edge chains along the view axes stay. Skewed is the class of a group whose bolt
    /// axis is neither square to the view nor in its plane.
    /// </summary>
    internal static string? StepRefusal(BoltGroupGeometry group, BoltGroupViewClass viewClass,
        double tolerance = CoordinateTolerance)
    {
        if (!ValidPoint(group.FirstPosition) || !ValidPoint(group.SecondPosition))
            return "bolt-group reference direction is missing";
        var dx = Math.Abs(group.SecondPosition[0] - group.FirstPosition[0]);
        var dy = Math.Abs(group.SecondPosition[1] - group.FirstPosition[1]);
        // A Side group may run its reference along the view depth; its projection is still one line.
        if (viewClass == BoltGroupViewClass.Plan && dx <= tolerance && dy <= tolerance)
            return "bolt-group reference direction has no stable XY projection";
        if (dx > tolerance && dy > tolerance)
            return "the bolt row is turned in the view plane; steps run along the view axes only";
        return null;
    }

    // Cluster against the first coordinate, not transitively against the previous point.
    private static Dictionary<int, int> Cluster(IEnumerable<BoltPointGeometry> points, int axis, double tolerance)
    {
        var result = new Dictionary<int, int>();
        var number = -1;
        var first = double.NegativeInfinity;
        foreach (var point in points.OrderBy(p => p.Point[axis]).ThenBy(p => p.Index))
        {
            if (point.Point[axis] - first > tolerance) { first = point.Point[axis]; number++; }
            result.Add(point.Index, number);
        }
        return result;
    }

    private static int AddChains(List<StepChain> chains, IEnumerable<IGrouping<int, Cell>> rows,
        int axis, int groupId, IReadOnlyDictionary<int, string?> centers)
    {
        var candidates = 0;
        foreach (var row in rows.OrderBy(row => row.Key))
        {
            var points = row.OrderBy(cell => cell.Point.Point[axis]).ThenBy(cell => cell.Point.Index).ToArray();
            if (points.Length < 2) continue;
            var blocked = points.SelectMany(cell => cell.Sources)
                .Where(p => !centers.TryGetValue(p.Index, out var state) || state != "Inside")
                .Select(p => p.Index).OrderBy(index => index).ToArray();
            if (blocked.Length == 0) candidates++;
            chains.Add(new StepChain($"bolt-{groupId}-{(axis == 0 ? "X" : "Y")}-{row.Key}", axis, blocked,
                points[0].Point.Index, points.Select(cell => new StepPoint(cell.Point.Point,
                    cell.Sources.Select(p => p.Index).ToArray())).ToArray(),
                points.Skip(1).Select((cell, index) => cell.Point.Point[axis] - points[index].Point.Point[axis]).ToArray()));
        }
        return candidates;
    }

    private static void AddEdgeChains(List<object> chains, IEnumerable<IGrouping<int, Cell>> rows,
        int axis, int groupId, IReadOnlyDictionary<int, string?> centers, int[] partIds,
        IReadOnlyDictionary<int, IReadOnlyList<OutlineTreeNodeResult>>? partContours, double tolerance)
    {
        foreach (var row in rows.OrderBy(row => row.Key))
        {
            var points = row.OrderBy(cell => cell.Point.Point[axis]).ToArray();
            var rowInside = points.SelectMany(cell => cell.Sources).All(p =>
                centers.TryGetValue(p.Index, out var state) && state == "Inside");
            foreach (var partId in partIds)
            {
                IReadOnlyList<OutlineTreeNodeResult>? contours = null;
                partContours?.TryGetValue(partId, out contours);
                var rowId = $"bolt-{groupId}-{(axis == 0 ? "X" : "Y")}-{row.Key}";
                chains.Add(BoltEdgeDistancePreview.Build(groupId, partId, rowId, points[0].Sources, axis, -1, contours, rowInside, tolerance));
                chains.Add(BoltEdgeDistancePreview.Build(groupId, partId, rowId, points[points.Length - 1].Sources, axis, 1, contours, rowInside, tolerance));
            }
        }
    }

    private sealed class Cell(int x, int y, BoltPointGeometry[] sources)
    {
        public int X { get; } = x;
        public int Y { get; } = y;
        public BoltPointGeometry[] Sources { get; } = sources;
        public BoltPointGeometry Point => Sources[0];
    }

    private static bool ValidPoint(double[] point) => point.Length == 3
        && point.All(value => !double.IsNaN(value) && !double.IsInfinity(value));
}
