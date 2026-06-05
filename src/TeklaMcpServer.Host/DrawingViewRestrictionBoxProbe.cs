using System;
using System.Collections.Generic;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Geometry3d;
using TeklaMcpServer.Api.Drawing;

namespace TeklaMcpServer.Host;

internal sealed class DrawingViewRestrictionBoxProbe
{
    public void Run()
    {
        var drawingHandler = new DrawingHandler();
        var activeDrawing = drawingHandler.GetActiveDrawing()
            ?? throw new InvalidOperationException("No active drawing. Open a drawing in Tekla and try again.");

        var view = ResolveTargetView(drawingHandler, activeDrawing);
        var boxes = GetViewRestrictionBoxes(view);
        var shortening = ViewShorteningAttributesReader.Read(view);
        var mapper = ViewShorteningCoordinateMapper.FromAabbs(
            boxes,
            shortening.SpaceBetweenCutPartsInViewCoordinates);

        Console.WriteLine($"Drawing: {activeDrawing.Name}");
        Console.WriteLine($"View id: {ResolveViewId(view)}");
        Console.WriteLine($"View name: {view.Name}");
        Console.WriteLine($"View scale: {shortening.ViewScale}");
        Console.WriteLine(
            $"Shortening attributes: cutParts={shortening.CutParts} " +
            $"cutSkewParts={shortening.CutSkewParts} " +
            $"minimumLength={shortening.MinimumLength:0.###} " +
            $"offset={shortening.Offset:0.###} " +
            $"cutPartType={shortening.CutPartType}");
        Console.WriteLine($"Space between cut parts in paper coordinates: {shortening.SpaceBetweenCutParts:0.###}");
        Console.WriteLine($"Space between cut parts in view coordinates: {shortening.SpaceBetweenCutPartsInViewCoordinates:0.###}");
        Console.WriteLine($"Visible area restriction boxes: {boxes.Count}");
        for (var i = 0; i < boxes.Count; i++)
        {
            var box = boxes[i];
            Console.WriteLine(
                $"  box[{i}]: min=({box.MinPoint.X:0.###}, {box.MinPoint.Y:0.###}, {box.MinPoint.Z:0.###}) " +
                $"max=({box.MaxPoint.X:0.###}, {box.MaxPoint.Y:0.###}, {box.MaxPoint.Z:0.###}) " +
                $"size=({box.MaxPoint.X - box.MinPoint.X:0.###} x {box.MaxPoint.Y - box.MinPoint.Y:0.###})");
        }

        Console.WriteLine($"Shortening in X: {mapper.HasShorteningX}");
        Console.WriteLine($"Shortening in Y: {mapper.HasShorteningY}");

        WriteAxisMap("X", mapper.XIntervals);
        WriteAxisMap("Y", mapper.YIntervals);

        DrawRestrictionBoxes(activeDrawing, view, boxes, mapper);
        DrawDimensionTextBoxComparison(activeDrawing, view, mapper);
        DrawMarkGeometryComparison(activeDrawing, view, mapper);
    }

    private static View ResolveTargetView(DrawingHandler drawingHandler, Drawing activeDrawing)
    {
        var selected = drawingHandler.GetDrawingObjectSelector().GetSelected();
        while (selected.MoveNext())
        {
            if (selected.Current is View selectedView)
                return selectedView;
        }

        var views = new List<View>();
        var viewEnumerator = activeDrawing.GetSheet().GetViews();
        while (viewEnumerator.MoveNext())
        {
            if (viewEnumerator.Current is View view)
                views.Add(view);
        }

        if (views.Count == 1)
            return views[0];

        if (views.Count == 0)
            throw new InvalidOperationException("No views found in the active drawing.");

        throw new InvalidOperationException(
            $"Active drawing contains {views.Count} views. Select one view in the drawing editor and run the host again.");
    }

    private static List<AABB> GetViewRestrictionBoxes(View view)
    {
        var result = new List<AABB>();
        var boxes = view.GetVisibleAreaRestrictionBoxes();
        while (boxes.MoveNext())
        {
            if (boxes.Current is AABB box)
                result.Add(box);
        }

        return result;
    }

    private static void DrawRestrictionBoxes(
        Drawing activeDrawing,
        View view,
        IReadOnlyList<AABB> boxes,
        ViewShorteningCoordinateMapper mapper)
    {
        var rawInserted = 0;
        var convertedInserted = 0;
        foreach (var box in boxes)
        {
            if (box.MaxPoint.X <= box.MinPoint.X || box.MaxPoint.Y <= box.MinPoint.Y)
                continue;

            if (DrawBox(view, box, DrawingColors.Green))
                rawInserted++;

            if (mapper.HasShortening && DrawConvertedBox(view, box, mapper, DrawingColors.Blue))
                convertedInserted++;
        }

        if (rawInserted > 0 || convertedInserted > 0)
            activeDrawing.CommitChanges();

        Console.WriteLine($"Drawn raw visible area restriction boxes: {rawInserted}");
        Console.WriteLine($"Drawn converted visible area restriction boxes: {convertedInserted}");
    }

    private static bool DrawBox(View view, AABB box, DrawingColors color)
    {
        var rectangle = new Rectangle(
            view,
            new Point(box.MinPoint.X, box.MinPoint.Y, 0.0),
            new Point(box.MaxPoint.X, box.MaxPoint.Y, 0.0));
        rectangle.Attributes.Line.Color = color;
        return rectangle.Insert();
    }

