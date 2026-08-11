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
    {
        Role = role;
        RuleId = ruleId;
        Reason = reason;
    }

    public PartRole Role { get; }

    /// <summary>The rule that matched, or "none" when nothing did.</summary>
    public string RuleId { get; }

    /// <summary>What was known about the part when it was classified.</summary>
    public string Reason { get; }

    public override string ToString() => $"{Role} [{RuleId}] {Reason}";
}
