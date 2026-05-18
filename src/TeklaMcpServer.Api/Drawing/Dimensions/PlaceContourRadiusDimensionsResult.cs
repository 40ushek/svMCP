namespace TeklaMcpServer.Api.Drawing;

public sealed class PlaceContourRadiusDimensionsResult
{
    public bool Created { get; set; }
    public int CreatedCount { get; set; }
    public int ViewId { get; set; }
    public string ViewType { get; set; } = string.Empty;
    public int ModelId { get; set; }
    public int ArcCount { get; set; }
    public int[] DimensionIds { get; set; } = [];
    public string? Error { get; set; }
}
