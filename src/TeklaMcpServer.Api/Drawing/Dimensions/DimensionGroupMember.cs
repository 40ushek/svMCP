namespace TeklaMcpServer.Api.Drawing;

internal sealed class DimensionGroupMember
{
    public int DimensionId { get; set; }
    public double Distance { get; set; }
    public double SortKey { get; set; }
    public double DirectionX { get; set; }
    public double DirectionY { get; set; }
    public int TopDirection { get; set; }
    public DrawingBoundsInfo? Bounds { get; set; }
    public DrawingLineInfo? ReferenceLine { get; set; }
    public DrawingLineInfo? LeadLineMain { get; set; }
    public DrawingLineInfo? LeadLineSecond { get; set; }
    public DrawingDimensionInfo Dimension { get; set; } = new();
}
