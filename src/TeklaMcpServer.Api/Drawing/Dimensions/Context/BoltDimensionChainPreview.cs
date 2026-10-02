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
        DimensionCoordinateSettings? coordinateSettings = null)
    {
        var tolerance = (coordinateSettings ?? new DimensionCoordinateSettings()).ToleranceMm;
        var groups = snapshot.TryGetProperty("boltGroups", out var rows)
            ? rows.EnumerateArray().Select(row => BuildGroup(row, includedPartIds, sides, partContours, tolerance)).ToArray()
            : Array.Empty<object>();
        return new {
            scope = "bolt-chain-proposals", coordinateSystem = "display",
            coordinateTolerance = tolerance,
            geometryReadComplete = Flag(snapshot, "isComplete"),
            selectionComplete = Flag(snapshot, "selectionComplete"),
            visibilityVerified = Flag(snapshot, "visibilityVerified"),
            creationMode = "explicitSelectionOnly",
            pending = new[] { "part scope and internal-dimension policy", "bolt-plane orientation and selection verification",
                "placement side and offset", "edge-dimension policy", "group-position datum and chains" },
            unread = snapshot.TryGetProperty("unread", out var unread) ? unread : default(JsonElement?),
            error = snapshot.TryGetProperty("error", out var error) ? error : default(JsonElement?),
            groups,
            partChains = BoltPartChainPreview.Build(snapshot, includedPartIds, sides, partContours, tolerance)
        };
    }

    private static bool Flag(JsonElement value, string name) =>
        value.TryGetProperty(name, out var flag) && flag.ValueKind == JsonValueKind.True;

    private static object BuildGroup(JsonElement row, IReadOnlyCollection<int> includedPartIds,
        IReadOnlyCollection<DimensionChainSide> sides,
        IReadOnlyDictionary<int, IReadOnlyList<OutlineTreeNodeResult>>? partContours, double tolerance)
    {
        var geometry = row.GetProperty("geometry");
        var group = JsonSerializer.Deserialize<BoltGroupGeometry>(geometry.GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var related = new[] { group.PartToBeBoltedId, group.PartToBoltToId }
            .Where(id => id.HasValue).Select(id => id!.Value).Concat(group.OtherPartIds)
            .Distinct().OrderBy(id => id).ToArray();
        var partCandidates = related.Where(includedPartIds.Contains).ToArray();
        var centers = row.GetProperty("restriction").EnumerateArray()
            .GroupBy(value => value.GetProperty("index").GetInt32())
            .ToDictionary(values => values.Key, values => values.Count() == 1
                ? values.First().GetProperty("centerState").GetString() : "Unresolved");
        var reason = PatternRefusal(group, tolerance);
        var chains = new List<object>();
        var edgeChains = new List<object>();
        var candidateChainCount = 0;
        var decisions = new List<object>();
        if (reason != null)
        {
            decisions.AddRange(group.Positions.Select(p => (object)new {
                index = p.Index, state = "Blocked", reason, point = p.Point }));
        }
        else
        {
            var x = Cluster(group.Positions, 0, tolerance);
            var y = Cluster(group.Positions, 1, tolerance);
            var cells = group.Positions.GroupBy(p => (X: x[p.Index], Y: y[p.Index]))
                .OrderBy(cell => cell.Key.Y).ThenBy(cell => cell.Key.X)
                .Select(cell => new Cell(cell.Key.X, cell.Key.Y, cell.OrderBy(p => p.Index).ToArray())).ToArray();
            if (cells.Length != x.Values.Distinct().Count() * y.Values.Distinct().Count())
            {
                reason = "projected pattern is staggered, irregular or incomplete; rectangular grouping is not justified";
                decisions.AddRange(group.Positions.Select(p => (object)new {
                    index = p.Index, state = "Blocked", reason, point = p.Point }));
            }
            else
            {
                foreach (var cell in cells)
                {
                    foreach (var p in cell.Sources)
                    {
                        centers.TryGetValue(p.Index, out var center);
                        decisions.Add(new { index = p.Index, point = p.Point,
                            state = center == "Inside" ? (p == cell.Point ? "Candidate" : "Merged") : "Blocked",
                            reason = center == "Inside" ? (p == cell.Point ? "internal-spacing source" : "duplicate projected center")
                                : "center restriction is " + (center ?? "Unresolved") + "; axial extent is not verified",
                            representativeIndex = cell.Point.Index });
                    }
                }
                if (sides.Any(side => side is DimensionChainSide.Top or DimensionChainSide.Bottom))
                {
                    candidateChainCount += AddChains(chains, cells.GroupBy(cell => cell.Y), 0, group.ModelId, centers);
                    AddEdgeChains(edgeChains, cells.GroupBy(cell => cell.Y), 0, group.ModelId, centers, partCandidates, partContours, tolerance);
                }
                if (sides.Any(side => side is DimensionChainSide.Left or DimensionChainSide.Right))
                {
                    candidateChainCount += AddChains(chains, cells.GroupBy(cell => cell.X), 1, group.ModelId, centers);
                    AddEdgeChains(edgeChains, cells.GroupBy(cell => cell.X), 1, group.ModelId, centers, partCandidates, partContours, tolerance);
                }
            }
        }
        if (reason == null && chains.Count > 0 && candidateChainCount == 0)
            reason = "no internal chain has all source centers admitted by the restriction evidence";
        return new {
            boltGroupId = group.ModelId, relatedPartIds = related, partCandidates,
            partScopeStatus = partCandidates.Length == 1 ? "single included related part; role/policy still requires review"
                : "unresolved: choose the dimensioned part explicitly",
            shape = group.Shape,
            status = reason != null || (chains.Count > 0 && candidateChainCount == 0)
                ? "Blocked" : chains.Count == 0 ? "NoInternalSpacing" : "Candidate",
            candidateChainCount, blockedChainCount = chains.Count - candidateChainCount,
            reason, points = decisions, chains, edgeChains,
            edgeScopeStatus = partCandidates.Length == 0 ? "Blocked: no included related part"
                : "separate proposals per related part; choose part and edge policy before creation"
        };
    }

    internal static string? PatternRefusal(BoltGroupGeometry group, double tolerance = CoordinateTolerance)
    {
        if (group.Positions.Count == 0 || group.Positions.Any(p => !ValidPoint(p.Point)))
            return "missing or invalid bolt positions";
        if (group.Positions.Select(p => p.Index).Distinct().Count() != group.Positions.Count)
            return "source bolt indices are not unique";
        if (group.Shape != "BoltArray") return "only BoltArray is supported by this first preview";
        if (group.Positions.Max(p => p.Point[2]) - group.Positions.Min(p => p.Point[2]) > tolerance)
            return "bolt centers span view depth; a face-on planar pattern is not established";
        if (!ValidPoint(group.FirstPosition) || !ValidPoint(group.SecondPosition))
            return "bolt-group reference direction is missing";
        var dx = Math.Abs(group.SecondPosition[0] - group.FirstPosition[0]);
        var dy = Math.Abs(group.SecondPosition[1] - group.FirstPosition[1]);
        if (dx <= tolerance && dy <= tolerance)
            return "bolt-group reference direction has no stable XY projection";
        if (dx > tolerance && dy > tolerance)
            return "skewed bolt-group reference requires an explicit direction policy";
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

    private static int AddChains(List<object> chains, IEnumerable<IGrouping<int, Cell>> rows,
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
            chains.Add(new {
                proposalId = $"bolt-{groupId}-{(axis == 0 ? "X" : "Y")}-{row.Key}", kind = "internal",
                axis = axis == 0 ? "X" : "Y", direction = axis == 0 ? "horizontal" : "vertical",
                state = blocked.Length == 0 ? "Candidate" : "Blocked", creationMode = "explicitSelectionOnly",
                reason = blocked.Length == 0 ? "ordered adjacent bolt centers; policy and visibility are not verified"
                    : "restriction evidence does not admit every source; the chain is not shortened",
                blockedIndices = blocked, startIndex = points[0].Point.Index,
                points = points.Select(cell => new { point = cell.Point.Point,
                    sourceIndices = cell.Sources.Select(p => p.Index).ToArray() }).ToArray(),
                segments = points.Skip(1).Select((cell, index) => cell.Point.Point[axis] - points[index].Point.Point[axis]).ToArray()
            });
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
