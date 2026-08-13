using System.Collections.Generic;
using System.Linq;
using SolidContacts;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// The outline of the parts that fix an assembly's size, and what the selection did not
/// know.
///
/// This is the pairing the extent actually needs. The outline of everything a view draws
/// is a different number: on both walls checked it ran past the last chain point by the
/// thickness of an overhanging layer - 10 mm on one, 5 and 10 on the other. Asking for the
/// frame gives the frame, and the reservation says whether that answer can be trusted as a
/// fact or only as a guess.
/// </summary>
public sealed class StructuralOutline
{
    public StructuralOutline(
        ViewAssemblyOutlineResult outline,
        IReadOnlyList<PartRoleInView> defining,
        IReadOnlyList<PartRoleInView> unknown,
        IReadOnlyList<PartRoleInView> unclassified,
        IReadOnlyList<UnreadPart>? unreadRoles = null)
    {
        Outline = outline;
        Defining = defining;
        Unknown = unknown;
        Unclassified = unclassified;
        UnreadRoles = unreadRoles ?? Array.Empty<UnreadPart>();
    }

    public ViewAssemblyOutlineResult Outline { get; }

    /// <summary>The parts measured over.</summary>
    public IReadOnlyList<PartRoleInView> Defining { get; }

    /// <summary>Parts the classifier looked at and had no rule for.</summary>
    public IReadOnlyList<PartRoleInView> Unknown { get; }

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
    /// True only when every part had a role, every requested part was drawn, and every
    /// solid was read. Anything less and the extent is a guess, however clean it looks.
    /// </summary>
    public bool IsComplete =>
        UnreadRoles.Count == 0 &&
        Unknown.Count == 0 &&
        Unclassified.Count == 0 &&
        Defining.Count > 0 &&
        Outline.IsComplete &&
        Outline.SelectionComplete;

    /// <summary>One line naming what was not known, or null when everything was.</summary>
    public string? Reservation()
    {
        var said = new List<string>();

        if (UnreadRoles.Count > 0)
            said.Add($"{UnreadRoles.Count} part(s) had no readable properties ({string.Join("; ", UnreadRoles.Take(5))})");

        if (Defining.Count == 0)
            said.Add("no part in this view is classified as defining, so there is nothing to measure over");

        if (Unknown.Count > 0)
            said.Add($"{Unknown.Count} part(s) matched no role rule ({Marks(Unknown)})");

        if (Unclassified.Count > 0)
            said.Add($"{Unclassified.Count} part(s) were never classified ({Marks(Unclassified)})");

        if (!Outline.SelectionComplete)
            said.Add($"{Outline.NotVisibleRequestedIds.Count} defining part(s) are not drawn in this view");

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

/// <summary>Builds the outline of a view's defining parts.</summary>
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
    /// Reads what each part is for, keeps the ones that define the size, and outlines
    /// those.
    ///
    /// Roles come from a properties-only read, so no solid is fetched for a part that is
    /// about to be discarded - on a wall that is every piece of insulation and every
    /// fixing. The outline then reads solids once, for what survived.
    ///
    /// The selection happens here rather than being passed in as ids: a caller wanting the
    /// structural extent should not have to know which prefixes this plant uses. Ids stay
    /// on the outline call for looking at things by hand.
    /// </summary>
    public StructuralOutline Get(
        int viewId,
        OutlineOptions? options = null,
        System.Action<IReadOnlyList<PartRoleInView>>? beforeOutlineRead = null)
    {
        var read = _roles.GetRolesInView(viewId);

        var defining = read.Roles.Where(part => part.Role.Role == PartRole.Defining).ToList();
        var unknown = read.Roles.Where(part => part.Role.IsClassified && part.Role.Role == PartRole.Unknown).ToList();
        var unclassified = read.Roles.Where(part => !part.Role.IsClassified).ToList();

        var ids = defining.Select(part => part.ModelId).Distinct().ToList();
        beforeOutlineRead?.Invoke(defining);

        return new StructuralOutline(
            _outline.GetAssemblyOutline(viewId, options, ids),
            defining,
            unknown,
            unclassified,
            read.Unread);
    }
}
