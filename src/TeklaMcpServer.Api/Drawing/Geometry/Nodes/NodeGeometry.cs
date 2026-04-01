namespace TeklaMcpServer.Api.Drawing;

public sealed class NodeGeometry
{
    public int Index { get; set; }
    public DrawingNodeKind Kind { get; set; }
    public int ModelId { get; set; }
    public int MainPartId { get; set; }
    public double[] Center { get; set; } = [];
    public double[] BboxMin { get; set; } = [];
    public double[] BboxMax { get; set; } = [];
    public List<DrawingNodePointInfo> Points { get; set; } = new();
}
