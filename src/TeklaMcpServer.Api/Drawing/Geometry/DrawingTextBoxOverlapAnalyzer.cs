using System.Collections.Generic;
using TeklaMcpServer.Api.Algorithms.Geometry;

namespace TeklaMcpServer.Api.Drawing;

public sealed class DrawingTextBoxOverlap
{
    public DrawingTextBox First { get; set; } = new();

    public DrawingTextBox Second { get; set; } = new();

    public double SeparationAxisX { get; set; }

    public double SeparationAxisY { get; set; }

    public double SeparationDepth { get; set; }
}

public static class DrawingTextBoxOverlapAnalyzer
{
    public static List<DrawingTextBoxOverlap> Analyze(IReadOnlyList<DrawingTextBox> boxes)
    {
        var result = new List<DrawingTextBoxOverlap>();

        for (var i = 0; i < boxes.Count; i++)
        for (var j = i + 1; j < boxes.Count; j++)
        {
            if (TryAnalyzePair(boxes[i], boxes[j], out var overlap))
                result.Add(overlap);
        }

        return result;
    }

    public static bool TryAnalyzePair(
        DrawingTextBox first,
        DrawingTextBox second,
        out DrawingTextBoxOverlap overlap)
    {
        overlap = new DrawingTextBoxOverlap();

        if (!MightOverlapByBounds(first, second))
            return false;

        if (first.Polygon.Count < 3 || second.Polygon.Count < 3)
            return false;

        if (!PolygonGeometry.TryGetMinimumTranslationVector(
                first.Polygon,
                second.Polygon,
                out var axisX,
                out var axisY,
                out var depth))
            return false;

        overlap = new DrawingTextBoxOverlap
        {
            First = first,
            Second = second,
            SeparationAxisX = axisX,
            SeparationAxisY = axisY,
            SeparationDepth = depth
        };
        return true;
    }

    public static bool MightOverlapByBounds(DrawingTextBox first, DrawingTextBox second) =>
        first.MaxX > second.MinX &&
        second.MaxX > first.MinX &&
        first.MaxY > second.MinY &&
        second.MaxY > first.MinY;
}
