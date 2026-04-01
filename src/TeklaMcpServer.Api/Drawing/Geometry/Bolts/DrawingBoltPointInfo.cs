namespace TeklaMcpServer.Api.Drawing;

public sealed class DrawingBoltPointInfo
{
    public DrawingBoltPointKind Kind { get; set; }
    public DrawingBoltPointSourceKind SourceKind { get; set; }
    public int SourceModelId { get; set; }
    public int Index { get; set; }
    public double[] Point { get; set; } = [];
}
