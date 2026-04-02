using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing.DimensionDefinitions;

public sealed class GetDimensionDefinitionPresetResult
{
    public bool Success { get; set; }
    public DrawingDimensionDefinitionScope Scope { get; set; }
    public string? Error { get; set; }
    public List<string> Warnings { get; set; } = new();
    public DrawingDimensionPreset? Preset { get; set; }
}
