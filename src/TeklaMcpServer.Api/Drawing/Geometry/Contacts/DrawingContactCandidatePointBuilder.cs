using System.Collections.Generic;
using System.Globalization;
using SolidContacts;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// Candidate dimension anchors taken from where the parts of a view touch each other.
///
/// Its own layer, deliberately not mixed with the contour candidates or the older part
/// points. A contour corner says where a part ends; a contact point says where two parts
/// meet, and those are different claims about the drawing even when they land on the same
/// millimetre. Pooling them would lose which claim was made, and the difference is exactly
/// what a later rule has to weigh.
///
/// Nothing here places a dimension or ranks anything. It only turns geometry that has
/// already been found into places that can be named.
/// </summary>
public static class DrawingContactCandidatePointBuilder
{
    /// <summary>
    /// Every point of every flattened contact shape, once each.
    ///
    /// Whatever the shape turned out to be: a polygon gives its corners, a line its two
    /// ends, a single place itself. No filtering by size or kind - a contact seen end-on is
    /// one place and that place is as real as the corners of one seen flat.
    ///
    /// Once each, because two regions of one contact can land on the same shape when the
    /// view looks along their plane, and the sheet then shows one line where the model has
    /// two patches. Both regions are kept as they are in the geometry - they are two real
    /// surfaces - but a place on the drawing is a place, and offering it twice would let
    /// something downstream count one location as two pieces of evidence.
    ///
    /// Nothing is lost by dropping the repeat: the shapes that collide belong to the same
    /// contact, so they name the same two parts and their candidates are identical. Two
    /// patches of a plate at different studs are different contacts, keep different shapes
    /// and different ids, and both survive.
    /// </summary>
    public static ViewContactCandidatePointsResult Build(ViewContactGeometryResult geometry)
    {
        if (geometry == null)
        {
            return new ViewContactCandidatePointsResult(
                0, new List<DrawingPartCandidatePoint>(), new List<UnflattenedRegion>(),
                new List<ContactShapeInView>(), new List<UnreadPart>(),
                searchComplete: false, error: "no contact geometry to read");
        }

        var points = new List<DrawingPartCandidatePoint>();
        var placed = new HashSet<string>();

        foreach (var shape in geometry.Shapes)
        {
            // A shape whose two parts are not both named is reported rather than turned into
            // candidates. The geometry is real, but a contact candidate says "these two
            // parts meet here", and that sentence cannot be finished.
            if (!shape.Participants.Resolved)
                continue;

            for (var index = 0; index < shape.Shape.Points.Count; index++)
            {
                var candidate = Candidate(shape, index);

                if (placed.Add(candidate.Anchor.Key))
                    points.Add(candidate);
            }
        }

        return new ViewContactCandidatePointsResult(
            geometry.ViewId, points, geometry.Unflattened, geometry.Unresolved,
            geometry.Unread, geometry.SearchComplete, geometry.Error);
    }

    private static DrawingPartCandidatePoint Candidate(ContactShapeInView shape, int pointIndex)
    {
        var point = shape.Shape.Points[pointIndex];

        return new DrawingPartCandidatePoint
        {
            // Both parts, always. The place exists because they meet; either one alone
            // would turn a junction into a feature of a single part.
            ModelObjectIds = new List<int>(shape.Participants.ModelObjectIds),
            Point = new[] { point.X, point.Y },

            Source = DrawingPartCandidatePointSource.Contact,

            // Derived, not exact. The contact was computed from two solids at a tolerance
            // and then flattened; no vertex of either part need sit here.
            Confidence = DrawingPartCandidateConfidence.DerivedGeometry,

            Anchor = new DrawingPartCandidateAnchor
            {
                // No single owner, by the nature of the thing - see the anchor kind.
                ModelObjectId = null,
                Kind = DrawingPartCandidateAnchorKind.ContactPoint,
                Id = shape.AnchorKey(pointIndex)
            },

            Reason = new DrawingPartCandidateReason
            {
                Code = "contact_shape_point",
                ModelObjectIds = new List<int>(shape.Participants.ModelObjectIds),
                Values = new Dictionary<string, string>
                {
                    ["contactKind"] = shape.Kind.ToString(),
                    ["shapeKind"] = shape.Shape.Kind.ToString(),
                    ["shapeId"] = shape.ShapeId,
                    ["pointIndex"] = pointIndex.ToString(CultureInfo.InvariantCulture),
                    ["pointsInShape"] = shape.Shape.Points.Count.ToString(CultureInfo.InvariantCulture)
                }
            }
        };
    }
}

/// <summary>
/// The contact candidates of one view, and everything that kept the set from being all of
/// them.
///
/// The three ways a point can be missing are kept apart because they are answered
/// differently: a part that was never read needs the geometry looked at again, a region
/// that flattened to nothing needs the flattening looked at, and a shape whose parts did
/// not resolve needs the ids looked at. Rolled into one count they would all read as
/// "something went wrong somewhere".
/// </summary>
public sealed class ViewContactCandidatePointsResult
{
    public ViewContactCandidatePointsResult(
        int viewId,
        IReadOnlyList<DrawingPartCandidatePoint> points,
        IReadOnlyList<UnflattenedRegion> unflattened,
        IReadOnlyList<ContactShapeInView> unresolved,
        IReadOnlyList<UnreadPart> unread,
        bool searchComplete,
        string? error = null)
    {
        ViewId = viewId;
        Points = points;
        Unflattened = unflattened;
        Unresolved = unresolved;
        Unread = unread;
        SearchComplete = searchComplete;
        Error = error;
    }

    public int ViewId { get; }

    /// <summary>Why there was nothing to read, when that is the case.</summary>
    public string? Error { get; }

    /// <summary>
    /// The candidates that came through. Correct as far as they go even when the result is
    /// incomplete - a point built from two named parts and a shape that flattened cleanly
    /// is not made wrong by a different part elsewhere going unread.
    /// </summary>
    public IReadOnlyList<DrawingPartCandidatePoint> Points { get; }

    /// <summary>Contact regions that left nothing on the sheet, so gave no points.</summary>
    public IReadOnlyList<UnflattenedRegion> Unflattened { get; }

    /// <summary>
    /// Shapes that reached the sheet but whose two parts were not both named, so no
    /// candidate could claim them. The geometry is in here to be looked at, not lost.
    /// </summary>
    public IReadOnlyList<ContactShapeInView> Unresolved { get; }

    /// <summary>Parts the view draws whose geometry never reached the contact search.</summary>
    public IReadOnlyList<UnreadPart> Unread { get; }

    /// <summary>Whether the contact search underneath saw the whole view.</summary>
    public bool SearchComplete { get; }

    /// <summary>
    /// True when the whole view was searched, every region flattened, and every shape knew
    /// both of its parts. Only then does the absence of a candidate somewhere mean that
    /// nothing touches there.
    /// </summary>
    public bool IsComplete =>
        Error == null && SearchComplete && Unflattened.Count == 0 && Unresolved.Count == 0;

    public override string ToString() =>
        Error != null
            ? "view " + ViewId + ": " + Error + "  INCOMPLETE"
            : "view " + ViewId + ": " + Points.Count + " contact candidate(s), "
              + Unflattened.Count + " lost, " + Unresolved.Count + " unowned, "
              + Unread.Count + " unread" + (IsComplete ? string.Empty : "  INCOMPLETE");
}
