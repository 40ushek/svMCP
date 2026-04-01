using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing;

public sealed class GetPartPointsResult
{
    public bool Success { get; set; }
    public int ViewId { get; set; }
    public int ModelId { get; set; }
    public string? Error { get; set; }

    public string? Type { get; set; }
    public string? Name { get; set; }
    public string? PartPos { get; set; }
    public string? Profile { get; set; }
    public string? Material { get; set; }

    public List<DrawingPartPointInfo> Points { get; set; } = new();
}
