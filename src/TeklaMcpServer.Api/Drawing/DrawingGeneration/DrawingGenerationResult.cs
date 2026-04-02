using System.Collections.Generic;
using TeklaMcpServer.Api.Drawing.ViewDefinitions;

namespace TeklaMcpServer.Api.Drawing.DrawingGeneration;

public sealed class DrawingGenerationResult
{
    public bool Success { get; set; }
    public DrawingGenerationKind Kind { get; set; }
    public string? Error { get; set; }
    public List<string> Warnings { get; set; } = new();
    public DrawingCreationResult? Drawing { get; set; }
    public GaDrawingCreationResult? GaDrawing { get; set; }
    public DrawingViewPreset? ResolvedViewPreset { get; set; }
}
