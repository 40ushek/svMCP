namespace TeklaMcpServer.Api.Drawing.ViewDefinitions;

public sealed class DrawingViewDefinitionSet
{
    public DrawingViewDefinitionScope Scope { get; set; }
    public DrawingViewCreationMode CreationMode { get; set; } = DrawingViewCreationMode.AlongAxis;
    public DrawingViewOrientationPolicy Orientation { get; set; } = new();
    public DrawingViewVisibilityPolicy Visibility { get; set; } = new();
    public DrawingViewSheetPolicy Sheet { get; set; } = new();
    public List<DrawingViewDefinition> Views { get; set; } = new();
}
