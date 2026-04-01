namespace TeklaMcpServer.Api.Drawing;

public sealed class ConnectionNodeGeometry
{
    public int NodeIndex { get; set; }
    public DrawingNodeKind NodeKind { get; set; }
    public int SourceModelId { get; set; }
    public int MainPartId { get; set; }
    public int PrimaryPartId { get; set; }
    public List<int> SecondaryPartIds { get; set; } = new();
    public double[] PrimaryWorkPoint { get; set; } = [];
    public double[] SecondaryWorkPoint { get; set; } = [];
    public DrawingLineInfo? ReferenceLine { get; set; }
    public List<ConnectionNodeParticipantInfo> Participants { get; set; } = new();
}
