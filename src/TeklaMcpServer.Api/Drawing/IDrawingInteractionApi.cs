using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing;

public interface IDrawingInteractionApi
{
    SelectDrawingObjectsResult SelectObjectsByModelIds(IReadOnlyCollection<int> targetModelIds);
    FilterDrawingObjectsResult FilterObjects(string objectType, string specificType);
    DrawingContextResult GetDrawingContext();
}
