using Tekla.Structures.Drawing;

namespace TeklaMcpServer.Api.Drawing;

internal static class FallbackMarkGeometryBuilder
{
    public static MarkGeometryInfo Build(Mark mark)
    {
        if (MarkBodyGeometryCollector.TryCollectBodyPolygon(mark, out var polygon))
            return MarkGeometryFactory.BuildFromPolygon(polygon, "ChildObjectGeometryFallback", isReliable: false);

        return MarkGeometryFactory.BuildFromInsertionPoint(
            mark.InsertionPoint.X,
            mark.InsertionPoint.Y,
            "InsertionPointFallback",
            isReliable: false);
    }
}
