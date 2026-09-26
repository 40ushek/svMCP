using System;
using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>Chains plus diagnostics emitted once for one rule evaluation.</summary>
internal sealed class DimensionRuleEvaluation
{
    public IReadOnlyList<DimensionRuleResult> Results { get; }
    public IReadOnlyDictionary<string, object> Diagnostics { get; }

    public DimensionRuleEvaluation(IEnumerable<DimensionRuleResult> results,
        IReadOnlyDictionary<string, object>? diagnostics = null)
    {
        Results = Array.AsReadOnly(results.ToArray());
        var copy = new Dictionary<string, object>();
        if (diagnostics != null)
            foreach (var item in diagnostics)
                copy.Add(item.Key, item.Value);
        Diagnostics = copy;
    }
}
