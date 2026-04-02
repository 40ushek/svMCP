using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing.SectionDefinitions;

public sealed class GetSectionDefinitionPresetResult
{
    public bool Success { get; set; }
    public DrawingSectionDefinitionScope Scope { get; set; }
    public string? Error { get; set; }
    public List<string> Warnings { get; set; } = new();
    public DrawingSectionPreset? Preset { get; set; }
}
