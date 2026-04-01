namespace TeklaMcpServer.Api.Drawing;

public sealed class DrawingAssemblyPointInfo
{
    public DrawingAssemblyPointKind Kind { get; set; }
    public DrawingAssemblyPointSourceKind SourceKind { get; set; }
    public int SourceModelId { get; set; }
    public int Index { get; set; }
    public double[] Point { get; set; } = [];
}
