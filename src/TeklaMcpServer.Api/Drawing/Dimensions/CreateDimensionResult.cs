namespace TeklaMcpServer.Api.Drawing;

public sealed class CreateDimensionResult
{
    public bool    Created     { get; set; }
    public int     DimensionId { get; set; }
    public int     ViewId      { get; set; }
    public int     PointCount  { get; set; }
    public string? Error       { get; set; }
}
