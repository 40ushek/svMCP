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
    /// <summary>
    /// The hole each bolted part really gets. Tekla's hole type and slots act only on the parts whose
    /// flag Hole1..Hole5 is on (part to bolt to, part to be bolted, then the other parts); the others
    /// get a plain hole. Empty when not read (older cases): then the group's own values count.
    /// </summary>
    public List<BoltPartHole> PartHoles { get; set; } = new();

    /// <summary>The hole in this part: its own entry, or the group's values when none was read.</summary>
    public BoltPartHole HoleIn(int partId) =>
        PartHoles.FirstOrDefault(hole => hole.PartId == partId)
        ?? new BoltPartHole { PartId = partId, Special = true, HoleType = HoleType, SlottedHoleX = SlottedHoleX, SlottedHoleY = SlottedHoleY };
}

public sealed class BoltPartHole
{
    public int PartId { get; set; }
    /// <summary>The group's special hole (type, slots) acts on this part; otherwise the hole is plain.</summary>
    public bool Special { get; set; }
    public string? HoleType { get; set; }
    public double SlottedHoleX { get; set; }
    public double SlottedHoleY { get; set; }
}
