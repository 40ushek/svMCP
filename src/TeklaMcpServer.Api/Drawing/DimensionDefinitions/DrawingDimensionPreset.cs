namespace TeklaMcpServer.Api.Drawing.DimensionDefinitions;

public sealed class DrawingDimensionPreset
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DrawingDimensionDefinitionSet DefinitionSet { get; set; } = new();
}
