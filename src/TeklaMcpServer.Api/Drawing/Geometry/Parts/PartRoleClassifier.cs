using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// Decides what a part is to the size of its assembly, from its properties alone.
///
/// One interpretation in one place. The same judgement was written inline three times in
/// the defect detector and the three did not agree - two read the mark prefix, the third
/// read MATERIAL_TYPE, and that one counted insulation as timber and had genuine overall
/// dimensions reported as removable.
///
/// Properties only: no geometry, no view, no coordinates. That is what makes it a pure
/// function, testable on a list of marks with nothing running.
/// </summary>
public sealed class PartRoleClassifier
{
    /// <summary>
    /// The prefixes this plant uses, in order. Defaults live in code until a second model
    /// needs different ones; a file or a setting can be added then, when it is known who
    /// edits it. An override built before there is a second set of rules would be a layer
    /// with no consumer.
    /// </summary>
    public static IReadOnlyList<PartRoleRule> DefaultRules { get; } = new List<PartRoleRule>
    {
        new("prefix-T", "T", PartRole.Defining),

        // A glulam beam carries the frame like a stud does, so it sets the extent. Found by
        // measurement, not by reading a catalogue: on thirteen assembly drawings one
        // dimension point had no candidate under it, and the part beneath it was GL24h with
        // this prefix, unclassified and therefore left out of the structural set.
        new("prefix-GLB", "GLB", PartRole.Defining),

        new("prefix-R", "R", PartRole.Attached),

        // Sheathing is attached to the frame; it does not decide where the frame ends. Two
        // more of those thirteen drawings dimension a 15 mm OSB panel, and the temptation is
        // to call it Defining so those points appear. That would put the panel's overhang
        // back into the overall - the reason the structural extent starts at 210 and not the
        // 200 the whole-view outline reports. A dimension to a sheet is a fact about a
        // different semantic group, not a reason to widen this one.
        new("prefix-S", "S", PartRole.Attached),

        // No W rule. Three parts on two panels came back unclassified - W-65 and W-68 on
        // EW.8, W-64 on EW.18 - and all three were ContourPlate with material WINDOW. That
        // is evidence about three windows, not about the letter: the prefix is incidental to
        // what was actually observed, and a prefix rule asserts a role for every future W
        // part in every future assembly. Ignored is not a harmless guess either - a part
        // wrongly Ignored is one that silently stops setting the extent, which is the one
        // failure the completeness flag exists to prevent.
        //
        // What would earn the rule: read the properties of every W part across the model,
        // not one drawing, and show that none of them carries the frame. Until then these
        // parts stay Unknown, isComplete comes back false, and the caller is told to settle
        // the role rather than being answered from a guess.

        new("prefix-M", "M", PartRole.Ignored),
    }.AsReadOnly();

    private readonly IReadOnlyList<PartRoleRule> _rules;

    /// <summary>
    /// The rules are copied, not held by reference. A classifier whose table can be
    /// rewritten from outside after it is built would answer differently at different
    /// times for reasons no caller can see, and this is the one place in the dimension
    /// work where a silent change of mind is most expensive.
    /// </summary>
    public PartRoleClassifier(IReadOnlyList<PartRoleRule>? rules = null)
    {
        _rules = rules == null
            ? DefaultRules
            : Checked(rules);
    }

