namespace TeklaMcpServer.Api.Drawing;

public sealed class GetAssemblyNodesResult
{
    public bool Success { get; set; }
    public int ViewId { get; set; }
    public int ModelId { get; set; }
    public int MainPartId { get; set; }
    public string? Error { get; set; }
    public List<string> Warnings { get; set; } = new();
    public List<NodeGeometry> Nodes { get; set; } = new();
}
