namespace TeklaMcpServer.Api.Drawing;

public interface IDrawingCreationApi
{
    GaDrawingCreationResult CreateGaDrawing(string viewName, string drawingProperties, bool openDrawing);

    DrawingCreationResult CreateSinglePartDrawing(int modelObjectId, string drawingProperties, bool openDrawing);

    DrawingCreationResult CreateAssemblyDrawing(int modelObjectId, string drawingProperties, bool openDrawing);
}
