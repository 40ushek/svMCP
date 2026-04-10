using Tekla.Structures.Drawing;

namespace TeklaMcpServer.Api.Drawing;

internal static class LeaderLineMarkGeometryBuilder
{
    public static MarkGeometryInfo Build(Mark mark)
    {
        if (MarkBodyGeometryCollector.TryCollectBodyPolygon(mark, out var polygon))
            return MarkGeometryFactory.BuildFromPolygon(polygon, "ChildObjectGeometry", isReliable: true);

        return MarkGeometryFactory.BuildFromInsertionPoint(
            mark.InsertionPoint.X,
            mark.InsertionPoint.Y,
            "InsertionPointFallback",
            isReliable: false);
    }
}
