using System;
using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// First composition boundary: preserve proposals without guessing equivalence from coordinates.
/// No Tekla access, support substitution, classification, placement or writing occurs here.
/// </summary>
internal static class DimensionChainComposer
{
    public static DimensionCompositionPlan Compose(IEnumerable<DimensionRuleResult> proposals,
        IReadOnlyDictionary<string, object>? viewEvidence = null)
    {
        var inputs = proposals.ToArray();
        if (inputs.Any(p => p.Proposal == null))
            throw new ArgumentException("Every proposal must have snapshot-local identity.", nameof(proposals));
        if (inputs.Select(p => p.Proposal!.ProposalId).Distinct(StringComparer.Ordinal).Count() != inputs.Length)
            throw new ArgumentException("Duplicate proposal IDs or shortened-ID collisions are ambiguous.", nameof(proposals));
        if (inputs.Select(p => (p.Proposal!.ContextId, p.Proposal.ViewId)).Distinct().Count() > 1)
            throw new ArgumentException("Proposals must belong to one frozen view context.", nameof(proposals));
        var decisions = inputs.Select(p => {
            var issues = Refusals(p);
            return new DimensionCompositionDecision(p.Proposal!.ProposalId,
                issues.Length == 0 ? DimensionCompositionDecisionKind.KeepSeparate : DimensionCompositionDecisionKind.Blocked,
                issues.Length == 0 ? "Preserved separately; no combination or coverage equivalence is asserted."
                    : string.Join(" ", issues.Select(issue => issue.Reason)), issues);
        }).ToArray();
        // View-level failures do not prove which particular proposal is incomplete.
        return new DimensionCompositionPlan(inputs, decisions, PartIssues(viewEvidence).ToArray());
    }

    private static DimensionProposalIssue[] Refusals(DimensionRuleResult proposal)
    {
        var issues = PartIssues(proposal.Evidence).ToList();
        if (proposal.Evidence.TryGetValue("legacyPreviewRefusal", out var legacyRefusal) && legacyRefusal is string reason)
            issues.Add(new("unsupported-legacy-preview", reason));
        if (!(proposal.Placement is OutsideOutlineDimensionPlacement))
            issues.Add(new("unsupported-placement", "Placement policy is unsupported: " + proposal.Placement.GetType().FullName + "."));
        if (proposal.Note != null) issues.Add(new("rule-refusal", "Original rule decision: " + proposal.Note));
        if (proposal.Points.Count < 2) issues.Add(new("too-few-points",
            "Fewer than two supported points (" + proposal.Points.Count + ")."));
        var unsupportedIds = proposal.Points.Where(p => p.Sources.Count == 0).Select(p => p.Id).ToArray();
        if (unsupportedIds.Length > 0) issues.Add(new("missing-source-evidence",
            "No source evidence for points: " + string.Join(",", unsupportedIds) + ".", pointIds: unsupportedIds));
        if (issues.Count == 0 && proposal.Evidence.TryGetValue("incomplete", out var incomplete) && incomplete is true)
            issues.Add(new("incomplete-unspecified", "The rule reports an incomplete chain without identifying a cause."));
        return issues.ToArray();
    }

    private static IEnumerable<DimensionProposalIssue> PartIssues(IReadOnlyDictionary<string, object>? evidence)
    {
        if (evidence == null) yield break;
        foreach (var entry in new[] {
            (Key: "missingSupportModelIds", Code: "missing-support", Reason: "No selected support for model IDs: "),
            (Key: "unlocatedModelIds", Code: "unlocated-part", Reason: "No location selected for model IDs: "),
            (Key: "tiltedPartIds", Code: "unsupported-inclined-part", Reason: "Inclined members unsupported by the source rule, model IDs: ")
        })
        {
            if (!evidence.TryGetValue(entry.Key, out var value)) continue;
            if (!(value is IEnumerable<int> ids)) throw new ArgumentException(entry.Key + " must contain model IDs.");
            var sorted = ids.Distinct().OrderBy(id => id).ToArray();
            if (sorted.Length > 0) yield return new(entry.Code, entry.Reason + string.Join(",", sorted) + ".", sorted);
        }
    }
}
