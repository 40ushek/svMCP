using System;
using System.Collections.Generic;
using System.Linq;
using SolidContacts;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// How much of a part's position in the view plane its contacts already settle.
///
/// Read-only diagnostic - see <c>ROADMAP_PART_FREEDOM.md</c> beside this file for what it
/// is and, as importantly, what it is not: it does not decide which dimensions a drawing
/// needs, and without a datum it says nothing about whether the assembly as a whole is
/// pinned to anything - see <see cref="ViewPartFreedomResult.AllPartsHaveObservedAxisContacts"/>.
///
/// Geometry only, like <see cref="CalcDimensionChains"/>: no Tekla call, testable on
/// contact shapes built by hand. It counts a contact only when both halves of it say it
/// can really bear something - <see cref="ContactKind.FaceToFace"/>, in
/// <see cref="ContactState.Touching"/> - and only when the contact plane's own normal reads
/// as pointing along one in-plane axis rather than the other, within a real angular
/// tolerance. Anything short of that is reported as excluded, never folded silently into
/// "free". Rotation is not counted; propagation from a datum is not attempted. Both are
/// named as missing work in the roadmap rather than guessed at here.
/// </summary>
public static class PartDegreesOfFreedomAnalyzer
{
    /// <summary>
    /// Shared with the axis-alignment test <see cref="CalcDimensionChains"/> uses for a
    /// part's own extent - the two ask the same shape of question ("is this direction close
    /// enough to one axis to trust"), and a different number here would need its own
    /// justification for why 0.1 degrees is right for a part's edges but not for a contact's
    /// normal. Applied here as a pure angle, unlike there: a unit normal's components are
    /// direction cosines, not millimetres, so no absolute-offset term applies alongside it.
    /// </summary>
    private static readonly double AngularToleranceSine =
        Math.Sin(CalcDimensionChains.PartExtentAxisAlignmentAngleToleranceDegrees * Math.PI / 180.0);

    /// <param name="contacts">
    /// The raw search. Its <see cref="ViewContactsResult.RequestedIds"/> is the only honest
    /// population to report over - every part the view asked to be searched, not merely
    /// those a junction happened to mention.
    /// </param>
    /// <param name="geometry">
    /// The same search's contacts, flattened onto the view plane. Kind, state and normal
    /// travel with each shape already - see <see cref="ContactShapeInView"/> - so this needs
    /// no second lookup against the raw search to use them.
    /// </param>
    public static ViewPartFreedomResult Build(ViewContactsResult contacts, ViewContactGeometryResult geometry)
    {
        if (contacts == null) throw new ArgumentNullException(nameof(contacts));
        if (geometry == null) throw new ArgumentNullException(nameof(geometry));

        // The two must be one search read twice, not two searches mixed up by a caller -
        // ContactGeometryInViewBuilder.Build always stamps geometry with contacts.ViewId, so
        // a mismatch here can only mean the wrong pair was passed in.
        if (contacts.ViewId != geometry.ViewId)
        {
            throw new ArgumentException(
                $"contacts is for view {contacts.ViewId} but geometry is for view " +
                $"{geometry.ViewId} - both must come from the same contact search.",
                nameof(geometry));
        }

        var error = contacts.Error ?? geometry.Error;
        if (error != null)
        {
            return new ViewPartFreedomResult(
                contacts.ViewId, Array.Empty<PartDegreesOfFreedom>(), geometry.Unread,
                geometry.Unflattened, geometry.Unresolved, contacts.Graph.Failures,
                Array.Empty<ContactShapeInView>(), Array.Empty<ContactShapeInView>(),
                searchComplete: false, error: error);
        }

        return Build(contacts.ViewId, contacts.Graph.Failures, contacts.RequestedIds, geometry);
    }

