using System;
using System.Collections.Generic;
using System.Globalization;
using SolidContacts;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// Which two bodies a piece of contact geometry belongs to.
///
/// The bodies are named twice on purpose. The contact search knows them by the ids it was
/// given, which are always there; the model ids are those read back as numbers, and that
/// can fail. A body whose id will not parse is still a body, and its geometry is still
/// worth reporting - what must not happen is inventing a part to hang it on.
/// </summary>
public sealed class ContactParticipants
{
    public ContactParticipants(string solidAId, string solidBId)
    {
        SolidAId = solidAId;
        SolidBId = solidBId;
        ModelObjectIdA = AsModelId(solidAId);
        ModelObjectIdB = AsModelId(solidBId);
    }

    /// <summary>The ids the search worked with. Always present.</summary>
    public string SolidAId { get; }
    public string SolidBId { get; }

    /// <summary>
    /// The model objects, where the ids resolve to them. Null means unresolved - not part
    /// zero, which is a real answer about a part that does not exist.
    /// </summary>
    public int? ModelObjectIdA { get; }
    public int? ModelObjectIdB { get; }

    /// <summary>True when both bodies are known model objects.</summary>
    public bool Resolved => ModelObjectIdA.HasValue && ModelObjectIdB.HasValue;

    /// <summary>
    /// Both model objects, or neither. Never one.
    ///
    /// Half an answer would be worse than none here. A contact belongs to two parts the
    /// same way, and a list holding only the resolved one reads as a complete ownership
    /// claim: something downstream would take the touching surface for a feature of part A
    /// alone, and the part it actually meets would have vanished without a trace. When only
    /// one id resolves, the pair is unconfirmed - see <see cref="SolidAId"/> and
    /// <see cref="SolidBId"/> for what was there.
    /// </summary>
    public IReadOnlyList<int> ModelObjectIds =>
        Resolved
            ? new List<int> { ModelObjectIdA!.Value, ModelObjectIdB!.Value }
            : new List<int>();

    private static int? AsModelId(string solidId) =>
        int.TryParse(solidId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            ? id
            : (int?)null;

    public override string ToString() =>
        Describe(ModelObjectIdA, SolidAId) + "-" + Describe(ModelObjectIdB, SolidBId);

    private static string Describe(int? modelId, string solidId) =>
        modelId.HasValue ? modelId.Value.ToString(CultureInfo.InvariantCulture) : "?" + solidId;
}

/// <summary>
/// One contact region as it reads on the sheet: the same touching surface the contact
/// search found, with the depth taken out and the vertices that say nothing removed.
///
/// A region rather than a contact. One contact can touch in several separate places - a
/// plate crossing two studs is a single junction with two patches - and merging them would
/// invent a surface between the studs that nothing occupies.
/// </summary>
public sealed class ContactShapeInView
{
    public ContactShapeInView(
        string contactId,
        ContactParticipants participants,
        ContactKind kind,
        PlanarShape shape)
    {
        ContactId = contactId;
        Participants = participants;
        Kind = kind;
        Shape = shape;
    }

    /// <summary>The contact this region belongs to. Stable across runs, not across edits.</summary>
    public string ContactId { get; }

    /// <summary>
    /// The flat shape, named by where its points are.
    ///
    /// It names the shape on the sheet, not the region that cast it, and the difference is
    /// real. Two separate regions of one contact are coplanar but can sit apart in depth;
    /// seen along that plane they land on the same line and share this name. That is the
    /// right answer for a drawing - the two really are one place there - but it means this
    /// must not be read as "which region", and two shapes of a contact carrying the same
    /// id is a finding rather than a fault.
    /// </summary>
    public string ShapeId => Shape.Id;

    /// <summary>
    /// Both parts. A touching surface belongs to two of them equally; naming one would make
    /// the other face a coincidence.
    /// </summary>
    public ContactParticipants Participants { get; }

    public ContactKind Kind { get; }

    /// <summary>
    /// What the region looks like on the sheet - an area, a line, or a single place. The
    /// three are genuinely different: a face contact between stacked members is a line on
    /// an elevation, because the view looks along it.
    /// </summary>
    public PlanarShape Shape { get; }

