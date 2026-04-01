namespace TeklaMcpServer.Api.Drawing;

public sealed class BoltGroupGeometryInViewResult
{
    public bool Success { get; set; }
    public int ViewId { get; set; }
    public int ModelId { get; set; }
    public string? Error { get; set; }
    public BoltGroupGeometry BoltGroup { get; set; } = new();
}
