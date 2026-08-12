using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// A possible dimension anchor. Coordinates are in the owning drawing view coordinate
/// system. This is evidence for placement; it does not create a dimension.
/// </summary>
public sealed class DrawingPartCandidatePoint
{
    /// <summary>
    /// The parts this point came from, from none to several.
    ///
    /// A point on one part's geometry names that part. A contact names both participants:
    /// where a stud meets its plate the point belongs to each, and two records at one
    /// coordinate would invent a second point that is not there.
    ///
    /// Empty is a legitimate answer, not a gap. A point created by unioning part contours
    /// has no owner to name - the union merges boundaries at a tolerance that moves them,
    /// so a vertex of the assembly outline need not be a vertex of any part, and
    /// intersections appear that no part ever had. Do not fill this by finding the nearest
    /// part: that invents an owner, and it is the distance-based association this whole
    /// area exists to avoid.
    /// </summary>
    public List<int> ModelObjectIds { get; set; } = [];
    public double[] Point { get; set; } = [];
    public DrawingPartCandidatePointSource Source { get; set; }
    public double[]? Normal { get; set; }
    /// <summary>
    /// Unit face normal projected into the view XY plane, or null when the source has
    /// no normal or its normal is perpendicular to the drawing plane.
    /// </summary>
    public double[]? InPlaneNormal { get; set; }
    public DrawingPartCandidateConfidence Confidence { get; set; }
    public DrawingPartCandidateAnchor Anchor { get; set; } = new();
    public DrawingPartCandidateReason Reason { get; set; } = new();
}

public enum DrawingPartCandidatePointSource
{
    AxisStart,
    AxisEnd,
    SolidVertex,
    FaceBoundaryMidpoint,
    HullVertex,
    BoundingBoxCorner,

    /// <summary>
    /// A corner of one part's projected contour - the union of its faces in the view.
    ///
    /// Kept apart from <see cref="SolidVertex"/> rather than folded into it. After the
    /// union a corner need not be a vertex of the solid: cut ends and merged coplanar
    /// faces produce corners the model never had, and calling those solid vertices would
    /// promise a native Tekla feature that is not there.
    /// </summary>
    PartContour,

    /// <summary>
    /// A corner of the assembly outline, where the contours of several parts were merged.
    /// Belongs to no single part - see the note on ModelObjectIds.
    /// </summary>
    AssemblyContour,

    /// <summary>
    /// A place on a contact between two parts, as that contact reads on the sheet.
    ///
    /// Not geometry either part owns. It is where they meet, which is a fact about the pair
    /// and is why both are named - and it is the fact a dimension between them is measuring.
    /// </summary>
    Contact
}

/// <summary>How directly the source proves that the point belongs to the part.</summary>
public enum DrawingPartCandidateConfidence
{
    ExactGeometry,
    ReferenceGeometry,
    DerivedGeometry,
    BoundingBoxFallback
}

public sealed class DrawingPartCandidateAnchor
{
    /// <summary>
    /// The one part this anchor is a feature of, where it is a feature of one.
    ///
    /// Single rather than a list: a point may belong to several parts, but a face or a
    /// vertex belongs to exactly one, and that is what an anchor names.
    ///
    /// Null where the feature is not one part's. A contact surface is the meeting of two
    /// parts and belongs to neither more than the other, so naming one of them here would
    /// make the other's face a coincidence - and naming zero would invent a part.
    /// </summary>
    public int? ModelObjectId { get; set; }
    public DrawingPartCandidateAnchorKind Kind { get; set; }
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Stable comparison key: anchor kind and anchor id, prefixed by the part when the
    /// feature is a part's.
    ///
    /// Empty where there is no anchor. A point on the assembly outline is a feature of no
    /// part, and "0:None:" would read as an identity shared by every such point. Where a
    /// feature has no single owner but does have a name of its own - a contact point, whose
    /// id already says which contact and which shape - the part is simply left off rather
    /// than filled in.
    /// </summary>
    public string Key => Kind == DrawingPartCandidateAnchorKind.None
        ? string.Empty
        : ModelObjectId.HasValue
            ? $"{ModelObjectId.Value}:{Kind}:{Id}"
            : $"{Kind}:{Id}";
}

public enum DrawingPartCandidateAnchorKind
{
    AxisEnd,
    Vertex,
    FaceEdge,
    HullVertex,
    BoundingBoxCorner,

    /// <summary>
    /// A corner of a part's own projected contour, the union of its faces in the view.
    /// Distinct from <see cref="Vertex"/>: after the union a corner need not be a vertex of
    /// the solid at all, so there is no native Tekla feature to name.
    /// </summary>
    ContourVertex,

    /// <summary>
    /// A point of a contact shape - one contact, one flattened shape, one place on it.
    ///
    /// Distinct from every other kind because it has no single owner and does not need one:
    /// its id already says which contact and which shape, which no part could say.
    /// </summary>
    ContactPoint,

    /// <summary>
    /// No feature of any one part. For a corner of the assembly outline, where the union
    /// merged boundaries and there is nothing to anchor to.
    /// </summary>
    None
}

/// <summary>Machine-readable evidence for why a candidate was emitted.</summary>
public sealed class DrawingPartCandidateReason
{
    public string Code { get; set; } = string.Empty;
    public List<int> ModelObjectIds { get; set; } = new();
    public Dictionary<string, string> Values { get; set; } = new();
}
