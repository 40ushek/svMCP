namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// A part's role, which rule said so, and what was known at the time.
///
/// The last two are not decoration. Without them <see cref="PartRole.Unknown"/> is
/// visible but useless: a part with no prefix, one with an unfamiliar prefix, and one
/// whose prefix no rule covers yet are three different situations wanting three different
/// fixes, and the report has to tell them apart.
///
/// It is where MATERIAL_TYPE appears, and the only place the classifier uses it: it is
/// already known to lie about the case that matters, since insulation reports 5, the same
/// as timber. Reporting it is not the same as the problem being over - the old inline
/// reading in DimensionDefectDetector.StructuralParts is still there until the consumers
/// move across.
///
/// IsMainPart is meant to join it, for the same reason: a fact from Tekla whose meaning
/// for dimensioning nobody has checked. The classifier does not receive it yet - see step
/// five of ROADMAP_PART_ROLES.md.
/// </summary>
public sealed class PartRoleResult
{
    public PartRoleResult(PartRole role, string ruleId, string reason)
        : this(role, ruleId, reason, isClassified: true)
    {
    }

    private PartRoleResult(PartRole role, string ruleId, string reason, bool isClassified)
    {
        Role = role;
        RuleId = ruleId;
        Reason = reason;
        IsClassified = isClassified;
    }

    /// <summary>
    /// For a part that was never put through the classifier - a hand-built test fixture, a
    /// captured state from before roles existed, a geometry-only copy that dropped the
    /// properties a role is derived from.
    ///
    /// Told apart from a part the classifier looked at and had no rule for by
    /// <see cref="IsClassified"/>, and by <see cref="RuleId"/> reading "unclassified"
    /// rather than "none". Both carry <see cref="PartRole.Unknown"/>, so neither the role
    /// nor the reason distinguishes them.
    /// </summary>
    public static PartRoleResult Unclassified { get; } =
        new(PartRole.Unknown, "unclassified", "not classified", isClassified: false);

    /// <summary>
    /// Whether the classifier looked at this part at all.
    ///
    /// A flag rather than a phrase in the reason. Both cases carry
    /// <see cref="PartRole.Unknown"/>, and the difference between "no rule covered it" and
    /// "nobody asked" changes what a caller should do - the first wants a rule, the second
    /// wants the part read properly. Leaving that in prose would have somebody comparing
    /// against the string sooner or later.
    /// </summary>
    public bool IsClassified { get; }

    public PartRole Role { get; }

    /// <summary>
    /// The rule that matched; "none" when the classifier ran and none did, and
    /// "unclassified" when it never ran. Prefer <see cref="IsClassified"/> for the second
    /// distinction - this is here so a report can name what happened, not so callers
    /// compare against it.
    /// </summary>
    public string RuleId { get; }

    /// <summary>What was known about the part when it was classified.</summary>
    public string Reason { get; }

    public override string ToString() => $"{Role} [{RuleId}] {Reason}";
}
