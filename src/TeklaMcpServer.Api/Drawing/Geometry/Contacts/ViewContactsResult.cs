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
        IReadOnlyList<int>? requestedIds = null)
    {
        ViewId = viewId;
        Graph = graph;
        Unread = unread;
        Error = error;
        RequestedIds = requestedIds?.Distinct().ToArray() ?? Array.Empty<int>();
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
    /// True when every part the view draws was read and every pair searched. Only then
    /// does the absence of a contact mean anything.
    /// </summary>
    public bool IsComplete => Error == null && Unread.Count == 0 && Graph.Failures.Count == 0;

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
