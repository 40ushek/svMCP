using TeklaMcpServer.Api.Algorithms.Geometry;

namespace TeklaMcpServer.Api.Drawing;

internal static class MarkGeometryMath
{
    public static bool PolygonsIntersect(IReadOnlyList<double[]> first, IReadOnlyList<double[]> second)
    {
        return PolygonGeometry.Intersects(first, second);
    }

    public static bool TryGetMinimumTranslationVector(
        IReadOnlyList<double[]> first,
        IReadOnlyList<double[]> second,
        out double axisX,
        out double axisY,
        out double depth)
    {
        return PolygonGeometry.TryGetMinimumTranslationVector(first, second, out axisX, out axisY, out depth);
    }

    public static List<double[]> TranslateLocalCorners(IReadOnlyList<double[]> localCorners, double centerX, double centerY)
    {
        return PolygonGeometry.Translate(localCorners, centerX, centerY);
    }

    public static void GetPolygonBounds(
        IReadOnlyList<double[]> polygon,
        out double minX,
        out double minY,
        out double maxX,
        out double maxY)
    {
        PolygonGeometry.GetBounds(polygon, out minX, out minY, out maxX, out maxY);
    }

    public static bool RectanglesOverlap(
        double firstMinX,
        double firstMinY,
        double firstMaxX,
        double firstMaxY,
        double secondMinX,
        double secondMinY,
        double secondMaxX,
        double secondMaxY)
    {
        return PolygonGeometry.RectanglesOverlap(firstMinX, firstMinY, firstMaxX, firstMaxY, secondMinX, secondMinY, secondMaxX, secondMaxY);
    }
}
