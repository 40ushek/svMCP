namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// Whether a part takes part in the structural geometry of its assembly.
///
/// Two states, not the former four. `Defining`/`Attached`/`Ignored`/`Unknown` were decided
/// from mark prefixes written into this code, and a prefix is a plant's own convention: it
/// differs by office, by model and by language. The table answered one timber model and
/// turned every part of the next one into an `Unknown` that blocked the whole drawing -
/// nine parts of one steel column, all prefixed `P`, produced no structural geometry at
/// all.
///
/// What a part is FOR is now the business of the rule set the operator picks (see the
/// skill references). The code answers one narrower question: was this part excluded.
///
/// The main part of an assembly is a separate fact and deliberately not a role - see
/// <see cref="PartRoleInView.IsMainPart"/>. It carries the assembly on a beam or a column,
/// where everything else is fixed to one member, and carries nothing on a panel, where the
/// frame is many equal members.
/// </summary>
public enum PartRole
{
    /// <summary>
    /// Takes part in the structural geometry. The default for every part that is read:
    /// nothing is dropped unless the caller says to drop it.
    /// </summary>
    Included,

    /// <summary>
    /// Taken out by the caller's exclusion filter, which is the only way a part leaves the
    /// set. There is no rule in this code that excludes anything.
    /// </summary>
    Excluded,
}
