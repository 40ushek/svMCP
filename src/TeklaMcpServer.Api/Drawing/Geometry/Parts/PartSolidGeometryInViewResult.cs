namespace TeklaMcpServer.Api.Drawing;

public sealed class PartSolidGeometryInViewResult
{
    public bool Success { get; set; }
    public int ViewId { get; set; }
    public int ModelId { get; set; }
    public string? Error { get; set; }
    public PartSolidGeometry Solid { get; set; } = new();
}
