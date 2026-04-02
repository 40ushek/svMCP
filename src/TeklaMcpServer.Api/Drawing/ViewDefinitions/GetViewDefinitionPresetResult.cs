namespace TeklaMcpServer.Api.Drawing.ViewDefinitions;

public sealed class GetViewDefinitionPresetResult
{
    public bool Success { get; set; }
    public DrawingViewDefinitionScope Scope { get; set; }
    public string? Error { get; set; }
    public List<string> Warnings { get; set; } = new();
    public DrawingViewPreset? Preset { get; set; }
}
