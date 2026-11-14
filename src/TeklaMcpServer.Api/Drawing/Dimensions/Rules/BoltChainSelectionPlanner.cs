namespace TeklaMcpServer.Api.Drawing;

/// <summary>Final bolt selection after policy evaluation: view admission, position coverage,
/// required chains, then replacement by main-part chains. Does not place lines or format JSON.</summary>
internal static class BoltChainSelectionPlanner
{
    internal static IReadOnlyList<BoltChainDecision> Select(IReadOnlyList<BoltChainDecision> evaluated,
        string? readRefusal, Func<DimensionRulePoint, bool> outsideFrame)
    {
        var decisions = evaluated.Select(decision => decision.WithViewRefusal(readRefusal
            ?? (decision.Candidate.Points.Any(outsideFrame) ? "a point lies outside the view frame" : null))).ToArray();
        ApplyPositionCoverage(decisions);
        ApplyRequirements(decisions);
        ApplyMainPartReplacements(decisions);
        return decisions;
    }

    private static void ApplyPositionCoverage(BoltChainDecision[] decisions)
    {
        foreach (var position in decisions.Where(decision => decision.Create
                     && decision.Candidate.Kind == BoltChainCandidate.PositionKind).ToArray())
        {
            var held = new HashSet<int>(position.Candidate.GroupIds);
            for (var i = 0; i < decisions.Length; i++)
            {
                var candidate = decisions[i].Candidate;
                if (candidate.Kind == BoltChainCandidate.PartKind && candidate.Axis == position.Candidate.Axis
                    && candidate.GroupIds.All(held.Contains))
                    decisions[i] = decisions[i].WithPositionCoverage(position.Candidate.Key);
            }
        }
    }

    private static HashSet<string> CreatedKeys(IEnumerable<BoltChainDecision> decisions) =>
        new(decisions.Where(decision => decision.Create).Select(decision => decision.Candidate.Key), StringComparer.Ordinal);

    private static void ApplyRequirements(BoltChainDecision[] decisions)
    {
        var placed = CreatedKeys(decisions);
        for (var i = 0; i < decisions.Length; i++)
            if (decisions[i].Create && decisions[i].Candidate.RequiresChain is { Length: > 0 } needed && !placed.Contains(needed))
                decisions[i] = decisions[i].WithViewRefusal($"created only with {needed}, which is not created");
    }

    private static void ApplyMainPartReplacements(BoltChainDecision[] decisions)
    {
        var created = CreatedKeys(decisions);
        for (var i = 0; i < decisions.Length; i++)
            if (decisions[i].Create && decisions[i].Candidate.OverMainPartChain is { Length: > 0 } main && created.Contains(main))
                decisions[i] = decisions[i].WithViewRefusal($"the same bolts are dimensioned from the main part by {main}");
    }
}
