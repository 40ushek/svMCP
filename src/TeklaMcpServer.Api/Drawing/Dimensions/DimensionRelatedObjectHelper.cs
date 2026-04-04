using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;

namespace TeklaMcpServer.Api.Drawing;

internal static class DimensionRelatedObjectHelper
{
    public static bool TryGetRelatedObjectId(object? relatedObject, out int id)
    {
        id = 0;
        if (relatedObject == null)
            return false;

        if (relatedObject is Tekla.Structures.Drawing.ModelObject drawingModelObject)
        {
            try
            {
                var modelId = drawingModelObject.ModelIdentifier.ID;
                if (modelId > 0)
                {
                    id = modelId;
                    return true;
                }
            }
            catch
            {
            }
        }

        if (relatedObject is DrawingObject drawingObject)
        {
            try
            {
                var drawingId = drawingObject.GetIdentifier().ID;
                if (drawingId > 0)
                {
                    id = drawingId;
                    return true;
                }
            }
            catch
            {
            }
        }

        return false;
    }
}
