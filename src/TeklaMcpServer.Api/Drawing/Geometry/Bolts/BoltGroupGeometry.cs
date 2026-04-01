namespace TeklaMcpServer.Api.Drawing;

public sealed class BoltGroupGeometry
{
    public int ModelId { get; set; }
    public string? Shape { get; set; }
    public string? BoltType { get; set; }
    public string? BoltStandard { get; set; }
    public double BoltSize { get; set; }
    public double[] FirstPosition { get; set; } = [];
    public double[] SecondPosition { get; set; } = [];
    public double[] BboxMin { get; set; } = [];
    public double[] BboxMax { get; set; } = [];
    public int? PartToBeBoltedId { get; set; }
    public int? PartToBoltToId { get; set; }
    public List<int> OtherPartIds { get; set; } = new();
    public List<BoltPointGeometry> Positions { get; set; } = new();
}