    private static bool DrawConvertedBox(
        View view,
        AABB box,
        ViewShorteningCoordinateMapper mapper,
        DrawingColors color)
    {
        var polygon = new List<double[]>
        {
            new[] { box.MinPoint.X, box.MinPoint.Y },
            new[] { box.MaxPoint.X, box.MinPoint.Y },
            new[] { box.MaxPoint.X, box.MaxPoint.Y },
            new[] { box.MinPoint.X, box.MaxPoint.Y }
        };

        return DrawPolygon(view, mapper.ConvertPolygon(polygon), color);
    }

    private static void DrawDimensionTextBoxComparison(
        Drawing activeDrawing,
        View view,
        ViewShorteningCoordinateMapper mapper)
    {
        var textBoxes = DimensionDrawingTextBoxDebugReader.Collect(view);

        var rawInserted = 0;
        var convertedInserted = 0;
        foreach (var textBox in textBoxes)
        {
            if (textBox.Polygon.Count < 3)
                continue;

            if (DrawPolygon(view, textBox.Polygon, DrawingColors.Red))
                rawInserted++;

            if (mapper.HasShortening)
            {
                var convertedPolygon = mapper.ConvertPolygonToRaw(textBox.Polygon);
                if (DrawPolygon(view, convertedPolygon, DrawingColors.Magenta))
                    convertedInserted++;
            }
        }

        if (rawInserted > 0 || convertedInserted > 0)
            activeDrawing.CommitChanges();

        Console.WriteLine($"Dimension text boxes: {textBoxes.Count}");
        Console.WriteLine($"Drawn raw dimension text boxes: {rawInserted}");
        Console.WriteLine($"Drawn converted dimension text boxes: {convertedInserted}");
    }

    private static void DrawMarkGeometryComparison(
        Drawing activeDrawing,
        View view,
        ViewShorteningCoordinateMapper mapper)
    {
        var marks = MarkDrawingGeometryDebugReader.Collect(view);

        var rawInserted = 0;
        var convertedInserted = 0;
        foreach (var mark in marks)
        {
            if (mark.Geometry.Corners.Count >= 3 && DrawPolygon(view, mark.Geometry.Corners, DrawingColors.Black))
                rawInserted++;

            if (DrawPointMarker(view, mark.InsertionX, mark.InsertionY, 2.0, DrawingColors.Black))
                rawInserted++;

            if (!mapper.HasShortening)
                continue;

            if (mark.Geometry.Corners.Count >= 3)
            {
                var convertedPolygon = mapper.ConvertPolygonToRaw(mark.Geometry.Corners);
                if (DrawPolygon(view, convertedPolygon, DrawingColors.Blue))
                    convertedInserted++;
            }

            mapper.ConvertPointToRaw(mark.InsertionX, mark.InsertionY, out var convertedX, out var convertedY);
            if (DrawPointMarker(view, convertedX, convertedY, 2.0, DrawingColors.Blue))
                convertedInserted++;
        }

        if (rawInserted > 0 || convertedInserted > 0)
            activeDrawing.CommitChanges();

        Console.WriteLine($"Marks: {marks.Count}");
        Console.WriteLine($"Drawn raw mark geometry items: {rawInserted}");
        Console.WriteLine($"Drawn converted mark geometry items: {convertedInserted}");
    }

    private static bool DrawPolygon(View view, IReadOnlyList<double[]> polygon, DrawingColors color)
    {
        if (polygon.Count < 3)
            return false;

        var points = new PointList();
        foreach (var point in polygon)
            points.Add(new Point(point[0], point[1], 0.0));

        var first = polygon[0];
        var last = polygon[polygon.Count - 1];
        if (!NearlyEqual(first[0], last[0]) || !NearlyEqual(first[1], last[1]))
            points.Add(new Point(first[0], first[1], 0.0));

        var polyline = new Polyline(view, points);
        polyline.Attributes.Line.Color = color;
        return polyline.Insert();
    }

    private static bool DrawPointMarker(View view, double x, double y, double halfSize, DrawingColors color)
    {
        var rectangle = new Rectangle(
            view,
            new Point(x - halfSize, y - halfSize, 0.0),
            new Point(x + halfSize, y + halfSize, 0.0));
        rectangle.Attributes.Line.Color = color;
        return rectangle.Insert();
    }

    private static void WriteAxisMap(string axisName, IReadOnlyList<ViewShorteningInterval> intervals)
    {
        Console.WriteLine($"{axisName} visible intervals: {intervals.Count}");
        for (var i = 0; i < intervals.Count; i++)
            Console.WriteLine($"  {axisName}[{i}]: {intervals[i].Min:0.###}..{intervals[i].Max:0.###}");

        if (intervals.Count <= 1)
            return;

        Console.WriteLine($"{axisName} gaps:");
        for (var i = 1; i < intervals.Count; i++)
        {
            var gapStart = intervals[i - 1].Max;
            var gapEnd = intervals[i].Min;
            Console.WriteLine($"  gap[{i - 1}]: {gapStart:0.###}..{gapEnd:0.###} size={gapEnd - gapStart:0.###}");
        }
    }

    private static bool NearlyEqual(double a, double b)
        => Math.Abs(a - b) <= 1e-6;

    private static string ResolveViewId(View view)
    {
        try
        {
            return view.GetIdentifier().ID.ToString();
        }
        catch
        {
            return "<unknown>";
        }
    }
}
