namespace TeklaMcpServer.Api.Drawing.MarkDefinitions;

public sealed class DrawingMarkPreset
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DrawingMarkDefinitionSet DefinitionSet { get; set; } = new();
}
