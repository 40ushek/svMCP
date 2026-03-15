namespace TeklaMcpServer.Api.Drawing;

public sealed class DrawingViewInfo
{
    public int    Id       { get; set; }
    public string ViewType { get; set; } = string.Empty;
    public string Name     { get; set; } = string.Empty;
    public double OriginX  { get; set; }
    public double OriginY  { get; set; }
    public double Scale    { get; set; }
    public double Width    { get; set; }
    public double Height   { get; set; }
}
