using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

public sealed class PartInView
{
    public bool    Success { get; set; }
    public int     ViewId  { get; set; }
    public int     ModelId { get; set; }
    public string? Error   { get; set; }

    /// <summary>Part axis start point in view coordinate system (mm).</summary>
    public double[] StartPoint { get; set; } = [];

    /// <summary>Part axis end point in view coordinate system (mm).</summary>
    public double[] EndPoint { get; set; } = [];

    /// <summary>Part coordinate system origin in view coordinate system (mm).</summary>
    public double[] CoordinateSystemOrigin { get; set; } = [];

    /// <summary>Part local X axis direction in view coordinate system.</summary>
    public double[] AxisX { get; set; } = [];

    /// <summary>Part local Y axis direction in view coordinate system.</summary>
    public double[] AxisY { get; set; } = [];

    /// <summary>Solid bounding box minimum corner in view coordinate system (mm).</summary>
    public double[] BboxMin { get; set; } = [];

    /// <summary>Solid bounding box maximum corner in view coordinate system (mm).</summary>
    public double[] BboxMax { get; set; } = [];

    /// <summary>
    /// Unique solid vertices in view coordinate system (mm), rounded to five decimals.
    /// Empty when SolidGeometryComplete is false. ViewHull is projected from this same
    /// canonical snapshot.
    /// </summary>
    public List<double[]> SolidVertices { get; set; } = new();

    /// <summary>
    /// True only when the complete solid vertex/face traversal succeeded. False means
    /// the solid was unavailable or the tolerant traversal returned a partial snapshot.
    /// </summary>
    public bool SolidGeometryComplete { get; set; }

    /// <summary>
    /// Two-dimensional convex hull of the part projected to the XY plane of the view
    /// coordinate system. It is derived from the same canonical, rounded snapshot as
    /// SolidVertices, so every hull vertex corresponds to a source vertex by its [x,y]
    /// coordinates. Each point contains [x, y] in millimeters. For three or more points,
    /// the list is an open counter-clockwise traversal and does not repeat the first point.
    /// One- and two-point hulls are valid degenerate results.
    /// </summary>
    public List<double[]> ViewHull { get; set; } = new();

    // Fields populated by GetAllPartsGeometryInView (not set by single-part call)
    public string? Type         { get; set; }
    public string? Name         { get; set; }
    public string? PartPos      { get; set; }
    public string? Profile      { get; set; }
    public string? Material     { get; set; }
    /// <summary>Tekla MATERIAL_TYPE: 1=Steel, 2=Concrete, 5=Timber, 6=Misc. -1 if unavailable.</summary>
    public int     MaterialType { get; set; } = -1;
    /// <summary>
    /// Part mark prefix, e.g. "T" in "T-368". Report property PART_PREFIX.
    /// Classifies the part: T=timber, M=metal fitting, R=insulation in this model's numbering.
    ///
    /// Like the fields above, this is filled by GetAllPartsGeometryInView only. The single-part
    /// call (get_part_geometry_in_view) leaves it null and does not serialize it.
    ///
    /// Assembly-level properties (ASSEMBLY_POS / ASSEMBLY_PREFIX) are deliberately NOT read here:
    /// every part of an assembly drawing returns the same value, and each read costs a Select().
    /// Use get_drawing_parts for those.
    /// </summary>
    public string? PartPrefix     { get; set; }

    internal PartInView Clone() => new()
    {
        Success = Success,
        ViewId = ViewId,
        ModelId = ModelId,
        Error = Error,
        StartPoint = StartPoint.ToArray(),
        EndPoint = EndPoint.ToArray(),
        CoordinateSystemOrigin = CoordinateSystemOrigin.ToArray(),
        AxisX = AxisX.ToArray(),
        AxisY = AxisY.ToArray(),
        BboxMin = BboxMin.ToArray(),
        BboxMax = BboxMax.ToArray(),
        SolidVertices = SolidVertices.Select(static vertex => vertex.ToArray()).ToList(),
        ViewHull = ViewHull.Select(static vertex => vertex.ToArray()).ToList(),
        SolidGeometryComplete = SolidGeometryComplete,
        Type = Type,
        Name = Name,
        PartPos = PartPos,
        Profile = Profile,
        Material = Material,
        MaterialType = MaterialType,
        PartPrefix = PartPrefix
    };

    internal PartInView CloneGeometryOnly() => new()
    {
        Success = Success,
        ViewId = ViewId,
        ModelId = ModelId,
        Error = Error,
        StartPoint = StartPoint.ToArray(),
        EndPoint = EndPoint.ToArray(),
        CoordinateSystemOrigin = CoordinateSystemOrigin.ToArray(),
        AxisX = AxisX.ToArray(),
        AxisY = AxisY.ToArray(),
        BboxMin = BboxMin.ToArray(),
        BboxMax = BboxMax.ToArray(),
        SolidVertices = SolidVertices.Select(static vertex => vertex.ToArray()).ToList(),
        ViewHull = ViewHull.Select(static vertex => vertex.ToArray()).ToList(),
        SolidGeometryComplete = SolidGeometryComplete
    };
}
