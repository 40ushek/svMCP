namespace TeklaMcpServer.Api.Drawing;

public sealed class GetBoltGroupPointsResult
{
    public bool Success { get; set; }
    public int ViewId { get; set; }
    public int ModelId { get; set; }
    public string? Error { get; set; }
    public List<DrawingBoltPointInfo> Points { get; set; } = new();
}
