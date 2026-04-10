using Tekla.Structures.Drawing;

namespace TeklaMcpServer.Api.Drawing;

internal static class LeaderLineMarkGeometryBuilder
{
    public static MarkGeometryInfo Build(Mark mark)
    {
        return MarkGeometryFactory.BuildFromObjectAlignedBox(
            mark.GetObjectAlignedBoundingBox(),
            "ObjectAlignedBoundingBox",
            isReliable: true);
    }
}