    /// <summary>
    /// A name for one place on the sheet, for something later to point at.
    ///
    /// All three parts are needed. The contact says which junction, the shape says where on
    /// the sheet, and the index says which of its points - and the index only means
    /// anything because <see cref="PlanarShape.Points"/> is in canonical order.
    ///
    /// A place, not a region, for the reason given on <see cref="ShapeId"/>: two regions
    /// that read as one line on the sheet will give the same key for the same end of it,
    /// which is what a dimension should see.
    ///
    /// The contact id contains separators of its own, so the key does not split into three
    /// from the left. It is still unambiguous, and that is the property that matters: the
    /// last two fields are a fixed-width hash and a run of digits, neither of which can
    /// hold a separator, so reading from the right recovers the three parts exactly. Two
    /// different triples cannot write the same key.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// There is no such point. An index outside the shape would produce a key that looks
    /// exactly like a working one and refers to nothing, and the mistake would only be
    /// noticed by whatever tried to place a dimension at it.
    /// </exception>
    public string AnchorKey(int pointIndex)
    {
        if (pointIndex < 0 || pointIndex >= Shape.Points.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pointIndex), pointIndex,
                "This shape has " + Shape.Points.Count.ToString(CultureInfo.InvariantCulture) + " point(s).");
        }

        return ContactId + "|" + ShapeId + "|" + pointIndex.ToString(CultureInfo.InvariantCulture);
    }

    public override string ToString() => Participants + " " + Kind + " " + Shape;
}

/// <summary>
/// A contact region that came back with nothing on the sheet, and which contact it was.
///
/// Reported rather than dropped, for the reason the unread parts are: a region that
/// vanishes leaves a junction looking thinner than it is, and nothing downstream can tell
/// that from a junction that really is that thin.
/// </summary>
public sealed class UnflattenedRegion
{
    public UnflattenedRegion(string contactId, int regionIndex, ContactParticipants participants, string reason)
    {
        ContactId = contactId;
        RegionIndex = regionIndex;
        Participants = participants;
        Reason = reason;
    }

    public string ContactId { get; }

    /// <summary>
    /// Where the lost region sat in the list the contact holds. Position within this one
    /// answer and nothing more - the flat shape that would have named it properly is
    /// exactly what went missing.
    /// </summary>
    public int RegionIndex { get; }

    public ContactParticipants Participants { get; }
    public string Reason { get; }

    public override string ToString() =>
        Participants + " region " + RegionIndex.ToString(CultureInfo.InvariantCulture) + ": " + Reason;
}

/// <summary>
/// The contact geometry of one view, flattened onto its sheet, together with everything
/// that did not make it there.
///
/// The completeness of the search it came from is carried through unchanged. Flattening
/// cannot recover a part that was never read, so a result built on an incomplete search is
/// incomplete however well every region flattened.
/// </summary>
public sealed class ViewContactGeometryResult
{
    public ViewContactGeometryResult(
        int viewId,
        IReadOnlyList<ContactShapeInView> shapes,
        IReadOnlyList<UnflattenedRegion> unflattened,
        IReadOnlyList<UnreadPart> unread,
        bool searchComplete,
        string? error = null)
    {
        ViewId = viewId;
        Shapes = shapes;
        Unflattened = unflattened;
        Unread = unread;
        SearchComplete = searchComplete;
        Error = error;
    }

    public int ViewId { get; }

    /// <summary>Why there was nothing to flatten, when that is the case.</summary>
    public string? Error { get; }

    public IReadOnlyList<ContactShapeInView> Shapes { get; }

    /// <summary>Regions that left nothing on the sheet.</summary>
    public IReadOnlyList<UnflattenedRegion> Unflattened { get; }

    /// <summary>Parts the view draws whose geometry never reached the search.</summary>
    public IReadOnlyList<UnreadPart> Unread { get; }

    /// <summary>Whether the contact search underneath saw the whole view.</summary>
    public bool SearchComplete { get; }

    /// <summary>Shapes whose two bodies could not both be named as model objects.</summary>
    public IReadOnlyList<ContactShapeInView> Unresolved
    {
        get
        {
            var unresolved = new List<ContactShapeInView>();
            foreach (var shape in Shapes)
            {
                if (!shape.Participants.Resolved)
                    unresolved.Add(shape);
            }

            return unresolved;
        }
    }

    /// <summary>
    /// True when the whole view was searched, every region it found came through, and every
    /// shape knows both of its parts. Only then does the absence of contact geometry
    /// somewhere mean that nothing is there.
    /// </summary>
    public bool IsComplete =>
        Error == null && SearchComplete && Unflattened.Count == 0 && Unresolved.Count == 0;

    public override string ToString() =>
        Error != null
            ? "view " + ViewId + ": " + Error + "  INCOMPLETE"
            : "view " + ViewId + ": " + Shapes.Count + " shape(s), " + Unflattened.Count
              + " lost, " + Unread.Count + " unread" + (IsComplete ? string.Empty : "  INCOMPLETE");
}
