namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// Decides, from the class of the group, the part role and the settings, which built bolt chains
/// become dimensions. Every offered chain comes back with "create" and, when false, the reason;
/// a weak model only places what is marked.
/// </summary>
internal sealed class BoltChainRule(BoltChainSettings settings) : IDimensionRule
{
    public const string Kind = "bolt";

    public DimensionRuleEvaluation Calculate(DimensionRuleContext context)
    {
        var input = context.Require<BoltChainInput>();
        var results = new List<DimensionRuleResult>();
        foreach (var candidate in input.Candidates)
        {
            string? reason;
            switch (candidate.Kind)
            {
                case BoltChainCandidate.GroupKind:
                    reason = candidate.BuiltRefusal ?? "no chain was built for this group";
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
            results.Add(Result(candidate, reason));
        }
        return new DimensionRuleEvaluation(results);
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

    private static DimensionRuleResult Result(BoltChainCandidate candidate, string? reason) =>
        new(new DimensionDirection(candidate.Axis == 0 ? 1 : 0, candidate.Axis == 0 ? 0 : 1),
            new OutsideOutlineDimensionPlacement(candidate.OutwardNormal, 1), Kind, candidate.Points,
            note: reason, segments: candidate.Segments, evidence: new Dictionary<string, object> {
                ["key"] = candidate.Key, ["chainKind"] = candidate.Kind, ["create"] = reason == null,
                ["groupId"] = candidate.GroupId, ["partId"] = candidate.PartId ?? 0,
                ["viewClass"] = candidate.ViewClass.ToString(), ["baseOffset"] = candidate.BaseOffset,
                ["sources"] = candidate.Sources
            });
}
