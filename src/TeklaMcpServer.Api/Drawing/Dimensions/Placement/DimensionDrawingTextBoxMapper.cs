using System.Collections.Generic;
using System.Linq;
using TeklaMcpServer.Api.Algorithms.Geometry;

namespace TeklaMcpServer.Api.Drawing;

internal static class DimensionDrawingTextBoxMapper
{
    internal static DrawingTextBox ToDrawingTextBox(
        DimensionPresentationTextBox source,
        int textIndex,
        ViewShorteningCoordinateMapper? shorteningMapper = null,
        DimensionTextBoxShorteningMode shorteningMode = DimensionTextBoxShorteningMode.None)
    {
        var rawPolygon = source.Polygon
            .Select(static point => new[] { point[0], point[1] })
            .ToList();

        List<double[]> polygon;
        double centerX;
        double centerY;
        if (shorteningMapper != null && shorteningMapper.HasShortening && shorteningMode != DimensionTextBoxShorteningMode.None)
        {
            switch (shorteningMode)
            {
                case DimensionTextBoxShorteningMode.ToVisual:
                    polygon = shorteningMapper.ConvertPolygon(rawPolygon);
                    shorteningMapper.ConvertPoint(source.CenterX, source.CenterY, out centerX, out centerY);
                    break;
                case DimensionTextBoxShorteningMode.ToRaw:
                    polygon = shorteningMapper.ConvertPolygonToRaw(rawPolygon);
                    shorteningMapper.ConvertPointToRaw(source.CenterX, source.CenterY, out centerX, out centerY);
                    break;
                default:
                    polygon = rawPolygon;
                    centerX = source.CenterX;
                    centerY = source.CenterY;
                    break;
            }
        }
        else
        {
            polygon = rawPolygon;
            centerX = source.CenterX;
            centerY = source.CenterY;
        }

        GetBounds(polygon, source, out var minX, out var minY, out var maxX, out var maxY);

        return new DrawingTextBox
        {
            SourceKind = DrawingTextBoxSourceKind.Dimension,
            SourceObjectId = source.SourceObjectId,
            SourceObjectKind = source.SourceObjectKind,
            TextIndex = textIndex,
            Text = source.Text,
            CenterX = centerX,
            CenterY = centerY,
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
        IReadOnlyList<DimensionPresentationTextBox> sources,
        ViewShorteningCoordinateMapper? shorteningMapper = null,
        DimensionTextBoxShorteningMode shorteningMode = DimensionTextBoxShorteningMode.None)
    {
        var result = new List<DrawingTextBox>(sources.Count);
        for (var i = 0; i < sources.Count; i++)
            result.Add(ToDrawingTextBox(sources[i], i, shorteningMapper, shorteningMode));
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

        // Polygon-less fallback: ViewPositionX/Y are raw view coordinates straight from
        // TextPrimitive.Position * scale. They are NOT shortening-converted here because
        // the polygon-less branch is only reached for degenerate (zero-size) text whose
        // exact placement is not used as a layout blocker — this stays at the raw insert
        // point as a conservative reference.
        minX = source.ViewPositionX;
        minY = source.ViewPositionY;
        maxX = source.ViewPositionX;
        maxY = source.ViewPositionY;
    }
}
