using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing.MarkDefinitions;

public sealed class GetMarkDefinitionPresetResult
{
    public bool Success { get; set; }
    public DrawingMarkDefinitionScope Scope { get; set; }
    public string? Error { get; set; }
    public List<string> Warnings { get; set; } = new();
    public DrawingMarkPreset? Preset { get; set; }
}
