using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// The parts that fix an assembly's size, and what the selection did not know.
///
/// Both halves matter. An extent measured over a set containing parts nobody could
/// classify is a guess, and the caller has to be able to say so: including them puts
/// insulation back in the extent, which is the fault this exists to fix, and dropping them
/// silently shortens the assembly, which is worse, because a short overall looks exactly
/// like a correct one.
///
/// Third result in this work with the same shape, after the contact graph and the assembly
/// outline. A result derived from a set says what was missing from the set.
/// </summary>
public sealed class DefiningParts
{
    private DefiningParts(
        IReadOnlyList<PartInView> parts,
        IReadOnlyList<PartInView> unknown,
        IReadOnlyList<PartInView> unclassified,
        bool fellBackToEverything)
    {
        Parts = parts;
        Unknown = unknown;
        Unclassified = unclassified;
        FellBackToEverything = fellBackToEverything;
    }

    /// <summary>The parts to measure over.</summary>
    public IReadOnlyList<PartInView> Parts { get; }

    /// <summary>Parts the classifier looked at and had no rule for. These want a rule.</summary>
    public IReadOnlyList<PartInView> Unknown { get; }

    /// <summary>Parts that never reached the classifier. These want reading properly.</summary>
    public IReadOnlyList<PartInView> Unclassified { get; }

    /// <summary>
    /// True when nothing was classified as defining and every part was taken instead.
    ///
    /// The fallback is deliberate: an extent of zero would switch the overall test off
    /// without a word, which is the failure mode this whole area keeps producing. But
    /// falling back is not the same as knowing, and a caller that reports an extent taken
    /// this way should say where it came from.
    /// </summary>
    public bool FellBackToEverything { get; }

    /// <summary>Whether every part had a role and the extent can be stated as a fact.</summary>
    public bool IsComplete => Unknown.Count == 0 && Unclassified.Count == 0 && !FellBackToEverything;

    public static DefiningParts From(IReadOnlyList<PartInView> parts)
    {
        var defining = parts.Where(part => part.Role.Role == PartRole.Defining).ToList();
        var unknown = parts.Where(part => part.Role.IsClassified && part.Role.Role == PartRole.Unknown).ToList();
        var unclassified = parts.Where(part => !part.Role.IsClassified).ToList();

        return defining.Count > 0
            ? new DefiningParts(defining, unknown, unclassified, fellBackToEverything: false)
            : new DefiningParts(parts, unknown, unclassified, fellBackToEverything: true);
    }

    /// <summary>One line naming what was not known, or null when everything was.</summary>
    public string? Reservation()
    {
        if (IsComplete)
            return null;

        var said = new List<string>();

        if (FellBackToEverything)
            said.Add("no part is classified as defining, so the extent is over every part");

        if (Unknown.Count > 0)
            said.Add($"{Unknown.Count} part(s) matched no role rule ({Marks(Unknown)})");

        if (Unclassified.Count > 0)
            said.Add($"{Unclassified.Count} part(s) were never classified ({Marks(Unclassified)})");

        return string.Join("; ", said);
    }

    private static string Marks(IReadOnlyList<PartInView> parts) =>
        string.Join(", ", parts.Take(5).Select(part => part.PartPos ?? part.ModelId.ToString()))
        + (parts.Count > 5 ? ", ..." : string.Empty);
}
