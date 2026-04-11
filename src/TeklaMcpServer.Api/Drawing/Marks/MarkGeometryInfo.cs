namespace TeklaMcpServer.Api.Drawing;

public sealed class MarkGeometryInfo
{
    public double CenterX { get; set; }
    public double CenterY { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double MinX { get; set; }
    public double MinY { get; set; }
    public double MaxX { get; set; }
    public double MaxY { get; set; }
    public double AngleDeg { get; set; }
    public double AxisDx { get; set; }
    public double AxisDy { get; set; }
    public bool HasAxis { get; set; }
    public bool IsReliable { get; set; }
    public string Source { get; set; } = string.Empty;
    public List<double[]> Corners { get; set; } = new();
}
