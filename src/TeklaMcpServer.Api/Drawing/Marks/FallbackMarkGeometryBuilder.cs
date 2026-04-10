using Tekla.Structures.Drawing;

namespace TeklaMcpServer.Api.Drawing;

internal static class FallbackMarkGeometryBuilder
{
    public static MarkGeometryInfo Build(Mark mark)
    {
        return MarkGeometryFactory.BuildFromObjectAlignedBox(
            mark.GetObjectAlignedBoundingBox(),
            "ObjectAlignedBoundingBoxFallback",
            isReliable: false);
    }
}