    private static IReadOnlyList<PartRoleRule> Checked(IReadOnlyList<PartRoleRule> rules)
    {
        var copy = rules.Select(Validated).ToList();

        // Two rules on one prefix are refused rather than ordered. Matching is on the
        // prefix and nothing else, so the second could never fire whatever the order: it
        // is dead configuration, and letting it in would hide a mistake behind a rule
        // about precedence. When rules grow a condition beyond the prefix, overlapping
        // entries become meaningful and this is where that changes.
        var duplicate = copy
            .GroupBy(rule => rule.Prefix, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(sharing => sharing.Count() > 1);

        if (duplicate != null)
        {
            throw new ArgumentException(
                $"Prefix '{duplicate.Key}' is claimed by more than one rule ({string.Join(", ", duplicate.Select(rule => rule.Id))}); " +
                "matching is on the prefix alone, so all but the first would never fire.",
                nameof(rules));
        }

        return copy.AsReadOnly();
    }

    private static PartRoleRule Validated(PartRoleRule rule, int index)
    {
        if (rule == null)
            throw new ArgumentException($"Rule {index} is null.", nameof(rule));

        if (string.IsNullOrWhiteSpace(rule.Id))
            throw new ArgumentException($"Rule {index} has no id; the id is what a report names when the rule matches.", nameof(rule));

        if (string.IsNullOrWhiteSpace(rule.Prefix))
            throw new ArgumentException($"Rule '{rule.Id}' has no prefix, so it would match nothing.", nameof(rule));

        return rule;
    }

    /// <summary>
    /// Whether a part that arrived without a role can safely be given one now.
    ///
    /// Not a limit on the classifier: asked directly, it answers an honest
    /// <see cref="PartRole.Unknown"/> for a part with no prefix, marked as classified,
    /// because the live reader looked and there was nothing there.
    ///
    /// The question here is different. On a snapshot - a captured state, a hand-built
    /// fixture, a geometry-only copy - an absent prefix does not mean the part has none.
    /// It usually means nobody read it. Classifying anyway turns "the properties were never
    /// read" into "no rule covers it", and those want different fixes.
    ///
    /// It lives beside the rules so it cannot drift from them: today they read the prefix
    /// and nothing else, and when they read more, the evidence a snapshot must carry
    /// changes with them.
    /// </summary>
    public bool CanReclassifyFromSnapshot(PartInView part) =>
        part != null && !string.IsNullOrWhiteSpace(part.PartPrefix);

    public PartRoleResult Classify(PartInView part)
    {
        if (part is null) throw new ArgumentNullException(nameof(part));

        return ClassifyProperties(part.PartPrefix, part.Profile, part.Material, part.MaterialType, part.Name);
    }

    /// <summary>
    /// Matched on the prefix and nothing else, and prefixes are unique, so at most one rule
    /// can apply. Order is kept because rules will eventually carry conditions beyond the
    /// prefix and then it will decide; today it cannot be observed.
    ///
    /// Named apart from the <see cref="PartInView"/> overload rather than sharing a name:
    /// with both called Classify, Classify(null) silently bound to the other one and threw
    /// instead of returning Unknown. A caller passing a prefix it does not have is the
    /// ordinary case here, so the trap would have been sprung often.
    /// </summary>
    public PartRoleResult ClassifyProperties(
        string? partPrefix,
        string? profile = null,
        string? material = null,
        int materialType = -1,
        string? name = null)
    {
        var known = Describe(partPrefix, profile, material, materialType, name);

        foreach (var rule in _rules)
        {
            if (rule.Matches(partPrefix))
                return new PartRoleResult(rule.Role, rule.Id, known);
        }

        // No guess. MATERIAL_TYPE could be consulted here and deliberately is not: it is
        // already known to report 5 for insulation, the same as timber, so as a rule of
        // last resort it would quietly become the deciding one on every unfamiliar prefix
        // - which is exactly how the structural extent went wrong. This classifier does
        // not use it as a rule; it only reports it.
        return new PartRoleResult(PartRole.Unknown, "none", known);
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

/// <summary>One rule: a mark prefix and the role it means.</summary>
public sealed class PartRoleRule
{
    public PartRoleRule(string id, string prefix, PartRole role)
    {
        Id = id;
        Prefix = prefix;
        Role = role;
    }

    public string Id { get; }
    public string Prefix { get; }
    public PartRole Role { get; }

    /// <summary>
    /// Exact, case-insensitive. Patterns would buy flexibility nothing needs yet and cost
    /// legibility straight away: "T" is the letters before the dash in "T-766", and a rule
    /// that matches it should say so plainly.
    /// </summary>
    public bool Matches(string? partPrefix) =>
        !string.IsNullOrWhiteSpace(partPrefix) &&
        string.Equals(partPrefix, Prefix, StringComparison.OrdinalIgnoreCase);
}
