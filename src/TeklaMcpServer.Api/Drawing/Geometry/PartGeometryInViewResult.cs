using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing;

public sealed class PartGeometryInViewResult
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

    /// <summary>Unique solid vertices in view coordinate system (mm).</summary>
    public List<double[]> SolidVertices { get; set; } = new();

    // Fields populated by GetAllPartsGeometryInView (not set by single-part call)
    public string? Type         { get; set; }
    public string? Name         { get; set; }
    public string? PartPos      { get; set; }
    public string? Profile      { get; set; }
    public string? Material     { get; set; }
    /// <summary>Tekla MATERIAL_TYPE: 1=Steel, 2=Concrete, 5=Timber, 6=Misc. -1 if unavailable.</summary>
    public int     MaterialType { get; set; } = -1;
}
