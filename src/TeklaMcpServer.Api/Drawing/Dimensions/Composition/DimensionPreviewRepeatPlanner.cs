namespace TeklaMcpServer.Api.Drawing;

internal sealed class DimensionPreviewRepeatPlan(DimensionPreviewRow[] rows,
    IReadOnlyList<DimensionChainRepeats.Repeat>? repeats, bool publish)
{
    public DimensionPreviewRow[] Rows { get; } = rows;
    public IReadOnlyList<DimensionChainRepeats.Repeat>? Repeats { get; } = repeats;
    public bool Publish { get; } = publish;
}

/// <summary>Typed repeat selection; the context stores the returned state for batch resolution.
/// Uses the existing repeat rules, without reading formatted preview rows.</summary>
internal static class DimensionPreviewRepeatPlanner
{
    internal static DimensionPreviewRepeatPlan Structural(int viewId, string? viewType, DimensionPreviewRow[] rows,
        Func<IReadOnlyList<DimensionChainRepeats.Placed>>? readSheet, bool stored,
        IReadOnlyList<DimensionChainRepeats.Repeat>? previous)
    {
        var repeats = readSheet != null ? DimensionChainRepeats.Find(viewId, viewType, StructuralChains(rows), readSheet)
            : stored ? previous : null;
        return new(Mark(rows, repeats ?? []), repeats, readSheet != null);
    }

    internal static DimensionPreviewRepeatPlan Carried(int viewId, string? viewType, DimensionPreviewRow[] rows,
        Func<IReadOnlyList<DimensionChainRepeats.Placed>>? readSheet, bool stored,
        IReadOnlyList<DimensionChainRepeats.Repeat>? previous)
    {
        if (readSheet == null && !stored) return new(rows, previous, false);
        var carried = rows.Where(row => row.Side == ViewDimensionContext.BoltSide).SelectMany(row => row.Chains)
            .Where(chain => !chain.Incomplete && (chain.Carries != null || chain.ChainKind == BoltChainCandidate.PositionKind))
            .Select(chain => new DimensionChainRepeats.Chain(chain.Key, chain.PlacementSide!.Value.ToString(), "location", chain.Segments)).ToArray();
        if (carried.Length == 0) return new(rows, previous, false);
        var restored = rows.SelectMany(row => row.Chains).Where(chain => chain.RestoredBesides != null).Select(chain => chain.Key);
        var keys = new HashSet<string>(carried.Select(chain => chain.Key).Concat(restored), StringComparer.Ordinal);
        var repeats = previous;
        if (readSheet != null)
        {
            var found = DimensionChainRepeats.Find(viewId, viewType, StructuralChains(rows).Concat(carried).ToArray(), readSheet)
                .Where(repeat => keys.Contains(repeat.Key));
            repeats = (previous ?? []).Where(repeat => !keys.Contains(repeat.Key)).Concat(found).ToArray();
        }
        return new(Mark(rows, (repeats ?? []).Where(repeat => keys.Contains(repeat.Key))), repeats, readSheet != null);
    }

    // The complete chains compared for repeats; incomplete ones are never placed. Bolt chains are
    // compared by segment lengths here, not by their source bolts: equal steps in different groups
    // prove no repeat, so the Bolt row takes no part.
    internal static IReadOnlyList<DimensionChainRepeats.Chain> StructuralChains(IEnumerable<DimensionPreviewRow> rows) =>
        rows.Where(row => row.Side != ViewDimensionContext.BoltSide).SelectMany(row => row.Chains
            .Where(chain => !chain.Incomplete)
            .Select(chain => new DimensionChainRepeats.Chain(chain.Key, row.Side, chain.Kind, chain.Segments))).ToArray();

    private static DimensionPreviewRow[] Mark(DimensionPreviewRow[] rows, IEnumerable<DimensionChainRepeats.Repeat> repeats)
    {
        var byKey = repeats.ToDictionary(repeat => repeat.Key, StringComparer.Ordinal);
        if (byKey.Count == 0) return rows;
        return rows.Select(row => row.WithChains(row.Chains.Select(chain =>
            (byKey.TryGetValue(chain.Key, out var repeat) ? chain.Repeated(repeat) : chain).PreserveTemplatePrecision(includeCarried: true)))).ToArray();
    }
}
