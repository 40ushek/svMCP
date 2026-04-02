namespace TeklaMcpServer.Api.Drawing.ViewDefinitions;

public sealed class DrawingViewSheetPolicy
{
    public bool AutoSizeEnabled { get; set; }
    public DrawingSheetSizeMode SizeMode { get; set; } = DrawingSheetSizeMode.Disabled;
    public string? PreferredSize { get; set; }
    public List<string> AllowedSizes { get; set; } = new();
}
