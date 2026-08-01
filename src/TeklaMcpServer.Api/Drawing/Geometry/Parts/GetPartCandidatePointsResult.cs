using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing;

public sealed class GetPartCandidatePointsResult
{
    public bool Success { get; set; }
    public int ViewId { get; set; }
    public int ModelId { get; set; }
    public bool SolidGeometryComplete { get; set; }
    public string? Error { get; set; }
    public List<DrawingPartCandidatePoint> Candidates { get; set; } = new();
}
