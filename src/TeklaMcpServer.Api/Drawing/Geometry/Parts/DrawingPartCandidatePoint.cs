using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// A possible dimension anchor on one model part. Coordinates are in the owning drawing
/// view coordinate system. This is evidence for placement; it does not create a dimension.
/// </summary>
public sealed class DrawingPartCandidatePoint
{
    public int ModelObjectId { get; set; }
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
