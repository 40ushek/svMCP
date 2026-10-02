using System;
using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>A proposed chain, or a reason why the rule could not provide one.</summary>
internal sealed class DimensionRuleResult
{
    public DimensionDirection Direction { get; }
    public DimensionLinePlacement Placement { get; }
    public string Kind { get; }
    public IReadOnlyList<DimensionRulePoint> Points { get; }
    public string? Note { get; }
    public IReadOnlyList<double> Segments { get; }
    public IReadOnlyDictionary<string, object> Evidence { get; }
    public DimensionProposalIdentity? Proposal { get; }

    public DimensionRuleResult(DimensionDirection direction, DimensionLinePlacement placement, string kind,
        IEnumerable<DimensionRulePoint> points, string? note = null, IEnumerable<double>? segments = null,
        IReadOnlyDictionary<string, object>? evidence = null, DimensionProposalIdentity? proposal = null)
    {
        Direction = direction ?? throw new ArgumentNullException(nameof(direction));
        Placement = placement ?? throw new ArgumentNullException(nameof(placement));
        Kind = kind;
        Points = Array.AsReadOnly(points.ToArray());
        Segments = Array.AsReadOnly((segments ?? Array.Empty<double>()).ToArray());
        Note = note;
        Proposal = proposal;
        var copy = new Dictionary<string, object>();
        if (evidence != null)
            foreach (var item in evidence)
                copy.Add(item.Key, item.Value);
        Evidence = copy;
    }

    public DimensionRuleResult WithProposal(DimensionProposalIdentity proposal) =>
        new(Direction, Placement, Kind, Points, Note, Segments, Evidence, proposal);
}
