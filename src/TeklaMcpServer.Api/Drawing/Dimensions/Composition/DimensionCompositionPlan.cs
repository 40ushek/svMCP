using System;
using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

internal enum DimensionCompositionDecisionKind { KeepSeparate, Blocked }

internal sealed class DimensionProposalIssue
{
    public string Code { get; }
    public string Reason { get; }
    public IReadOnlyList<int> ModelIds { get; }
    public IReadOnlyList<string> PointIds { get; }

    public DimensionProposalIssue(string code, string reason, IEnumerable<int>? modelIds = null,
        IEnumerable<string>? pointIds = null)
    {
        Code = code;
        Reason = reason;
        ModelIds = Array.AsReadOnly((modelIds ?? Array.Empty<int>()).Distinct().OrderBy(id => id).ToArray());
        PointIds = Array.AsReadOnly((pointIds ?? Array.Empty<string>()).Distinct().OrderBy(id => id, StringComparer.Ordinal).ToArray());
    }

    public object Project() => new { code = Code, reason = Reason, modelIds = ModelIds, pointIds = PointIds };
}

internal sealed class DimensionCompositionDecision(string proposalId,
    DimensionCompositionDecisionKind kind, string reason, DimensionProposalIssue[] issues)
{
    public string ProposalId { get; } = proposalId;
    public DimensionCompositionDecisionKind Kind { get; } = kind;
    public string Reason { get; } = reason;
    public IReadOnlyList<DimensionProposalIssue> Issues { get; } = Array.AsReadOnly(issues);
}

/// <summary>Detached read-only plan. Original points, datum order and all source evidence are retained.</summary>
internal sealed class DimensionCompositionPlan
{
    public const string PolicyVersion = "preserve-proposals-v4";
    public IReadOnlyList<DimensionRuleResult> OriginalProposals { get; }
    public IReadOnlyList<DimensionCompositionDecision> Decisions { get; }
    public IReadOnlyList<DimensionProposalIssue> ViewIssues { get; }

    internal DimensionCompositionPlan(DimensionRuleResult[] proposals, DimensionCompositionDecision[] decisions,
        DimensionProposalIssue[] viewIssues)
    {
        OriginalProposals = Array.AsReadOnly(proposals);
        Decisions = Array.AsReadOnly(decisions);
        ViewIssues = Array.AsReadOnly(viewIssues);
    }

    public object Project(PartLayerSnapshot? layers = null)
    {
        var result = new Dictionary<string, object?> {
            ["policyVersion"] = PolicyVersion,
            ["policy"] = "preserve-proposals",
            ["scope"] = "view",
            ["contextId"] = OriginalProposals.FirstOrDefault()?.Proposal?.ContextId,
            ["viewId"] = OriginalProposals.FirstOrDefault()?.Proposal?.ViewId,
            ["viewIssues"] = ViewIssues.Select(issue => issue.Project()).ToArray(),
            ["proposals"] = OriginalProposals.Select(p => new {
                proposalId = p.Proposal!.ProposalId, previewKey = p.Proposal.PreviewKey,
                purpose = p.Proposal.Purpose.ToString(), reference = p.Proposal.Reference.ToString(),
                referenceSupport = p.Proposal.ReferenceSupport.ToString()
            }).ToArray(),
            ["decisions"] = Decisions.Select(d => new { proposalId = d.ProposalId,
                decision = d.Kind.ToString(), reason = d.Reason, issues = d.Issues.Select(issue => issue.Project()).ToArray() }).ToArray()
        };
        if (layers != null)
        {
            result["partClassification"] = layers.Project();
            result["pointPartBindings"] = layers.BindPoints(OriginalProposals.SelectMany(proposal => proposal.Points));
            result["proposalPointIds"] = OriginalProposals.Select(proposal => new {
                proposalId = proposal.Proposal!.ProposalId, pointIds = proposal.Points.Select(point => point.Id).ToArray()
            }).ToArray();
        }
        return result;
    }
}
