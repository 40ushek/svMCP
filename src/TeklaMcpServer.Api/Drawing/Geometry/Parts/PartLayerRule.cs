using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;

namespace TeklaMcpServer.Api.Drawing;

public enum PartLayerProperty { Name, Prefix, Profile, Material }
public enum PartLayerMatchKind { Equals, Contains, StartsWith }
public enum PartLayerStatus { Unknown, Classified, Conflict }

/// <summary>
/// One explicit, case-insensitive comparison against a captured property.
/// Contains is a literal substring search without word boundaries: "B" matches both BEAM and BATTEN.
/// Choose a sufficiently specific value or combine conditions; short values are not automatically rejected.
/// </summary>
public sealed class PartLayerCondition
{
    public PartLayerProperty Property { get; }
    public PartLayerMatchKind MatchKind { get; }
    public string Value { get; }

    public PartLayerCondition(PartLayerProperty property, string value, PartLayerMatchKind matchKind = PartLayerMatchKind.Equals)
    {
        if (!Enum.IsDefined(typeof(PartLayerProperty), property)) throw new ArgumentException("Unsupported classification property.", nameof(property));
        if (!Enum.IsDefined(typeof(PartLayerMatchKind), matchKind)) throw new ArgumentException("Unsupported classification match kind.", nameof(matchKind));
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A non-empty condition value is required.", nameof(value));
        Property = property;
        MatchKind = matchKind;
        Value = value;
    }

    internal bool Matches(string actual) => MatchKind switch {
        PartLayerMatchKind.Equals => string.Equals(actual, Value, StringComparison.OrdinalIgnoreCase),
        PartLayerMatchKind.Contains => actual.IndexOf(Value, StringComparison.OrdinalIgnoreCase) >= 0,
        PartLayerMatchKind.StartsWith => actual.StartsWith(Value, StringComparison.OrdinalIgnoreCase),
        _ => false
    };
}

/// <summary>
/// Project-supplied diagnostic rule. All explicit conditions must match, ignoring case.
/// Higher priority is explicit precedence. Additional JSON data is preserved, never interpreted.
/// </summary>
public sealed class PartLayerRule
{
    public string Id { get; }
    public string ClassName { get; }
    public int Priority { get; }
    public IReadOnlyList<PartLayerCondition> Conditions { get; }
    public IReadOnlyDictionary<string, JsonElement> Data { get; }

    public PartLayerRule(string id, string className, IReadOnlyDictionary<PartLayerProperty, string> conditions,
        int priority = 0, IReadOnlyDictionary<string, JsonElement>? data = null)
        : this(id, className, (conditions ?? throw new ArgumentNullException(nameof(conditions)))
            .Select(pair => new PartLayerCondition(pair.Key, pair.Value)), priority, data) { }

    public PartLayerRule(string id, string className, IEnumerable<PartLayerCondition> conditions,
        int priority = 0, IReadOnlyDictionary<string, JsonElement>? data = null)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A classification rule ID is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(className)) throw new ArgumentException("A class name is required.", nameof(className));
        var captured = (conditions ?? throw new ArgumentNullException(nameof(conditions))).ToArray();
        if (captured.Length == 0)
            throw new ArgumentException("A classification rule needs at least one condition.", nameof(conditions));
        if (captured.Any(condition => condition == null))
            throw new ArgumentException("Conditions cannot contain null entries.", nameof(conditions));
        if (data != null && data.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Value.ValueKind == JsonValueKind.Undefined))
            throw new ArgumentException("Additional data requires named, defined JSON values.", nameof(data));
        Id = id;
        ClassName = className;
        Priority = priority;
        Conditions = Array.AsReadOnly(captured.OrderBy(condition => condition.Property).ThenBy(condition => condition.MatchKind)
            .ThenBy(condition => condition.Value, StringComparer.Ordinal).ToArray());
        Data = new ReadOnlyDictionary<string, JsonElement>((data ?? new Dictionary<string, JsonElement>())
            .ToDictionary(p => p.Key, p => p.Value.Clone(), StringComparer.Ordinal));
    }

    internal object Project() => new {
        ruleId = Id, className = ClassName, priority = Priority,
        conditions = Conditions.Select(condition => new { property = condition.Property.ToString(),
            matchKind = condition.MatchKind.ToString(), value = condition.Value }).ToArray(),
        data = Data.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(pair => pair.Key, pair => pair.Value)
    };
}

public sealed class PartLayerResult
{
    public PartLayerStatus Status { get; }
    public string? ClassName { get; }
    public string Reason { get; }
    public IReadOnlyList<string> MatchedRuleIds { get; }
    public IReadOnlyList<string> SelectedRuleIds { get; }
    public IReadOnlyList<string> UnresolvedRuleIds { get; }

    internal PartLayerResult(PartLayerStatus status, string? className, string reason,
        IEnumerable<string> matched, IEnumerable<string> selected, IEnumerable<string> unresolved)
    {
        Status = status;
        ClassName = className;
        Reason = reason;
        MatchedRuleIds = Array.AsReadOnly(matched.OrderBy(id => id, StringComparer.Ordinal).ToArray());
        SelectedRuleIds = Array.AsReadOnly(selected.OrderBy(id => id, StringComparer.Ordinal).ToArray());
        UnresolvedRuleIds = Array.AsReadOnly(unresolved.OrderBy(id => id, StringComparer.Ordinal).ToArray());
    }
}
