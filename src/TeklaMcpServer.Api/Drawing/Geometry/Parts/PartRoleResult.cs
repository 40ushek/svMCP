namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// A part's role, which rule said so, and what was known at the time.
///
/// The last two are not decoration. Without them an excluded part is visible but
/// unexplained: which of the caller's exclusions took it out, and on what property, is
/// what a person needs before deciding the filter was right.
///
/// It is where MATERIAL_TYPE appears, and the only place the classifier uses it: it is
/// already known to lie about the case that matters, since insulation reports 5, the same
/// as timber. Reporting it is not the same as using it as a rule: it remains diagnostic
/// evidence only.
///
/// IsMainPart deliberately does NOT live here. It is a fact about the assembly, not a
/// judgement about the part, and it decides nothing on its own: it is the base of
/// measurement for a beam or a column and means nothing on a panel. It sits on
/// <see cref="PartRoleInView.IsMainPart"/> where a consumer must ask for it on purpose.
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
    /// Told apart from a part the classifier looked at and kept by
    /// <see cref="IsClassified"/>, and by <see cref="RuleId"/> reading "unclassified"
    /// rather than "included". Both carry <see cref="PartRole.Included"/>, so neither the
    /// role nor the reason distinguishes them.
    /// </summary>
    public static PartRoleResult Unclassified { get; } =
        new(PartRole.Included, "unclassified", "not classified", isClassified: false);

    /// <summary>
    /// Whether the classifier looked at this part at all.
    ///
    /// A flag rather than a phrase in the reason. Both cases carry
    /// <see cref="PartRole.Included"/>, and the difference between "the filter kept it"
    /// and "nobody asked" changes what a caller should do - the second means the part's
    /// properties were never read, so an exclusion that should have caught it could not
    /// fire. Leaving that in prose would have somebody comparing against the string
    /// sooner or later.
    /// </summary>
    public bool IsClassified { get; }

    public PartRole Role { get; }

    /// <summary>
    /// The exclusion that matched; "included" when the classifier ran and none did, and
    /// "unclassified" when it never ran. Prefer <see cref="IsClassified"/> for the second
    /// distinction - this is here so a report can name what happened, not so callers
    /// compare against it.
    /// </summary>
    public string RuleId { get; }

    /// <summary>What was known about the part when it was classified.</summary>
    public string Reason { get; }

    public override string ToString() => $"{Role} [{RuleId}] {Reason}";
}
