namespace TeklaMcpServer.Api.Drawing;

public sealed class PartSolidGeometryInViewResult
{
    public bool Success { get; set; }
    public int ViewId { get; set; }
    public int ModelId { get; set; }
    public string? Error { get; set; }

    /// <summary>Part axis start point in view coordinates, when the part exposes one.</summary>
    public double[] StartPoint { get; set; } = [];

    /// <summary>Part axis end point in view coordinates, when the part exposes one.</summary>
    public double[] EndPoint { get; set; } = [];

    public PartSolidGeometry Solid { get; set; } = new();
}
