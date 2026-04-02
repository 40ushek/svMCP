namespace TeklaMcpServer.Api.Drawing.ViewDefinitions;

public sealed class DrawingViewPreset
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DrawingViewDefinitionSet DefinitionSet { get; set; } = new();
}
