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

    /// <summary>Part local X axis direction in view coordinate system.</summary>
    public double[] AxisX { get; set; } = [];

    /// <summary>Part local Y axis direction in view coordinate system.</summary>
    public double[] AxisY { get; set; } = [];

    /// <summary>Solid bounding box minimum corner in view coordinate system (mm).</summary>
    public double[] BboxMin { get; set; } = [];

    /// <summary>Solid bounding box maximum corner in view coordinate system (mm).</summary>
    public double[] BboxMax { get; set; } = [];
}
