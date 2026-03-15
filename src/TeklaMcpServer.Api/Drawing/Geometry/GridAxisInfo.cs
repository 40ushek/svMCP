namespace TeklaMcpServer.Api.Drawing;

public sealed class GridAxisInfo
{
    public string? Guid     { get; set; }
    public string Label     { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty; // "X" (vertical line) | "Y" (horizontal line) | "other"
    public double StartX    { get; set; }
    public double StartY    { get; set; }
    public double EndX      { get; set; }
    public double EndY      { get; set; }
    /// <summary>Position along the perpendicular axis (X for vertical lines, Y for horizontal lines).</summary>
    public double Coordinate { get; set; }
}

public sealed class GetGridAxesResult
{
    public bool              Success  { get; set; }
    public string?           Error    { get; set; }
    public int               ViewId   { get; set; }
    public List<GridAxisInfo> Axes    { get; set; } = new();
}
