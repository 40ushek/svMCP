namespace TeklaMcpServer.Api.Drawing;

public sealed class NodeWorkPointSet
{
    public int NodeIndex { get; set; }
    public DrawingNodeKind NodeKind { get; set; }
    public int NodeModelId { get; set; }
    public int MainPartId { get; set; }
    public double[] PrimaryPoint { get; set; } = [];
    public double[] SecondaryPoint { get; set; } = [];
    public DrawingLineInfo? ReferenceLine { get; set; }
    public List<DrawingWorkPointInfo> Points { get; set; } = new();
}
