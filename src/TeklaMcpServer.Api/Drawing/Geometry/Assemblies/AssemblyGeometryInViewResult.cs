namespace TeklaMcpServer.Api.Drawing;

public sealed class AssemblyGeometryInViewResult
{
    public bool Success { get; set; }
    public int ViewId { get; set; }
    public int ModelId { get; set; }
    public string? Error { get; set; }
    public AssemblyGeometry Assembly { get; set; } = new();
}
