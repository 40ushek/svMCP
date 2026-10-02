using System;
using System.Linq;

namespace TeklaMcpServer.Shared;

internal static class DimensionPreviewQuestions
{
    public static string NormalizeRuleSet(string? ruleSet)
    {
        var value = (ruleSet ?? "steel").Trim().ToLowerInvariant();
        if (value is not "steel" and not "panel")
            throw new ArgumentException("ruleSet must be 'steel' or 'panel'");
        return value;
    }

    public static string NormalizeChainView(string? chainView)
    {
        var value = (chainView ?? "chain").Trim().ToLowerInvariant();
        if (value is not "chain" and not "chaindetails")
            throw new ArgumentException("chainView must be 'chain' or 'chainDetails'");
        return value;
    }

    public static string Normalize(string? questions)
    {
        if (questions == null) return "chain,scale";
        var allowed = new[] { "chain", "chaindetails", "parts", "scale", "diagnostics" };
        var requested = questions.Split(',').Select(q => q.Trim().ToLowerInvariant()).ToArray();
        if (requested.Length == 0 || requested.Any(q => !allowed.Contains(q)))
            throw new ArgumentException("questions allows only chain, chainDetails, parts, scale, diagnostics; source points and contacts are not exposed by get_view_dimension_context");
        return string.Join(",", requested.Distinct());
    }
}
