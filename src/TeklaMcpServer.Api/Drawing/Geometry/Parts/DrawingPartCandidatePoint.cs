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
    BoundingBoxCorner
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
    /// The one part this anchor is a feature of. Single on purpose, unlike the candidate's
    /// own list: a point may belong to several parts, but a face or a vertex belongs to
    /// exactly one, and that is what an anchor names.
    /// </summary>
    public int ModelObjectId { get; set; }
    public DrawingPartCandidateAnchorKind Kind { get; set; }
    public string Id { get; set; } = string.Empty;

    /// <summary>Stable comparison key: model object id + anchor kind + anchor id.</summary>
    public string Key => $"{ModelObjectId}:{Kind}:{Id}";
}

public enum DrawingPartCandidateAnchorKind
{
    AxisEnd,
    Vertex,
    FaceEdge,
    HullVertex,
    BoundingBoxCorner
}

/// <summary>Machine-readable evidence for why a candidate was emitted.</summary>
public sealed class DrawingPartCandidateReason
{
    public string Code { get; set; } = string.Empty;
    public List<int> ModelObjectIds { get; set; } = new();
    public Dictionary<string, string> Values { get; set; } = new();
}
