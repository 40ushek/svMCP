using System;
using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing.ViewLayout.Constraints;

internal enum AxisFailure
{
    None,

    /// <summary>The constraints are consistent but need more room than the axis has.</summary>
    Oversize,

    /// <summary>The constraints contradict each other (a cycle, or a separation inside an aligned group).</summary>
    Contradiction
}

internal readonly struct AxisItem
{
    public AxisItem(int id, double size)
    {
        Id = id;
        Size = size;
    }

    public int Id { get; }

    public double Size { get; }
}

/// <summary><c>min(After) &gt;= max(Before) + Gap</c>.</summary>
internal readonly struct AxisSeparation
{
    public AxisSeparation(int beforeId, int afterId, double gap)
    {
        BeforeId = beforeId;
        AfterId = afterId;
        Gap = gap;
    }

    public int BeforeId { get; }

    public int AfterId { get; }

    public double Gap { get; }
}

/// <summary><c>center(Second) - center(First) == Offset</c>.</summary>
internal readonly struct AxisAlignment
{
    public AxisAlignment(int firstId, int secondId, double offset = 0.0)
    {
        FirstId = firstId;
        SecondId = secondId;
        Offset = offset;
    }

    public int FirstId { get; }

    public int SecondId { get; }

    public double Offset { get; }
}

internal sealed class AxisSolution
{
    public AxisSolution(
        AxisFailure failure,
        double requiredSize,
        double availableSize,
        IReadOnlyDictionary<int, double> centers)
    {
        Failure = failure;
        RequiredSize = requiredSize;
        AvailableSize = availableSize;
        Centers = centers;
    }

    public AxisFailure Failure { get; }

    public bool IsFeasible => Failure == AxisFailure.None;

    /// <summary>Smallest axis length that satisfies the constraints. Zero for a contradiction.</summary>
    public double RequiredSize { get; }

    public double AvailableSize { get; }

    /// <summary>Item centers. Centered in the axis when feasible, packed from the lower bound when oversize, empty for a contradiction.</summary>
    public IReadOnlyDictionary<int, double> Centers { get; }
}

/// <summary>
/// Places items along one axis under separation and center-alignment constraints.
/// Aligned items move as one group, each at a fixed offset from the group's
/// position; separations form a DAG whose longest path gives the tightest packing.
/// </summary>
internal static class AxisConstraintSolver
{
    private const double Epsilon = 1e-6;

    /// <param name="anchorId">
    /// Optional item the rest gathers around: items that must precede it are pulled
    /// up to it instead of staying at the lower bound.
    /// </param>
    public static AxisSolution Solve(
        double min,
        double max,
        IReadOnlyList<AxisItem> items,
        IReadOnlyList<AxisSeparation> separations,
        IReadOnlyList<AxisAlignment> alignments,
        int? anchorId = null)
    {
        var available = max - min;
        var indexById = BuildIndex(items);
        var groupOf = new int[items.Count];
        var offsetInGroup = new double[items.Count];
        if (!TryBuildAlignmentGroups(alignments, indexById, groupOf, offsetInGroup))
            return Contradiction(available);

        var groups = groupOf.Distinct().ToList();

        // Extent of every group around its position: [position + Low, position + High].
        var extent = new Dictionary<int, (double Low, double High)>(groups.Count);
        for (var i = 0; i < items.Count; i++)
        {
            var low = offsetInGroup[i] - (items[i].Size / 2.0);
            var high = offsetInGroup[i] + (items[i].Size / 2.0);
            extent[groupOf[i]] = extent.TryGetValue(groupOf[i], out var current)
                ? (Math.Min(current.Low, low), Math.Max(current.High, high))
                : (low, high);
        }

        var successors = groups.ToDictionary(static g => g, static _ => new Dictionary<int, double>());
        var predecessors = groups.ToDictionary(static g => g, static _ => new Dictionary<int, double>());
        foreach (var separation in separations)
        {
            var before = Index(indexById, separation.BeforeId);
            var after = Index(indexById, separation.AfterId);
            var from = groupOf[before];
            var to = groupOf[after];
            var centerDistance = ((items[before].Size + items[after].Size) / 2.0) + separation.Gap;
            if (from == to)
            {
                // Both are fixed inside one group: the separation either already holds or never can.
                if (offsetInGroup[after] - offsetInGroup[before] < centerDistance - Epsilon)
                    return Contradiction(available);
                continue;
            }

            var distance = centerDistance + offsetInGroup[before] - offsetInGroup[after];
            successors[from][to] = successors[from].TryGetValue(to, out var known) ? Math.Max(known, distance) : distance;
            predecessors[to][from] = successors[from][to];
        }

        var order = TopologicalOrder(groups, successors, predecessors);
        if (order.Count < groups.Count)
            return Contradiction(available);

        var earliest = PackForward(order, predecessors, extent, min, fixedPositions: null);
        var required = Span(earliest, extent, min);
        if (required > available + Epsilon)
        {
            return new AxisSolution(
                AxisFailure.Oversize, required, available, ToCenters(items, groupOf, offsetInGroup, earliest, 0.0));
        }

        var positions = earliest;
        if (anchorId.HasValue)
        {
            var gathered = GatherAroundAnchor(
                groupOf[Index(indexById, anchorId.Value)], order, successors, predecessors, extent, min, earliest);
            if (Span(gathered, extent, min) <= available + Epsilon)
                positions = gathered;
        }

        return new AxisSolution(
            AxisFailure.None,
            required,
            available,
            ToCenters(items, groupOf, offsetInGroup, positions, CenteringShift(positions, extent, min, max)));
    }

