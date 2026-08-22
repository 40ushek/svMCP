using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// Decides whether a part takes part in the structural geometry of its assembly.
///
/// It ships with no rules at all, and that is the design. Everything a view draws is
/// <see cref="PartRole.Included"/> until the caller excludes it, because every signal a
/// rule could be written on - the mark prefix, the material name - is a plant's own
/// convention in the plant's own language. A prefix table baked in here served exactly one
/// timber model: on a steel column all nine parts carry prefix `P`, so no rule matched, no
/// part was defining, and the structural geometry came back empty.
///
/// Properties only: no geometry, no view, no coordinates. That is what makes it a pure
/// function, testable on a list of marks with nothing running.
/// </summary>
public sealed class PartRoleClassifier
{
    /// <summary>
    /// The empty set. It is the default because exclusions belong to a model, not to this
    /// code - see <see cref="PartExclusionRule"/>.
    /// </summary>
    public static IReadOnlyList<PartExclusionRule> NoExclusions { get; } =
        new List<PartExclusionRule>().AsReadOnly();

    private readonly IReadOnlyList<PartExclusionRule> _exclusions;

    /// <summary>
    /// The rules are copied, not held by reference. A classifier whose table can be
    /// rewritten from outside after it is built would answer differently at different
    /// moments for reasons nobody could see in a report.
    /// </summary>
    public PartRoleClassifier(IReadOnlyList<PartExclusionRule>? exclusions = null)
    {
        _exclusions = exclusions == null || exclusions.Count == 0
            ? NoExclusions
            : Checked(exclusions);
    }

    /// <summary>What this classifier takes out, in the order it is asked.</summary>
    public IReadOnlyList<PartExclusionRule> Exclusions => _exclusions;

    /// <summary>
    /// Whether any exclusion reads the mark prefix.
    ///
    /// A reader consults this before deciding that a failed property read matters. With no
    /// prefix exclusion the prefix decides nothing, and refusing the part because Tekla
    /// would not hand over a property nobody asked about is the blocker this redesign
    /// removed - it stopped a steel column whose prefixes were never going to be read.
    /// </summary>
    public bool NeedsPrefix => _exclusions.Any(rule => rule.Kind == PartExclusionKind.Prefix);

    /// <summary>
    /// Whether any exclusion reads the material name.
    ///
    /// The counterpart, and the one that must be fail-closed: a material read that quietly
    /// returned nothing would leave `excludeMaterials=WINDOW` matching nothing while the
    /// answer still called itself complete.
    /// </summary>
    public bool NeedsMaterial => _exclusions.Any(rule => rule.Kind == PartExclusionKind.Material);

    private static IReadOnlyList<PartExclusionRule> Checked(IReadOnlyList<PartExclusionRule> rules)
    {
        var copy = rules.Select(Validated).ToList();
        return copy.AsReadOnly();
    }

    private static PartExclusionRule Validated(PartExclusionRule rule, int index)
    {
        if (rule == null)
            throw new ArgumentException($"Exclusion {index} is null.", nameof(rule));

        if (string.IsNullOrWhiteSpace(rule.Value))
            throw new ArgumentException($"Exclusion '{rule.Id}' has no value, so it would match nothing.", nameof(rule));

        return rule;
    }

    /// <summary>
    /// Whether a part that arrived without a role can safely be given one now.
    ///
    /// With no exclusions the answer is always yes: every part is included, and that needs
    /// no property at all. It only turns to no when an exclusion reads a property this
    /// part does not carry - on a snapshot, a hand-built fixture or a geometry-only copy,
    /// an absent prefix usually means nobody read it, and including a part because its
    /// prefix was never read is the silent version of the mistake this whole area exists
    /// to prevent.
    /// </summary>
    public bool CanReclassifyFromSnapshot(PartInView part)
    {
        if (part == null)
            return false;

        if (NeedsPrefix && string.IsNullOrWhiteSpace(part.PartPrefix))
            return false;

        if (NeedsMaterial && string.IsNullOrWhiteSpace(part.Material))
            return false;

        return true;
    }

    public PartRoleResult Classify(PartInView part)
    {
        if (part is null) throw new ArgumentNullException(nameof(part));

        return ClassifyProperties(part.PartPrefix, part.Profile, part.Material, part.MaterialType, part.Name);
    }

