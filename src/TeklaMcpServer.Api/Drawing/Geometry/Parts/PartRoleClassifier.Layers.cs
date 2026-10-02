using System;
using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

public sealed partial class PartRoleClassifier
{
    public const string LayerPolicyVersion = "part-layer-diagnostics-v2";

    /// <summary>Independent of structural inclusion/exclusion. No default layer or plant prefix table.</summary>
    public static PartLayerResult ClassifyLayer(string? name, string? prefix, string? profile,
        IReadOnlyList<PartLayerRule>? rules = null, string? material = null)
    {
        var configured = ValidateLayerRules(rules);
        string? Value(PartLayerProperty property) => property switch {
            PartLayerProperty.Name => name, PartLayerProperty.Prefix => prefix,
            PartLayerProperty.Profile => profile, PartLayerProperty.Material => material, _ => null
        };
        var matched = new List<PartLayerRule>();
        var unresolved = new List<PartLayerRule>();
        foreach (var rule in configured)
        {
            // A known mismatch disproves the conjunction even if another field was not captured.
            if (rule.Conditions.Any(condition => Value(condition.Property) is { } value && !string.IsNullOrWhiteSpace(value)
                && !condition.Matches(value))) continue;
            if (rule.Conditions.Any(condition => string.IsNullOrWhiteSpace(Value(condition.Property)))) unresolved.Add(rule);
            else matched.Add(rule);
        }
        PartLayerResult Result(PartLayerStatus status, string? className, string reason, IEnumerable<PartLayerRule>? selected = null) =>
            new(status, className, reason, matched.Select(r => r.Id), (selected ?? Array.Empty<PartLayerRule>()).Select(r => r.Id), unresolved.Select(r => r.Id));
        var highest = matched.Count == 0 ? (int?)null : matched.Max(rule => rule.Priority);
        var uncertain = unresolved.Where(rule => !highest.HasValue || rule.Priority >= highest.Value).ToArray();
        if (uncertain.Length > 0)
            return Result(PartLayerStatus.Unknown, null, "Required properties are unavailable for potentially deciding rules: "
                + string.Join(", ", uncertain.Select(rule => rule.Id + " (" + string.Join(",",
                    rule.Conditions.Select(condition => condition.Property).Distinct().Where(property => string.IsNullOrWhiteSpace(Value(property)))
                        .OrderBy(property => property).Select(property => property.ToString())) + ")")) + ".");
        if (matched.Count == 0)
            return Result(PartLayerStatus.Unknown, null, configured.Count == 0 ? "No layer classification rules configured." : "No configured rule matched the captured properties.");
        var winners = matched.Where(rule => rule.Priority == highest).ToArray();
        var classes = winners.Select(rule => rule.ClassName).Distinct(StringComparer.Ordinal).OrderBy(c => c, StringComparer.Ordinal).ToArray();
        if (classes.Length > 1)
            return Result(PartLayerStatus.Conflict, null, "Equal-priority rules assign incompatible classes: " + string.Join(",", classes) + ".");
        return Result(PartLayerStatus.Classified, classes[0], "Classified by highest-priority matching rules: "
            + string.Join(",", winners.Select(r => r.Id).OrderBy(id => id, StringComparer.Ordinal)) + ".", winners);
    }

    internal static IReadOnlyList<PartLayerRule> ValidateLayerRules(IReadOnlyList<PartLayerRule>? rules)
    {
        var copy = (rules ?? Array.Empty<PartLayerRule>()).ToArray();
        if (copy.Any(rule => rule == null)) throw new ArgumentException("Layer rules cannot contain null entries.", nameof(rules));
        if (copy.Select(rule => rule.Id).Distinct(StringComparer.Ordinal).Count() != copy.Length)
            throw new ArgumentException("Layer rule IDs must be unique.", nameof(rules));
        return Array.AsReadOnly(copy.OrderBy(rule => rule.Id, StringComparer.Ordinal).ToArray());
    }
}