    /// <summary>
    /// The same computation, taking the pieces of <see cref="ViewContactsResult"/> that are
    /// not <see cref="ViewContactGeometryResult"/> directly, so tests can supply hand-built
    /// contact shapes without needing a real <see cref="ContactGraph"/> - one is only
    /// buildable from real solids. Internal: the two-object overload above is what a caller
    /// outside this assembly should use.
    /// </summary>
    internal static ViewPartFreedomResult Build(
        int viewId,
        IReadOnlyList<SearchFailure> failures,
        IReadOnlyList<int> requestedIds,
        ViewContactGeometryResult geometry)
    {
        if (failures == null) throw new ArgumentNullException(nameof(failures));
        if (requestedIds == null) throw new ArgumentNullException(nameof(requestedIds));
        if (geometry == null) throw new ArgumentNullException(nameof(geometry));

        var constrainedX = new Dictionary<int, List<FreedomConstraint>>();
        var constrainedY = new Dictionary<int, List<FreedomConstraint>>();
        var ambiguous = new List<ContactShapeInView>();
        var notLoadBearing = new List<ContactShapeInView>();

        foreach (var shape in geometry.Shapes)
        {
            // A shape whose two parts are not both named removes freedom from nobody in
            // particular. It stays in `geometry.Unresolved` for whoever needs to see it.
            if (!shape.Participants.Resolved)
                continue;

            // Only a face resting on a face carries real support - SolidContacts' own words
            // for ContactKind.FaceToFace. An edge laid on a face or two edges alongside each
            // other are real touches with no area behind them, and a gap within the search's
            // broad tolerance or an overlap are not a settled position either. None of the
            // three earns a verdict; all three are reported so the exclusion is visible
            // rather than silently read as "nothing touches here".
            if (shape.Kind != ContactKind.FaceToFace || shape.State != ContactState.Touching)
            {
                notLoadBearing.Add(shape);
                continue;
            }

            var axis = ConstrainedAxisOf(shape.Normal);
            if (axis == null)
            {
                ambiguous.Add(shape);
                continue;
            }

            var a = shape.Participants.ModelObjectIdA!.Value;
            var b = shape.Participants.ModelObjectIdB!.Value;
            var map = axis == Axis.X ? constrainedX : constrainedY;
            Constrain(map, owner: a, partner: b, shape);
            Constrain(map, owner: b, partner: a, shape);
        }

        var parts = requestedIds
            .OrderBy(static id => id)
            .Select(id => new PartDegreesOfFreedom(
                id,
                freeX: !constrainedX.ContainsKey(id),
                freeY: !constrainedY.ContainsKey(id),
                constrainedByX: constrainedX.TryGetValue(id, out var cx) ? cx : Array.Empty<FreedomConstraint>(),
                constrainedByY: constrainedY.TryGetValue(id, out var cy) ? cy : Array.Empty<FreedomConstraint>()))
            .ToList();

        // geometry.IsComplete already folds in failures when geometry came from the same
        // search failures did - the production path, through the two-object overload above.
        // This overload receives failures on its own, though, precisely so a caller cannot
        // pass a geometry that reads complete alongside failures that say otherwise and have
        // the mismatch go unnoticed; the count is checked again here rather than trusted.
        var searchComplete = geometry.IsComplete && failures.Count == 0;

        return new ViewPartFreedomResult(
            viewId, parts, geometry.Unread, geometry.Unflattened, geometry.Unresolved, failures,
            notLoadBearing, ambiguous, searchComplete, error: null);
    }

    private enum Axis { X, Y }

    /// <summary>
    /// Reads a contact's own plane normal, not the shape it happened to flatten to: a face
    /// contact seen face-on can flatten to a narrow polygon by accident of the projection,
    /// and a shape-based reading would call that a clean vertical or horizontal support it
    /// is not. The normal does not lie about this - a face-on contact's normal points out of
    /// the view plane regardless of the patch's outline.
    ///
    /// A normal close to the view's Z axis (small in-plane component) resists depth, not
    /// either in-plane axis, and is not classified. Of what remains, a normal close to X
    /// removes X and leaves Y - the slide along the face; close to Y removes Y. A normal
    /// genuinely diagonal in-plane is not classified either: guessing an axis for it would be
    /// inventing a constraint model past what one number can support.
    /// </summary>
    private static Axis? ConstrainedAxisOf(Vec3 normal)
    {
        var inPlaneLength = Math.Sqrt((normal.X * normal.X) + (normal.Y * normal.Y));

        if (inPlaneLength <= AngularToleranceSine)
            return null;

        var offX = Math.Abs(normal.Y); // how far the in-plane direction sits from pure X
        var offY = Math.Abs(normal.X); // how far it sits from pure Y

        var pointsAlongX = IsNegligible(offX, inPlaneLength);
        var pointsAlongY = IsNegligible(offY, inPlaneLength);

        if (pointsAlongX && !pointsAlongY) return Axis.X;
        if (pointsAlongY && !pointsAlongX) return Axis.Y;
        return null;
    }

