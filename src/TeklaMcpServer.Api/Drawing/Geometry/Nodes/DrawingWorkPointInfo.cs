namespace TeklaMcpServer.Api.Drawing;

public sealed class DrawingWorkPointInfo
{
    public DrawingWorkPointKind Kind { get; set; }
    public int SourceNodeIndex { get; set; }
    public int SourceModelId { get; set; }
    public double[] Point { get; set; } = [];
}