    private static Dictionary<int, int> BuildIndex(IReadOnlyList<AxisItem> items)
    {
        var indexById = new Dictionary<int, int>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].Size < 0)
                throw new ArgumentException($"Item {items[i].Id} has a negative size.", nameof(items));
            if (indexById.ContainsKey(items[i].Id))
                throw new ArgumentException($"Item {items[i].Id} is listed twice.", nameof(items));

            indexById[items[i].Id] = i;
        }

        return indexById;
    }

    private static int Index(Dictionary<int, int> indexById, int id)
        => indexById.TryGetValue(id, out var index)
            ? index
            : throw new ArgumentException($"Constraint refers to unknown item {id}.");

    /// <summary>
    /// Walks the alignments from the first item of every connected set, giving each
    /// item its group and its offset from that first item. False when two alignment
    /// paths give one item different offsets.
    /// </summary>
    private static bool TryBuildAlignmentGroups(
        IReadOnlyList<AxisAlignment> alignments,
        Dictionary<int, int> indexById,
        int[] groupOf,
        double[] offsetInGroup)
    {
        var links = Enumerable.Range(0, groupOf.Length).Select(static _ => new List<(int To, double Offset)>()).ToArray();
        foreach (var alignment in alignments)
        {
            var first = Index(indexById, alignment.FirstId);
            var second = Index(indexById, alignment.SecondId);
            links[first].Add((second, alignment.Offset));
            links[second].Add((first, -alignment.Offset));
        }

        var visited = new bool[groupOf.Length];
        for (var start = 0; start < groupOf.Length; start++)
        {
            if (visited[start])
                continue;

            visited[start] = true;
            groupOf[start] = start;
            offsetInGroup[start] = 0.0;
            var pending = new Queue<int>();
            pending.Enqueue(start);
            while (pending.Count > 0)
            {
                var item = pending.Dequeue();
                foreach (var (to, offset) in links[item])
                {
                    var expected = offsetInGroup[item] + offset;
                    if (visited[to])
                    {
                        if (Math.Abs(offsetInGroup[to] - expected) > Epsilon)
                            return false;
                        continue;
                    }

                    visited[to] = true;
                    groupOf[to] = start;
                    offsetInGroup[to] = expected;
                    pending.Enqueue(to);
                }
            }
        }

        return true;
    }

    private static List<int> TopologicalOrder(
        List<int> groups,
        Dictionary<int, Dictionary<int, double>> successors,
        Dictionary<int, Dictionary<int, double>> predecessors)
    {
        var pending = groups.ToDictionary(static g => g, g => predecessors[g].Count);
        var ready = new Queue<int>(groups.Where(g => pending[g] == 0));
        var order = new List<int>(groups.Count);
        while (ready.Count > 0)
        {
            var group = ready.Dequeue();
            order.Add(group);
            foreach (var next in successors[group].Keys.OrderBy(static g => g))
            {
                if (--pending[next] == 0)
                    ready.Enqueue(next);
            }
        }

        return order;
    }

    /// <summary>Lowest position of every group that respects its predecessors; fixed groups keep their position.</summary>
    private static Dictionary<int, double> PackForward(
        List<int> order,
        Dictionary<int, Dictionary<int, double>> predecessors,
        Dictionary<int, (double Low, double High)> extent,
        double min,
        Dictionary<int, double>? fixedPositions)
    {
        var positions = new Dictionary<int, double>(order.Count);
        foreach (var group in order)
        {
            if (fixedPositions != null && fixedPositions.TryGetValue(group, out var fixedPosition))
            {
                positions[group] = fixedPosition;
                continue;
            }

            var position = min - extent[group].Low;
            foreach (var predecessor in predecessors[group])
                position = Math.Max(position, positions[predecessor.Key] + predecessor.Value);
            positions[group] = position;
        }

        return positions;
    }

    /// <summary>
    /// Keeps the anchor where the tightest packing put it, pulls everything that must
    /// precede it up against it, then repacks the rest around those fixed positions.
    /// </summary>
    private static Dictionary<int, double> GatherAroundAnchor(
        int anchor,
        List<int> order,
        Dictionary<int, Dictionary<int, double>> successors,
        Dictionary<int, Dictionary<int, double>> predecessors,
        Dictionary<int, (double Low, double High)> extent,
        double min,
        Dictionary<int, double> earliest)
    {
        var fixedPositions = new Dictionary<int, double> { [anchor] = earliest[anchor] };
        var beforeAnchor = new HashSet<int>();
        var visit = new Stack<int>(predecessors[anchor].Keys);
        while (visit.Count > 0)
        {
            var group = visit.Pop();
            if (!beforeAnchor.Add(group))
                continue;
            foreach (var predecessor in predecessors[group].Keys)
                visit.Push(predecessor);
        }

        for (var i = order.Count - 1; i >= 0; i--)
        {
            var group = order[i];
            if (!beforeAnchor.Contains(group))
                continue;

            fixedPositions[group] = successors[group]
                .Where(next => fixedPositions.ContainsKey(next.Key))
                .Min(next => fixedPositions[next.Key] - next.Value);
        }

        return PackForward(order, predecessors, extent, min, fixedPositions);
    }

    private static double Span(
        Dictionary<int, double> positions,
        Dictionary<int, (double Low, double High)> extent,
        double min)
        => positions.Count == 0 ? 0.0 : positions.Max(p => p.Value + extent[p.Key].High) - min;

    private static double CenteringShift(
        Dictionary<int, double> positions,
        Dictionary<int, (double Low, double High)> extent,
        double min,
        double max)
    {
        if (positions.Count == 0)
            return 0.0;

        var blockMin = positions.Min(p => p.Value + extent[p.Key].Low);
        var blockMax = positions.Max(p => p.Value + extent[p.Key].High);
        return ((min + max) - (blockMin + blockMax)) / 2.0;
    }

    private static Dictionary<int, double> ToCenters(
        IReadOnlyList<AxisItem> items,
        int[] groupOf,
        double[] offsetInGroup,
        Dictionary<int, double> positions,
        double shift)
    {
        var centers = new Dictionary<int, double>(items.Count);
        for (var i = 0; i < items.Count; i++)
            centers[items[i].Id] = positions[groupOf[i]] + offsetInGroup[i] + shift;
        return centers;
    }

    private static AxisSolution Contradiction(double available)
        => new(AxisFailure.Contradiction, 0.0, available, new Dictionary<int, double>());
}
