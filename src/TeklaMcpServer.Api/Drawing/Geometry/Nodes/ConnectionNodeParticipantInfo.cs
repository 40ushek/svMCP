namespace TeklaMcpServer.Api.Drawing;

public sealed class ConnectionNodeParticipantInfo
{
    public int PartId { get; set; }
    public DrawingConnectionParticipantRole Role { get; set; }
    public bool IsMainPart { get; set; }
    public string? Name { get; set; }
    public string? Profile { get; set; }
    public string? Material { get; set; }
    public double[] Center { get; set; } = [];
    public double[] BboxMin { get; set; } = [];
    public double[] BboxMax { get; set; } = [];
}
