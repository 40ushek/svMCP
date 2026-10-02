using System;
using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

internal enum DimensionCompositionDecisionKind { KeepSeparate, Blocked, Combined }

internal sealed class DimensionProposalIssue
{
    public string Code { get; }
    public string Reason { get; }
    public IReadOnlyList<int> ModelIds { get; }
    public IReadOnlyList<string> PointIds { get; }
    public IReadOnlyList<string> ProposalIds { get; }

    public DimensionProposalIssue(string code, string reason, IEnumerable<int>? modelIds = null,
        IEnumerable<string>? pointIds = null, IEnumerable<string>? proposalIds = null)
    {
        Code = code;
        Reason = reason;
        ModelIds = Array.AsReadOnly((modelIds ?? Array.Empty<int>()).Distinct().OrderBy(id => id).ToArray());
        PointIds = Array.AsReadOnly((pointIds ?? Array.Empty<string>()).Distinct().OrderBy(id => id, StringComparer.Ordinal).ToArray());
        ProposalIds = Array.AsReadOnly((proposalIds ?? Array.Empty<string>()).Distinct().OrderBy(id => id, StringComparer.Ordinal).ToArray());
    }

    public object Project()
    {
        var result = new Dictionary<string, object> { ["code"] = Code, ["reason"] = Reason, ["modelIds"] = ModelIds, ["pointIds"] = PointIds };
        if (ProposalIds.Count > 0) result["proposalIds"] = ProposalIds;
        return result;
    }
}

internal sealed class DimensionCompositionDecision(string proposalId,
    DimensionCompositionDecisionKind kind, string reason, DimensionProposalIssue[] issues, string? chainId = null)
{
    public string ProposalId { get; } = proposalId;
    public DimensionCompositionDecisionKind Kind { get; } = kind;
    public string Reason { get; } = reason;
    public IReadOnlyList<DimensionProposalIssue> Issues { get; } = Array.AsReadOnly(issues);
    public string? ChainId { get; } = chainId;
}

internal sealed class DimensionComposedChain(string chainId, DimensionRuleResult[] proposals, DimensionRulePoint[] points,
    double[] segments)
{
    public string ChainId { get; } = chainId;
    public IReadOnlyList<DimensionRuleResult> Proposals { get; } = Array.AsReadOnly(proposals);
    public IReadOnlyList<DimensionRulePoint> Points { get; } = Array.AsReadOnly(points);
    public IReadOnlyList<double> Segments { get; } = Array.AsReadOnly(segments);

    public object Project() => new { chainId = ChainId, sourceProposalIds = Proposals.Select(p => p.Proposal!.ProposalId).ToArray(),
        datumPointId = Points[0].Id, pointIds = Points.Select(p => p.Id).ToArray(), segments = Segments,
        stage = "composition-preview" };
}

/// <summary>Detached read-only plan. Original points, datum order and all source evidence are retained.</summary>
internal sealed class DimensionCompositionPlan
{
    public const string PolicyVersion = "shared-datum-union-v2";
    public IReadOnlyList<DimensionRuleResult> OriginalProposals { get; }
    public IReadOnlyList<DimensionCompositionDecision> Decisions { get; }
    public IReadOnlyList<DimensionProposalIssue> ViewIssues { get; }
    public IReadOnlyList<DimensionComposedChain> Chains { get; }
    public DimensionCoordinateSettings CoordinateSettings { get; }

    internal DimensionCompositionPlan(DimensionRuleResult[] proposals, DimensionCompositionDecision[] decisions,
        DimensionProposalIssue[] viewIssues, DimensionComposedChain[]? chains = null, DimensionCoordinateSettings? coordinateSettings = null)
    {
        OriginalProposals = Array.AsReadOnly(proposals);
        Decisions = Array.AsReadOnly(decisions);
        ViewIssues = Array.AsReadOnly(viewIssues);
        Chains = Array.AsReadOnly(chains ?? Array.Empty<DimensionComposedChain>());
        CoordinateSettings = coordinateSettings ?? new DimensionCoordinateSettings();
    }

    public object Project(PartLayerSnapshot? layers = null)
    {
        var result = new Dictionary<string, object?> {
            ["policyVersion"] = PolicyVersion,
            ["policy"] = "shared-datum-union",
            ["coordinateToleranceViewUnits"] = CoordinateSettings.ToleranceMm,
            ["scope"] = "view",
            ["contextId"] = OriginalProposals.FirstOrDefault()?.Proposal?.ContextId,
            ["viewId"] = OriginalProposals.FirstOrDefault()?.Proposal?.ViewId,
            ["viewIssues"] = ViewIssues.Select(issue => issue.Project()).ToArray(),
            ["proposals"] = OriginalProposals.Select(ProjectProposal).ToArray(),
            ["decisions"] = Decisions.Select(d => new { proposalId = d.ProposalId,
                decision = d.Kind.ToString(), chainId = d.ChainId, reason = d.Reason, issues = d.Issues.Select(issue => issue.Project()).ToArray() }).ToArray()
        };
        if (Chains.Count > 0) result["chains"] = Chains.Select(chain => chain.Project()).ToArray();
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

    private static object ProjectProposal(DimensionRuleResult proposal)
    {
        var identity = proposal.Proposal!;
        var result = new Dictionary<string, object?> { ["proposalId"] = identity.ProposalId, ["previewKey"] = identity.PreviewKey,
            ["purpose"] = identity.Purpose.ToString(), ["reference"] = identity.Reference.ToString(),
            ["referenceSupport"] = identity.ReferenceSupport.ToString() };
        if (proposal.CompositionIntent != null) result["compositionIntent"] = proposal.CompositionIntent.Project();
        return result;
    }
}
