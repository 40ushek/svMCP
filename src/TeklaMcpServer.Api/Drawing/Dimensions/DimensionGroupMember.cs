namespace TeklaMcpServer.Api.Drawing;

internal sealed class DimensionGroupMember
{
    public int DimensionId { get; set; }
    public double Distance { get; set; }
    public double SortKey { get; set; }
    public DrawingBoundsInfo? Bounds { get; set; }
    public DrawingDimensionInfo Dimension { get; set; } = new();
}
