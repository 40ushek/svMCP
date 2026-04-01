namespace TeklaMcpServer.Api.Drawing;

public sealed class DrawingNodePointInfo
{
    public DrawingNodePointKind Kind { get; set; }
    public DrawingNodePointSourceKind SourceKind { get; set; }
    public int SourceModelId { get; set; }
    public int Index { get; set; }
    public double[] Point { get; set; } = [];
}
