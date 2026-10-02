using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// Conservative shared-datum union with explicit compatibility evidence; original proposals remain intact.
/// No Tekla access, support substitution, classification, placement or writing occurs here.
/// </summary>
internal static class DimensionChainComposer
{
    public static DimensionCompositionPlan Compose(IEnumerable<DimensionRuleResult> proposals,
        IReadOnlyDictionary<string, object>? viewEvidence = null, DimensionCoordinateSettings? coordinateSettings = null)
    {
        var settings = coordinateSettings ?? new DimensionCoordinateSettings();
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
                issues.Length == 0 ? EligibilityReason(p, settings.ToleranceMm) ?? "No compatible proposal with a verified supported datum."
                    : string.Join(" ", issues.Select(issue => issue.Reason)), issues);
        }).ToArray();
        var eligible = inputs.Where((p, index) => decisions[index].Kind != DimensionCompositionDecisionKind.Blocked
            && EligibilityReason(p, settings.ToleranceMm) == null).OrderBy(p => p.Proposal!.ProposalId, StringComparer.Ordinal).ToArray();
        var reasons = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var ambiguous = new Dictionary<string, DimensionProposalIssue>(StringComparer.Ordinal);
        var groups = CompatibleGroups(eligible, reasons, ambiguous, settings.ToleranceMm);
        var chains = groups.Where(group => group.Count > 1).Select(group => BuildChain(group, settings.ToleranceMm)).ToArray();
        if (chains.Select(chain => chain.ChainId).Distinct(StringComparer.Ordinal).Count() != chains.Length)
            throw new ArgumentException("Shortened composed-chain ID collision.", nameof(proposals));
        for (var index = 0; index < inputs.Length; index++)
        {
            var id = inputs[index].Proposal!.ProposalId;
            var chain = chains.FirstOrDefault(c => c.Proposals.Any(p => p.Proposal!.ProposalId == id));
            if (chain != null) decisions[index] = new(id, DimensionCompositionDecisionKind.Combined,
                "Combined under explicit matching semantics and an identical supported datum; all original proposals retained.", [], chain.ChainId);
            else if (ambiguous.TryGetValue(id, out var ambiguity))
                decisions[index] = new(id, DimensionCompositionDecisionKind.KeepSeparate, ambiguity.Reason, [ambiguity]);
            else if (decisions[index].Kind == DimensionCompositionDecisionKind.KeepSeparate && reasons.TryGetValue(id, out var incompatible))
                decisions[index] = new(id, DimensionCompositionDecisionKind.KeepSeparate,
                    string.Join(" ", incompatible.OrderBy(reason => reason, StringComparer.Ordinal)), []);
        }
        // View-level failures do not prove which particular proposal is incomplete.
        return new DimensionCompositionPlan(inputs, decisions, PartIssues(viewEvidence).ToArray(), chains, settings);
    }

    private static List<List<DimensionRuleResult>> CompatibleGroups(DimensionRuleResult[] eligible,
        Dictionary<string, HashSet<string>> reasons, Dictionary<string, DimensionProposalIssue> ambiguous, double tolerance)
    {
        var adjacent = eligible.ToDictionary(p => p.Proposal!.ProposalId, _ => new HashSet<string>(StringComparer.Ordinal));
        for (var i = 0; i < eligible.Length; i++)
            for (var j = i + 1; j < eligible.Length; j++)
            {
                var a = eligible[i].Proposal!.ProposalId;
                var b = eligible[j].Proposal!.ProposalId;
                var reason = CompatibilityReason(eligible[i], eligible[j], tolerance);
                if (reason == null) { adjacent[a].Add(b); adjacent[b].Add(a); }
                else
                    foreach (var id in new[] { a, b })
                    {
                        if (!reasons.TryGetValue(id, out var set)) reasons[id] = set = new HashSet<string>(StringComparer.Ordinal);
                        set.Add(reason);
                    }
            }
        var groups = new List<List<DimensionRuleResult>>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var proposal in eligible)
        {
            var id = proposal.Proposal!.ProposalId;
            if (!visited.Add(id)) continue;
            var component = new HashSet<string>(StringComparer.Ordinal) { id };
            var pending = new Stack<string>();
            pending.Push(id);
            while (pending.Count > 0)
                foreach (var neighbor in adjacent[pending.Pop()])
                    if (visited.Add(neighbor)) { component.Add(neighbor); pending.Push(neighbor); }
            if (component.All(member => adjacent[member].Count == component.Count - 1))
                groups.Add(eligible.Where(p => component.Contains(p.Proposal!.ProposalId)).ToList());
            else
            {
                var issue = new DimensionProposalIssue("ambiguous-composition",
                    "Compatibility is non-transitive: competing unions exist. All proposals in this component remain separate; explicit selection or policy is required.",
                    proposalIds: component);
                foreach (var member in component) ambiguous[member] = issue;
            }
        }
        return groups;
    }

    private static string? EligibilityReason(DimensionRuleResult proposal, double tolerance)
    {
        if (proposal.CompositionIntent == null) return "Composition intent is unspecified; category, units, reference identity, scope and measurement type are required.";
        if (proposal.Proposal!.Purpose == DimensionMeasurementPurpose.Unknown
            || proposal.Proposal.Reference == DimensionMeasurementReference.Unknown
            || proposal.Proposal.ReferenceSupport == DimensionReferenceSupport.Unspecified)
            return "Purpose, reference or reference support is unresolved.";
        if (proposal.Proposal.Purpose == DimensionMeasurementPurpose.Overall || proposal.Proposal.Purpose == DimensionMeasurementPurpose.Check)
            return "Overall and check measurements are retained separately by this policy.";
        if (proposal.CompositionIntent.Closure != DimensionChainClosure.Open) return "Closed-chain union is unsupported by this policy.";
        var along = proposal.Points.Select(point => Along(point, proposal.Direction)).ToArray();
        if (along.Any(value => double.IsInfinity(value) || double.IsNaN(value))) return "Projected coordinates are not finite.";
        if (double.IsInfinity(along[along.Length - 1] - along[0])) return "Projected span is not finite.";
        var forward = along.Zip(along.Skip(1), (a, b) => b - a > tolerance).All(value => value);
        var backward = along.Zip(along.Skip(1), (a, b) => a - b > tolerance).All(value => value);
        if (!forward && !backward) return "Point order must be strictly monotonic from an endpoint datum.";
        if (proposal.Points.Select(point => point.Id).Distinct(StringComparer.Ordinal).Count() != proposal.Points.Count)
            return "Point identities within a proposal are ambiguous.";
        return null;
    }

    private static string? CompatibilityReason(DimensionRuleResult a, DimensionRuleResult b, double tolerance)
    {
        var x = a.CompositionIntent!;
        var y = b.CompositionIntent!;
        if (x.Category != y.Category) return "Subject categories differ.";
        if (a.Kind != b.Kind || a.Proposal!.Purpose != b.Proposal!.Purpose) return "Measurement purposes differ.";
        if (x.Units != y.Units) return "Units differ.";
        if (a.Direction.X != b.Direction.X || a.Direction.Y != b.Direction.Y) return "Directions differ (including reversal).";
        if (a.Proposal.Reference != b.Proposal.Reference || a.Proposal.ReferenceSupport != b.Proposal.ReferenceSupport
            || x.ReferenceId != y.ReferenceId) return "Reference semantics or identities differ.";
        if (x.ScopeId != y.ScopeId) return "Local or layer scopes differ.";
        if (x.DimensionType != y.DimensionType) return "Measurement types differ.";
        var ap = (OutsideOutlineDimensionPlacement)a.Placement;
        var bp = (OutsideOutlineDimensionPlacement)b.Placement;
        if (ap.OutwardNormal.X != bp.OutwardNormal.X || ap.OutwardNormal.Y != bp.OutwardNormal.Y)
            return "Placement intents differ.";
        if (!SamePoint(a.Points[0], b.Points[0])) return "Starting datum identities or support evidence differ.";
        if (Math.Sign(Along(a.Points[1], a.Direction) - Along(a.Points[0], a.Direction))
            != Math.Sign(Along(b.Points[1], b.Direction) - Along(b.Points[0], b.Direction)))
            return "Chains extend on opposite sides of the datum.";
        foreach (var left in a.Points)
            foreach (var right in b.Points)
                if ((left.Id == right.Id || Math.Abs(Along(left, a.Direction) - Along(right, a.Direction)) <= tolerance) && !SamePoint(left, right))
                    return "Point identity or along-coordinate has incompatible support evidence; no substitution is allowed.";
        return null;
    }

    private static bool SamePoint(DimensionRulePoint a, DimensionRulePoint b) =>
        a.Id == b.Id && a.X == b.X && a.Y == b.Y && SourceKeys(a).SequenceEqual(SourceKeys(b));

    private static IEnumerable<string> SourceKeys(DimensionRulePoint point) => point.Sources.Select(source =>
        JsonSerializer.Serialize(new { source.ObjectKind, source.ModelId, source.GeometryId, source.PointIndex,
            source.FeatureKind, source.IsHole, source.ExtentAlongChain })).OrderBy(key => key, StringComparer.Ordinal);

    private static double Along(DimensionRulePoint point, DimensionDirection direction) => point.X * direction.X + point.Y * direction.Y;

    private static DimensionComposedChain BuildChain(List<DimensionRuleResult> group, double tolerance)
    {
        var first = group[0];
        var sign = Math.Sign(Along(first.Points[1], first.Direction) - Along(first.Points[0], first.Direction));
        var points = group.SelectMany(p => p.Points).GroupBy(p => p.Id, StringComparer.Ordinal).Select(g => g.First())
            .OrderBy(p => sign * Along(p, first.Direction)).ToArray();
        var segments = points.Zip(points.Skip(1), (a, b) => Math.Abs(Along(b, first.Direction) - Along(a, first.Direction))).ToArray();
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
            policy = DimensionCompositionPlan.PolicyVersion, coordinateToleranceMm = tolerance,
            sources = group.Select(p => p.Proposal!.ProposalId).ToArray()
        })));
        var chainId = "composed:" + BitConverter.ToString(bytes, 0, 8).Replace("-", "").ToLowerInvariant();
        return new(chainId, group.ToArray(), points, segments);
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
