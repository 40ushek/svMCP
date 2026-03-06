namespace TeklaMcpServer.Api.Drawing;

public interface IDrawingCreationApi
{
    DrawingCreationResult CreateSinglePartDrawing(int modelObjectId, string drawingProperties, bool openDrawing);

    DrawingCreationResult CreateAssemblyDrawing(int modelObjectId, string drawingProperties, bool openDrawing);
}
