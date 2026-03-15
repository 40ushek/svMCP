using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing;

public interface IDrawingQueryApi
{
    IReadOnlyList<DrawingInfo> ListDrawings();

    IReadOnlyList<DrawingInfo> FindDrawings(string? nameContains = null, string? markContains = null);

    IReadOnlyList<DrawingInfo> FindDrawingsByProperties(IReadOnlyCollection<DrawingPropertyFilter> filters);

    OpenDrawingResult OpenDrawing(System.Guid drawingGuid);

    CloseDrawingResult CloseActiveDrawing();

    ExportDrawingsPdfResult ExportDrawingsPdf(IReadOnlyCollection<string> drawingGuids, string outputDirectory);
}
