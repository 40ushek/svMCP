using System;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>Pure parsing of internal context requests; no geometry, cache or serialization.</summary>
internal sealed class DimensionContextQuery(string[] questions, string ruleSet, DimensionChainSide[] sides)
{
    internal string[] Questions { get; } = questions;
    internal string RuleSet { get; } = ruleSet;
    internal DimensionChainSide[] Sides { get; } = sides;

    internal static DimensionContextQuery Parse(string questions, string sides, string ruleSet)
    {
        var requested = Split(questions);
        var normalizedRuleSet = (ruleSet ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedRuleSet.Length == 0) normalizedRuleSet = "steel";
        if (normalizedRuleSet is not "steel" and not "panel")
            throw new ArgumentException("ruleSet must be 'steel' or 'panel'");
        var allowed = new[] { "points", "dimensionpoints", "chain", "chaindetails", "edges", "parts", "bolts", "boltchains", "scale", "placement", "contacts", "contactdetails", "diagnostics", "repeats", "all" };
        if (requested.Length == 0 || requested.Any(q => !allowed.Contains(q)))
            throw new ArgumentException("questions must contain points, dimensionPoints, chain, chainDetails, edges, parts, bolts, boltChains, scale, placement, contacts, contactDetails, diagnostics or all");
        return new DimensionContextQuery(requested, normalizedRuleSet, ParseSides(sides));
    }

    internal static string[] Split(string value) => value.Split(',').Select(s => s.Trim().ToLowerInvariant())
        .Where(s => s.Length > 0).Distinct().ToArray();

    internal static DimensionChainSide[] ParseSides(string sides)
    {
        var names = Split(sides);
        if (names.Length == 1 && names[0] == "all") return (DimensionChainSide[])Enum.GetValues(typeof(DimensionChainSide));
        if (names.Length == 0) throw new ArgumentException("sides must not be empty");
        return names.Select(n => Enum.TryParse<DimensionChainSide>(n, true, out var s)
            && Enum.IsDefined(typeof(DimensionChainSide), s) && !char.IsDigit(n[0])
                ? s : throw new ArgumentException("sides must contain Top, Bottom, Left, Right or all")).ToArray();
    }

}
