namespace TeklaMcpServer.Api.Drawing;

public sealed class PlaceControlDiagonalsResult
{
    public bool Created { get; set; }
    public int CreatedCount { get; set; }
    public int ViewId { get; set; }
    public string ViewType { get; set; } = string.Empty;
    public bool RectangleLike { get; set; }
    public int RequestedDiagonalCount { get; set; }
    public int PartsScanned { get; set; }
    public int SourceDimensionsScanned { get; set; }
    public int CandidatePoints { get; set; }
    public int DimensionId { get; set; }
    public int[] DimensionIds { get; set; } = [];
    public double[] StartPoint { get; set; } = [];
    public double[] EndPoint { get; set; } = [];
    public double FarthestDistance { get; set; }
    public long SelectViewMs { get; set; }
    public long ReadGeometryMs { get; set; }
    public long FindExtremesMs { get; set; }
    public long CreateMs { get; set; }
    public long CommitMs { get; set; }
    public long TotalMs { get; set; }
    public string? Error { get; set; }
}
