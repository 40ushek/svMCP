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

    /// <summary>
    /// Origin of the part's own coordinate system, in view coordinates. Empty when the
    /// part does not expose one.
    /// </summary>
    public double[] CoordinateSystemOrigin { get; set; } = [];

    /// <summary>
    /// The part's own X axis, in view coordinates. Sent with the geometry rather than
    /// left to be worked out from it: a bounding box on the part's own axes is snug where
    /// one on the view's axes is not, and a raked member measured on view axes gets a box
    /// spanning far more than it occupies. Deriving the axes from the faces instead would
    /// be guesswork standing in for something the part already knows.
    /// </summary>
    public double[] AxisX { get; set; } = [];

    /// <summary>The part's own Y axis, in view coordinates. Empty when unavailable.</summary>
    public double[] AxisY { get; set; } = [];

    public PartSolidGeometry Solid { get; set; } = new();
}
