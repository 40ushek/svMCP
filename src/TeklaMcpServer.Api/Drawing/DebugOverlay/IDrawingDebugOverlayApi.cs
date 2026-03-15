namespace TeklaMcpServer.Api.Drawing;

public interface IDrawingDebugOverlayApi
{
    DrawingDebugOverlayResult DrawOverlay(string requestJson);
    ClearDrawingDebugOverlayResult ClearOverlay(string? group);
}
