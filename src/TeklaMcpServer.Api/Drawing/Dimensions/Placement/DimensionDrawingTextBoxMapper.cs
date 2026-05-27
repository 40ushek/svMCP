using System.Collections.Generic;
using System.Linq;
using TeklaMcpServer.Api.Algorithms.Geometry;

namespace TeklaMcpServer.Api.Drawing;

internal static class DimensionDrawingTextBoxMapper
{
    internal static DrawingTextBox ToDrawingTextBox(
        DimensionPresentationTextBox source,
        int textIndex)
    {
        var polygon = source.Polygon
            .Select(static point => new[] { point[0], point[1] })
            .ToList();

        GetBounds(polygon, source, out var minX, out var minY, out var maxX, out var maxY);

        return new DrawingTextBox
        {
            SourceKind = DrawingTextBoxSourceKind.Dimension,
            SourceObjectId = source.SourceObjectId,
            TextIndex = textIndex,
            Text = source.Text,
            CenterX = (minX + maxX) / 2.0,
            CenterY = (minY + maxY) / 2.0,
            Width = source.ViewWidth,
            Height = source.ViewHeight,
            MinX = minX,
            MinY = minY,
            MaxX = maxX,
            MaxY = maxY,
            Polygon = polygon
        };
    }

    internal static List<DrawingTextBox> ToDrawingTextBoxes(
        IReadOnlyList<DimensionPresentationTextBox> sources)
    {
        var result = new List<DrawingTextBox>(sources.Count);
        for (var i = 0; i < sources.Count; i++)
            result.Add(ToDrawingTextBox(sources[i], i));
        return result;
    }

    private static void GetBounds(
        IReadOnlyList<double[]> polygon,
        DimensionPresentationTextBox source,
        out double minX,
        out double minY,
        out double maxX,
        out double maxY)
    {
        if (polygon.Count >= 3)
        {
            PolygonGeometry.GetBounds(polygon, out minX, out minY, out maxX, out maxY);
            return;
        }

        minX = source.ViewPositionX;
        minY = source.ViewPositionY;
        maxX = source.ViewPositionX;
        maxY = source.ViewPositionY;
    }
}
