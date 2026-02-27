using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing;

public interface IDrawingQueryApi
{
    IReadOnlyList<DrawingInfo> ListDrawings();

    IReadOnlyList<DrawingInfo> FindDrawings(string? nameContains = null, string? markContains = null);
}
