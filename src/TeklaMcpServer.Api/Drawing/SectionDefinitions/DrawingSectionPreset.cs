namespace TeklaMcpServer.Api.Drawing.SectionDefinitions;

public sealed class DrawingSectionPreset
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DrawingSectionDefinitionSet DefinitionSet { get; set; } = new();
}
