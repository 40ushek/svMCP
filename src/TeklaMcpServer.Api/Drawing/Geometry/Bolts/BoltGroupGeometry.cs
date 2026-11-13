namespace TeklaMcpServer.Api.Drawing;

public sealed class BoltGroupGeometry
{
    public int ModelId { get; set; }
    public string? Shape { get; set; }
    public string? BoltType { get; set; }
    public string? BoltStandard { get; set; }
    public double BoltSize { get; set; }
    /// <summary>Tekla's hole tolerance: the hole is this much wider than the bolt.</summary>
    public double HoleTolerance { get; set; }
    /// <summary>Bolt size plus hole tolerance.</summary>
    public double HoleDiameter => BoltSize + HoleTolerance;
    /// <summary>Only holes, no bolts (Tekla's <c>Bolt</c> switched off).</summary>
    public bool HoleOnly { get; set; }
    public string? HoleType { get; set; }
    public double SlottedHoleX { get; set; }
    public double SlottedHoleY { get; set; }
    public double[] FirstPosition { get; set; } = [];
    public double[] SecondPosition { get; set; } = [];
    /// <summary>Unit bolt axis (normal of the group's XY plane) in view coordinates; empty when it could not be read.</summary>
    public double[] Axis { get; set; } = [];
    public double[] BboxMin { get; set; } = [];
    public double[] BboxMax { get; set; } = [];
    public int? PartToBeBoltedId { get; set; }
    public int? PartToBoltToId { get; set; }
    public List<int> OtherPartIds { get; set; } = new();
    public List<BoltPointGeometry> Positions { get; set; } = new();
}
