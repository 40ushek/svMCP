namespace TeklaMcpServer.Api.Drawing;

public sealed class DrawingOperationResult
{
    public bool Found { get; set; }

    public bool Succeeded { get; set; }

    public string RequestedGuid { get; set; } = string.Empty;

    public DrawingInfo Drawing { get; set; } = new();
}
