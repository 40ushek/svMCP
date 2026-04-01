namespace TeklaMcpServer.Api.Drawing;

public sealed class PartBoltGeometryInViewResult
{
    public bool Success { get; set; }
    public int ViewId { get; set; }
    public int PartId { get; set; }
    public string? Error { get; set; }
    public List<BoltGroupGeometry> BoltGroups { get; set; } = new();
}
