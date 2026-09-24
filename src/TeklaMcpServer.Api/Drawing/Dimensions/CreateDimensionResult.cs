using TeklaMcpServer.Api.Drawing.Dimensions;

namespace TeklaMcpServer.Api.Drawing;

public sealed class CreateDimensionResult
{
    public DimensionWriteState? WriteState { get; set; }
    public bool    Created     { get; set; }
    public int     DimensionId { get; set; }
    public int     ViewId      { get; set; }
    public int     PointCount  { get; set; }
    public double? DistanceUsed { get; set; }
    public DimensionPlacementCalculation? Placement { get; set; }
    public string? Error       { get; set; }
}
