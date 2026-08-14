using System.Collections.Generic;
using System.Linq;
using SolidContacts;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// Contacts between the parts of a view, together with the parts that could not be read.
///
/// The second half is why this exists rather than a bare graph. A part whose geometry
/// failed simply would not appear among the bodies, and its absence looks exactly like a
/// part that touches nothing - so a graph on its own would report an assembly as fully
/// checked when part of it was never looked at. For dimensioning that is worse than an
/// error: a missing contact becomes a fact, and something downstream concludes there is
/// nothing there.
/// </summary>
public sealed class ViewContactsResult
{
    public ViewContactsResult(
        int viewId,
        ContactGraph graph,
        IReadOnlyList<UnreadPart> unread,
        string? error = null,
        IReadOnlyList<int>? requestedIds = null,
        bool restricted = false,
        IReadOnlyList<int>? notVisibleRequestedIds = null)
    {
        ViewId = viewId;
        Graph = graph;
        Unread = unread;
        Error = error;
        RequestedIds = requestedIds?.Distinct().ToArray() ?? Array.Empty<int>();
        Restricted = restricted;
        NotVisibleRequestedIds = notVisibleRequestedIds ?? Array.Empty<int>();
    }

    public int ViewId { get; }

    /// <summary>
    /// Why there was nothing to search, when that is the case: no drawing open, or no such
    /// view on it. Distinct from a view that was searched and holds nothing, which is a
    /// finding; this is the absence of a question, and it must not read as an answer.
    /// </summary>
    public string? Error { get; }

    public ContactGraph Graph { get; }

    /// <summary>Parts the view draws whose geometry did not come back.</summary>
    public IReadOnlyList<UnreadPart> Unread { get; }

    /// <summary>
    /// Every model part the view reader was asked to search, before any solid, bounding-box,
    /// or pair search could fail. This is deliberately not reconstructed from
    /// <see cref="Graph"/>: a body whose own box failed never reaches <see cref="ContactGraph.SolidIds"/>,
    /// but it was still part of the requested view and must remain visible to callers.
    /// </summary>
    public IReadOnlyList<int> RequestedIds { get; }

    /// <summary>
    /// Whether the caller narrowed the search to specific parts rather than everything the
    /// view draws. Recorded because a restricted search looks exactly like a small one: a
    /// "does A touch B" question and a genuinely quiet corner of the view both come back
    /// with few contacts, and a reader has to be able to tell which they asked for.
    /// </summary>
    public bool Restricted { get; }

    /// <summary>
    /// Ids the caller asked to search that this view does not draw - dropped before any
    /// solid was read, distinct from <see cref="Unread"/>, which is about geometry that was
    /// attempted and failed. A typo'd or stale id belongs here, not silently absent from
    /// <see cref="RequestedIds"/> with no trace of having been asked for.
    /// </summary>
    public IReadOnlyList<int> NotVisibleRequestedIds { get; }

    /// <summary>
    /// True when every part in the requested scope was read and every pair among them
    /// searched - the whole view when <see cref="Restricted"/> is false, only the named
    /// parts when it is true. Only then does the absence of a contact mean anything, and
    /// only about that scope: with a filter, a clean <see cref="IsComplete"/> proves the
    /// named parts do not touch each other, never that any of them touches nothing at all.
    /// A contact with a part outside the filter is not searched for, complete or not.
    /// </summary>
    public bool IsComplete => Error == null && Unread.Count == 0 && Graph.Failures.Count == 0;

    /// <summary>
    /// Whether every id the caller asked for made it into the search. Deliberately not
    /// folded into <see cref="IsComplete"/> - that one says the geometry actually searched
    /// was read in full, which is a different claim: asking for a typo'd or stale id and
    /// getting a clean, fully-read search of the other one back is a complete read of an
    /// incomplete selection, and reporting it as simply complete would hide the mistake.
    ///
    /// This is the field a caller must check before reading an empty result as "these parts
    /// do not touch" rather than "one of them was never in the search" - the distinction
    /// <see cref="ViewAssemblyOutlineResult.SelectionComplete"/> exists for on the outline
    /// side of this same problem.
    /// </summary>
    public bool SelectionComplete => NotVisibleRequestedIds.Count == 0;

    public override string ToString() =>
        Error != null
            ? $"view {ViewId}: {Error}  INCOMPLETE"
            : $"view {ViewId}: {Graph}, {Unread.Count} unread" + (IsComplete ? string.Empty : "  INCOMPLETE");
}

/// <summary>A part the view draws that the search could not take in, and why.</summary>
public sealed class UnreadPart
{
    public UnreadPart(int modelId, string reason)
    {
        ModelId = modelId;
        Reason = reason;
    }

    public int ModelId { get; }
    public string Reason { get; }

    public override string ToString() => $"{ModelId}: {Reason}";
}
