namespace TeklaMcpServer.Api.Drawing;

public sealed class DrawingCreationResult
{
    public bool Created { get; set; }

    public bool Opened { get; set; }

    public int DrawingId { get; set; }

    public string DrawingType { get; set; } = string.Empty;

    public int ModelObjectId { get; set; }

    public string DrawingProperties { get; set; } = string.Empty;
}