    /// <summary>
    /// Whether a 2D direction's component off one axis is small enough, relative to its own
    /// length, to call the direction that axis - the trigonometric form of the angle test,
    /// so it works the same regardless of how long the in-plane projection is.
    /// </summary>
    private static bool IsNegligible(double offAxisComponent, double length) =>
        offAxisComponent <= length * AngularToleranceSine;

    private static void Constrain(
        Dictionary<int, List<FreedomConstraint>> map, int owner, int partner, ContactShapeInView shape)
    {
        if (!map.TryGetValue(owner, out var list))
        {
            list = new List<FreedomConstraint>();
            map[owner] = list;
        }

        list.Add(new FreedomConstraint(partner, shape.ContactId, shape.ShapeId));
    }
}

/// <summary>
/// One contact's contribution to removing a part's freedom in one axis - who the other
/// part is, and which contact and shape said so. Named rather than reduced to a bare
/// partner id, so a reading can be checked against the drawing rather than taken on faith.
/// </summary>
public sealed class FreedomConstraint
{
    public FreedomConstraint(int partnerModelId, string contactId, string shapeId)
    {
        PartnerModelId = partnerModelId;
        ContactId = contactId;
        ShapeId = shapeId;
    }

    public int PartnerModelId { get; }
    public string ContactId { get; }
    public string ShapeId { get; }

    public override string ToString() => $"{PartnerModelId} ({ContactId}/{ShapeId})";
}

/// <summary>
/// One part's freedom in the view plane, and what removed what it no longer has.
///
/// <see cref="FreeRotation"/> is always null. That is not a placeholder for "false" - a
/// two-axis count that stayed silent about rotation would read as "fixed" for a part that
/// can still turn, which is exactly the M.53 end plate: pinned by an angled dimension, a
/// constraint this analyzer does not yet compute. An unanswered field says so; a missing one
/// would not.
///
/// <see cref="FreeX"/>/<see cref="FreeY"/> themselves say only that no counted face contact
/// removed the axis, not that the joint is engineered to hold it - the joining method and
/// the fixings are not in the contact set. Read them as "no observed load-bearing contact
/// against this axis", not as a proven mechanical fact.
/// </summary>
public sealed class PartDegreesOfFreedom
{
    public PartDegreesOfFreedom(
        int modelId, bool freeX, bool freeY,
        IReadOnlyList<FreedomConstraint> constrainedByX, IReadOnlyList<FreedomConstraint> constrainedByY)
    {
        ModelId = modelId;
        FreeX = freeX;
        FreeY = freeY;
        ConstrainedByX = constrainedByX ?? Array.Empty<FreedomConstraint>();
        ConstrainedByY = constrainedByY ?? Array.Empty<FreedomConstraint>();
    }

    public int ModelId { get; }
    public bool FreeX { get; }
    public bool FreeY { get; }

    /// <summary>Not counted yet. See the type's remarks.</summary>
    public bool? FreeRotation => null;

    /// <summary>The contacts that removed this part's freedom in X, if any.</summary>
    public IReadOnlyList<FreedomConstraint> ConstrainedByX { get; }

    /// <summary>The contacts that removed this part's freedom in Y, if any.</summary>
    public IReadOnlyList<FreedomConstraint> ConstrainedByY { get; }

    public override string ToString() =>
        $"{ModelId}: X={(FreeX ? "no observed contact" : "contact observed")}, " +
        $"Y={(FreeY ? "no observed contact" : "contact observed")}";
}

