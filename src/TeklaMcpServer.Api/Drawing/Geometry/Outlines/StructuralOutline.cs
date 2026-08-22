using System.Collections.Generic;
using System.Linq;
using SolidContacts;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// The outline of the parts an assembly's structural geometry is measured over, and what
/// the selection did not know.
///
/// This is the pairing the extent actually needs. The outline of everything a view draws
/// can be a different number: on both walls checked it ran past the last chain point by
/// the thickness of an overhanging layer - 10 mm on one, 5 and 10 on the other. That is
/// what the caller's exclusion filter is for, and the reservation says whether the answer
/// can be trusted as a fact or only as a guess.
/// </summary>
public sealed class StructuralOutline
{
    public StructuralOutline(
        ViewAssemblyOutlineResult outline,
        IReadOnlyList<PartRoleInView> included,
        IReadOnlyList<PartRoleInView> excluded,
        IReadOnlyList<PartRoleInView> unclassified,
        IReadOnlyList<UnreadPart>? unreadRoles = null)
    {
        Outline = outline;
        Included = included;
        Excluded = excluded;
        Unclassified = unclassified;
        UnreadRoles = unreadRoles ?? Array.Empty<UnreadPart>();
    }

    public ViewAssemblyOutlineResult Outline { get; }

    /// <summary>The parts measured over.</summary>
    public IReadOnlyList<PartRoleInView> Included { get; }

    /// <summary>
    /// Parts the caller's exclusion filter took out. Reported rather than dropped
    /// silently: an extent that came out short is answered by looking here first.
    /// </summary>
    public IReadOnlyList<PartRoleInView> Excluded { get; }

    /// <summary>Parts that never reached the classifier.</summary>
    public IReadOnlyList<PartRoleInView> Unclassified { get; }

    /// <summary>
    /// Parts whose properties could not be read, so no role could even be attempted.
    ///
    /// Distinct from the two above, which are about rules. A part that vanishes here takes
    /// its extent with it, and the outline of what remains comes back clean and short.
    /// </summary>
    public IReadOnlyList<UnreadPart> UnreadRoles { get; }

    /// <summary>
    /// True only when every part was read, every requested part was drawn, and every
    /// solid was read. Anything less and the extent is a guess, however clean it looks.
    /// </summary>
    public bool IsComplete =>
        UnreadRoles.Count == 0 &&
        Unclassified.Count == 0 &&
        Included.Count > 0 &&
        Outline.IsComplete &&
        Outline.SelectionComplete;

    /// <summary>One line naming what was not known, or null when everything was.</summary>
    public string? Reservation()
    {
        var said = new List<string>();

        if (UnreadRoles.Count > 0)
            said.Add($"{UnreadRoles.Count} part(s) had no readable properties ({string.Join("; ", UnreadRoles.Take(5))})");

        if (Included.Count == 0)
            said.Add("every part in this view was excluded, so there is nothing to measure over");

        if (Unclassified.Count > 0)
            said.Add($"{Unclassified.Count} part(s) were never classified ({Marks(Unclassified)})");

        if (!Outline.SelectionComplete)
            said.Add($"{Outline.NotVisibleRequestedIds.Count} included part(s) are not drawn in this view");

        if (Outline.Unread.Count > 0)
            said.Add($"{Outline.Unread.Count} part(s) could not be read");

        if (Outline.Error != null)
            said.Add(Outline.Error);

        return said.Count == 0 ? null : string.Join("; ", said);
    }

    private static string Marks(IReadOnlyList<PartRoleInView> parts) =>
        string.Join(", ", parts.Take(5).Select(part => part.PartPos ?? part.ModelId.ToString()))
        + (parts.Count > 5 ? ", ..." : string.Empty);
}

/// <summary>Builds the outline of the parts a view draws, minus what the filter excludes.</summary>
public sealed class TeklaDrawingStructuralOutlineApi
{
    private readonly IDrawingPartRoleApi _roles;
    private readonly IDrawingViewOutlineApi _outline;

    public TeklaDrawingStructuralOutlineApi(IDrawingPartRoleApi roles, IDrawingViewOutlineApi outline)
    {
        _roles = roles;
        _outline = outline;
    }

    /// <summary>
    /// Reads every part the view draws, drops the ones the caller's filter excludes, and
    /// outlines what is left.
    ///
    /// The filter is applied on a properties-only read, so no solid is fetched for a part
    /// that is about to be discarded - on a wall that is every piece of insulation and
    /// every fixing. The outline then reads solids once, for what survived.
    ///
    /// The selection happens here rather than being passed in as ids so that a caller
    /// wanting the structural extent does not have to repeat the filter. Ids stay on the
    /// outline call for looking at things by hand.
    /// </summary>
    public StructuralOutline Get(
        int viewId,
        OutlineOptions? options = null,
        System.Action<IReadOnlyList<PartRoleInView>>? beforeOutlineRead = null)
    {
        var read = _roles.GetRolesInView(viewId);

        var included = read.Roles.Where(part => part.Role.Role == PartRole.Included).ToList();
        var excluded = read.Roles.Where(part => part.Role.Role == PartRole.Excluded).ToList();
        var unclassified = read.Roles.Where(part => !part.Role.IsClassified).ToList();

        var ids = included.Select(part => part.ModelId).Distinct().ToList();
        beforeOutlineRead?.Invoke(included);

        return new StructuralOutline(
            _outline.GetAssemblyOutline(viewId, options, ids),
            included,
            excluded,
            unclassified,
            read.Unread);
    }
}
