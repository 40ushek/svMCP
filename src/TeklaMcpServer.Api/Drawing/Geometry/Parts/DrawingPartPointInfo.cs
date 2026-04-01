namespace TeklaMcpServer.Api.Drawing;

public sealed class DrawingPartPointInfo
{
    public DrawingPartPointKind Kind { get; set; }
    public DrawingPartPointSourceKind SourceKind { get; set; }
    public int Index { get; set; }
    public double[] Point { get; set; } = [];
}