/// <summary>
/// The freedom reading for every part a view asked its contact search to consider, together
/// with everything that kept the reading from being trusted everywhere or from bearing on
/// every contact it found.
/// </summary>
public sealed class ViewPartFreedomResult
{
    public ViewPartFreedomResult(
        int viewId,
        IReadOnlyList<PartDegreesOfFreedom> parts,
        IReadOnlyList<UnreadPart> unread,
        IReadOnlyList<UnflattenedRegion> unflattened,
        IReadOnlyList<ContactShapeInView> unresolved,
        IReadOnlyList<SearchFailure> failures,
        IReadOnlyList<ContactShapeInView> notLoadBearing,
        IReadOnlyList<ContactShapeInView> ambiguousDirection,
        bool searchComplete,
        string? error = null)
    {
        ViewId = viewId;
        Parts = parts;
        Unread = unread;
        Unflattened = unflattened;
        Unresolved = unresolved;
        Failures = failures;
        NotLoadBearing = notLoadBearing;
        AmbiguousDirection = ambiguousDirection;
        SearchComplete = searchComplete;
        Error = error;
    }

    public int ViewId { get; }

    /// <summary>Why there was nothing to read, when that is the case.</summary>
    public string? Error { get; }

    public IReadOnlyList<PartDegreesOfFreedom> Parts { get; }

    /// <summary>Parts the underlying contact search could not read.</summary>
    public IReadOnlyList<UnreadPart> Unread { get; }

    /// <summary>Contact regions that left nothing on the sheet once flattened.</summary>
    public IReadOnlyList<UnflattenedRegion> Unflattened { get; }

    /// <summary>Shapes whose two parts were not both named, so removed freedom from neither.</summary>
    public IReadOnlyList<ContactShapeInView> Unresolved { get; }

    /// <summary>Pairs whose search threw. Carried straight from <see cref="ContactGraph.Failures"/>.</summary>
    public IReadOnlyList<SearchFailure> Failures { get; }

    /// <summary>
    /// Contacts that touched but were not counted: not <see cref="ContactKind.FaceToFace"/>,
    /// or not in <see cref="ContactState.Touching"/> - a gap inside the search's broad
    /// tolerance, or an overlap. A part reading as free may still have one of these against
    /// it; look here before reading "free" as "isolated".
    /// </summary>
    public IReadOnlyList<ContactShapeInView> NotLoadBearing { get; }

    /// <summary>
    /// Load-bearing contacts whose own plane normal could not be read as pointing along one
    /// in-plane axis or the other - out of the view plane, or genuinely diagonal in it. Not a
    /// defect: an honest "not classified" rather than a guessed normal.
    /// </summary>
    public IReadOnlyList<ContactShapeInView> AmbiguousDirection { get; }

    /// <summary>Whether the contact search underneath saw the whole view.</summary>
    public bool SearchComplete { get; }

    /// <summary>
    /// True when every part was read and every contact resolved. Only then does a part
    /// reading as free mean nothing load-bearing touches it, rather than that something was
    /// never looked at - the same guarantee <see cref="ViewContactGeometryResult.IsComplete"/>
    /// gives contacts.
    /// </summary>
    public bool IsComplete => Error == null && SearchComplete;

    /// <summary>
    /// Null while <see cref="IsComplete"/> is false - an incomplete read has nothing to say.
    /// Otherwise: whether every part in this read has at least one counted contact against
    /// both X and Y.
    ///
    /// This is not a claim that the assembly is fixed in space, and the name says so on
    /// purpose. Nothing here is grounded to a datum - see the roadmap's "what is still
    /// missing" - so a system of parts bolted only to each other reads exactly like one
    /// bolted to the world: every local axis gap is closed, and the whole rigid group could
    /// still float. What this answers is narrower and still useful: whether the local reading
    /// has any gap left in it, nothing more.
    /// </summary>
    public bool? AllPartsHaveObservedAxisContacts =>
        !IsComplete ? null : Parts.Count > 0 && Parts.All(static part => !part.FreeX && !part.FreeY);

    public override string ToString() =>
        Error != null
            ? $"view {ViewId}: {Error}  INCOMPLETE"
            : $"view {ViewId}: {Parts.Count} part(s), {NotLoadBearing.Count} not load-bearing, "
              + $"{AmbiguousDirection.Count} ambiguous, {Unread.Count} unread"
              + (IsComplete ? string.Empty : "  INCOMPLETE");
}
