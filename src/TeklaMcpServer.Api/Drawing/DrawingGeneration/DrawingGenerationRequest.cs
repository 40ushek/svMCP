namespace TeklaMcpServer.Api.Drawing.DrawingGeneration;

public sealed class DrawingGenerationRequest
{
    public DrawingGenerationKind Kind { get; set; }
    public int? ModelObjectId { get; set; }
    public string? ViewName { get; set; }
    public string DrawingProperties { get; set; } = string.Empty;
    public bool OpenDrawing { get; set; }
    public bool ResolveDefaultViewPreset { get; set; } = true;
}
