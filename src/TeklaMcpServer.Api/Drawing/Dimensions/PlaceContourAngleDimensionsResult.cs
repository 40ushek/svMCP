namespace TeklaMcpServer.Api.Drawing;

public sealed class PlaceContourAngleDimensionsResult
{
    public bool Created { get; set; }
    public int CreatedCount { get; set; }
    public int ViewId { get; set; }
    public string ViewType { get; set; } = string.Empty;
    public int ModelId { get; set; }
    public int ContourPointCount { get; set; }
    public bool Flipped { get; set; }
    public int[] DimensionIds { get; set; } = [];
    public string? Error { get; set; }
}
