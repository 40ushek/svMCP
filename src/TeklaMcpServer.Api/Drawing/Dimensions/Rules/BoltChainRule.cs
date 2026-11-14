namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// Evaluates settings and geometric refusals on typed candidates. Final view admission,
/// coverage and dependencies belong to BoltChainSelectionPlanner; this rule assigns no rows.
/// </summary>
internal sealed class BoltChainRule(BoltChainSettings settings)
{
    public const string Kind = "bolt";

    internal IReadOnlyList<BoltChainDecision> Evaluate(IReadOnlyList<BoltChainCandidate> candidates)
    {
        var results = new List<BoltChainDecision>();
        foreach (var candidate in candidates)
        {
            string? reason;
            switch (candidate.Kind)
            {
                case BoltChainCandidate.GroupKind:
                    reason = candidate.BuiltRefusal ?? "no chain was built for this group";
                    break;
                case BoltChainCandidate.PositionKind:
                    if (settings.PositionBoltsTo == BoltPositionTo.None) continue;
                    reason = settings.PositionBoltsTo == BoltPositionTo.WorkingPoints
                        ? "Position bolts to: Working points is not implemented yet" : Refusal(candidate);
                    break;
                case BoltChainCandidate.PartKind:
                    var partPolicy = Policy(candidate);
                    if (partPolicy is BoltInternalDimensions.None or BoltInternalDimensions.Internal) continue;
                    reason = Refusal(candidate)
                        ?? (partPolicy == BoltInternalDimensions.Necessary ? "setting Necessary is not implemented yet"
                        : !candidate.EdgeConfirmed ? "part edge is not confirmed on this view"
                        : null);
                    break;
                default:
                    // Steps alone are offered only when the setting asks for them (Internal): under All
                    // steps tied to no edge or axis locate nothing, so they never stand in for an edge chain.
                    if (Policy(candidate) != BoltInternalDimensions.Internal) continue;
                    reason = Refusal(candidate);
                    break;
            }
            results.Add(new BoltChainDecision(candidate, reason));
        }
        return results;
    }

    private BoltInternalDimensions Policy(BoltChainCandidate candidate) => settings.For(candidate.MainPart);

    private string? Refusal(BoltChainCandidate candidate)
    {
        if (candidate.BuiltRefusal != null) return candidate.BuiltRefusal;
        if (!settings.Dimensions(candidate.ViewClass))
            return candidate.ViewClass is BoltGroupViewClass.Plan or BoltGroupViewClass.Side
                ? $"class {candidate.ViewClass} is switched off in the settings"
                : $"class {candidate.ViewClass} is not dimensioned";
        if (candidate.Points.Count < 2) return "fewer than two distinct points";
        return null;
    }

}
