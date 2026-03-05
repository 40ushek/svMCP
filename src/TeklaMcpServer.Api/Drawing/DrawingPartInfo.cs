namespace TeklaMcpServer.Api.Drawing;

public sealed class DrawingPartInfo
{
    public int    ModelId      { get; set; }
    public string Type        { get; set; } = string.Empty;
    public string PartPos     { get; set; } = string.Empty;
    public string AssemblyPos { get; set; } = string.Empty;
    public string Profile     { get; set; } = string.Empty;
    public string Material    { get; set; } = string.Empty;
    public string Name        { get; set; } = string.Empty;
}

public sealed class GetDrawingPartsResult
{
    public int                    Total { get; set; }
    public System.Collections.Generic.List<DrawingPartInfo> Parts { get; set; } = new();
}