    /// <summary>
    /// First matching exclusion wins; with none, the part is included.
    ///
    /// Named apart from the <see cref="PartInView"/> overload rather than sharing a name:
    /// with both called Classify, Classify(null) silently bound to the other one and threw
    /// instead of answering. A caller passing a prefix it does not have is the ordinary
    /// case here, so the trap would have been sprung often.
    /// </summary>
    public PartRoleResult ClassifyProperties(
        string? partPrefix,
        string? profile = null,
        string? material = null,
        int materialType = -1,
        string? name = null)
    {
        var known = Describe(partPrefix, profile, material, materialType, name);

        foreach (var rule in _exclusions)
        {
            if (rule.Matches(partPrefix, material))
                return new PartRoleResult(PartRole.Excluded, rule.Id, known);
        }

        // No guess and no last resort. MATERIAL_TYPE is deliberately not consulted even
        // here: it reports 5 for insulation, the same as timber, so as a tie-breaker it
        // would quietly become the deciding signal - which is exactly how the structural
        // extent went wrong once already. It is reported in the reason and used nowhere.
        return new PartRoleResult(PartRole.Included, "included", known);
    }

    private static string Describe(
        string? partPrefix, string? profile, string? material, int materialType, string? name)
    {
        var parts = new List<string>
        {
            $"prefix={Show(partPrefix)}"
        };

        if (!string.IsNullOrWhiteSpace(profile)) parts.Add($"profile={profile}");
        if (!string.IsNullOrWhiteSpace(material)) parts.Add($"material={material}");
        if (materialType >= 0) parts.Add($"materialType={materialType.ToString(CultureInfo.InvariantCulture)}");
        if (!string.IsNullOrWhiteSpace(name)) parts.Add($"name={name}");

        return string.Join(" ", parts);
    }

    private static string Show(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "<none>" : value!;
}

/// <summary>What property an exclusion reads.</summary>
public enum PartExclusionKind
{
    /// <summary>The mark prefix, matched whole and case-insensitively.</summary>
    Prefix,

    /// <summary>
    /// The material name, matched as a case-insensitive substring. A substring because
    /// material names are written by the plant in the plant's language and carry grades
    /// and suffixes: `WINDOW`, `Mineralwolle 040`, `C24-KVH`.
    /// </summary>
    Material,
}

/// <summary>
/// One exclusion: a property, a value, and the id a report names when it matches.
///
/// Exclusions belong to a model and are supplied by whoever runs the drawing, never
/// shipped in this code. Prefixes and material names differ between plants and between
/// languages, so a default here would be one office's convention imposed on everyone
/// else's model - which is what the previous prefix table turned out to be.
/// </summary>
public sealed class PartExclusionRule
{
    public PartExclusionRule(string id, PartExclusionKind kind, string value)
    {
        Id = id;
        Kind = kind;
        Value = value;
    }

    public string Id { get; }
    public PartExclusionKind Kind { get; }
    public string Value { get; }

    public static PartExclusionRule ByPrefix(string prefix) =>
        new("exclude-prefix:" + prefix, PartExclusionKind.Prefix, prefix);

    public static PartExclusionRule ByMaterial(string material) =>
        new("exclude-material:" + material, PartExclusionKind.Material, material);

    public bool Matches(string? partPrefix, string? material) => Kind switch
    {
        PartExclusionKind.Prefix =>
            !string.IsNullOrWhiteSpace(partPrefix) &&
            string.Equals(partPrefix, Value, StringComparison.OrdinalIgnoreCase),

        PartExclusionKind.Material =>
            !string.IsNullOrWhiteSpace(material) &&
            material!.IndexOf(Value, StringComparison.OrdinalIgnoreCase) >= 0,

        _ => false
    };

    public override string ToString() => Id;
}

/// <summary>
/// Reads an exclusion set from the two plain lists a caller can type: prefixes and
/// material names, comma-separated.
///
/// Deliberately not a filter language. The Tekla filter expressions this project already
/// parses answer a different question - which objects to select from a model - and running
/// one per part would drag a Tekla selection into a pure function. Two lists cover what
/// was actually asked for, and an empty string means exclude nothing.
/// </summary>
public static class PartExclusions
{
    public static IReadOnlyList<PartExclusionRule> Parse(string? prefixes, string? materials)
    {
        var rules = new List<PartExclusionRule>();

        foreach (var prefix in Split(prefixes))
            rules.Add(PartExclusionRule.ByPrefix(prefix));

        foreach (var material in Split(materials))
            rules.Add(PartExclusionRule.ByMaterial(material));

        return rules;
    }

    private static IEnumerable<string> Split(string? list) =>
        string.IsNullOrWhiteSpace(list)
            ? Enumerable.Empty<string>()
            : list!.Split(',')
                .Select(value => value.Trim())
                .Where(value => value.Length > 0);
}
