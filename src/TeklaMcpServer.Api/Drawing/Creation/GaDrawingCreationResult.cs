namespace TeklaMcpServer.Api.Drawing;

public sealed class GaDrawingCreationResult
{
    public bool Created { get; set; }
    public string ErrorDetails { get; set; } = string.Empty;
    public string ViewName { get; set; } = string.Empty;
    public string DrawingProperties { get; set; } = string.Empty;
    public bool OpenDrawing { get; set; }
}
