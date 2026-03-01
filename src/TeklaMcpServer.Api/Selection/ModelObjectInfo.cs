namespace TeklaMcpServer.Api.Selection;

public sealed class ModelObjectInfo
{
    public int Id { get; set; }

    public string? Guid { get; set; }

    /// <summary>Tekla runtime type name: Beam, ContourPlate, BoltArray, BoltCircle, Weld, RebarGroup…</summary>
    public string Type { get; set; } = string.Empty;

    public string? Name { get; set; }

    // Part properties
    public string? Profile { get; set; }
    public string? Material { get; set; }
    public string? Class { get; set; }
    public double? WeightKg { get; set; }

    // Bolt properties
    public string? BoltStandard { get; set; }
    public double? BoltSize { get; set; }
    public string? BoltGrade { get; set; }
    public int? BoltCount { get; set; }
}
