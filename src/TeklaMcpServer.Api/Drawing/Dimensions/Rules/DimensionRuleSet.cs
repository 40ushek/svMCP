using System;
using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>Executes configured rules in order without writing dimensions.</summary>
internal sealed class DimensionRuleSet(params IDimensionRule[] rules)
{
    private readonly IDimensionRule[] _rules = rules.ToArray();

    public DimensionRuleEvaluation Calculate(DimensionRuleContext context)
    {
        var evaluations = _rules.Select(rule => rule.Calculate(context)).ToArray();
        var diagnostics = new Dictionary<string, object>();
        foreach (var item in evaluations.SelectMany(evaluation => evaluation.Diagnostics))
        {
            if (diagnostics.ContainsKey(item.Key))
                throw new InvalidOperationException($"More than one dimension rule emitted diagnostic '{item.Key}'.");
            diagnostics.Add(item.Key, item.Value);
        }
        return new DimensionRuleEvaluation(evaluations.SelectMany(evaluation => evaluation.Results), diagnostics);
    }
}
