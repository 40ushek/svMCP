namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// What a part is to the size of the assembly it belongs to.
///
/// Named for that relationship rather than for the material, so the same four cover
/// timber and steel: a stud and a stiffener both define, insulation and a splice plate
/// are both attached, fixings are ignored. Naming them after timber would have made the
/// first steel assembly a rewrite.
///
/// This narrows a question; it does not answer one. What a part is worth for a given job
/// is the job's business - see ROADMAP_PART_ROLES.md for the contract per consumer. In
/// particular a door built into an assembly defines no extent and is exactly what says an
/// opening is there, so a caller that keeps only <see cref="Defining"/> will be wrong
/// about openings.
/// </summary>
public enum PartRole
{
    /// <summary>
    /// Nobody said. Deliberately not <see cref="Ignored"/>: "takes no part" and "no rule
    /// matched" are different, and an extent computed over a set containing these is a
    /// guess rather than a fact.
    /// </summary>
    Unknown,

    /// <summary>Fixes the assembly's size and the positions inside it.</summary>
    Defining,

    /// <summary>Fastened to the assembly; does not fix its size.</summary>
    Attached,

    /// <summary>Takes no part in dimensions.</summary>
    Ignored,
}
