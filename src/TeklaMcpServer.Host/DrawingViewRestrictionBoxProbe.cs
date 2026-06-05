using System;
using System.Collections.Generic;
using System.IO;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Geometry3d;
using TeklaMcpServer.Api.Drawing;

namespace TeklaMcpServer.Host;

internal sealed class DrawingViewRestrictionBoxProbe
{
    private static readonly List<string> LogLines = new();

    public void Run()
    {
        LogLines.Clear();

        var drawingHandler = new DrawingHandler();
        var activeDrawing = drawingHandler.GetActiveDrawing()
            ?? throw new InvalidOperationException("No active drawing. Open a drawing in Tekla and try again.");

        var view = ResolveTargetView(drawingHandler, activeDrawing);
        var boxes = GetViewRestrictionBoxes(view);
        var shortening = ViewShorteningAttributesReader.Read(view);
        var mapper = ViewShorteningCoordinateMapper.FromAabbs(
            boxes,
            shortening.SpaceBetweenCutPartsInViewCoordinates);

        Log($"Drawing: {activeDrawing.Name}");
        Log($"Drawing type: {activeDrawing.GetType().Name}");
        Log($"View id: {ResolveViewId(view)}");
        Log($"View name: {view.Name}");
        Log($"View type: {view.ViewType}");
        Log($"View scale: {shortening.ViewScale}");
        Log(
            $"Shortening attributes: cutParts={shortening.CutParts} " +
            $"cutSkewParts={shortening.CutSkewParts} " +
            $"minimumLength={shortening.MinimumLength:0.###} " +
            $"offset={shortening.Offset:0.###} " +
            $"cutPartType={shortening.CutPartType}");
        Log($"Space between cut parts in paper coordinates: {shortening.SpaceBetweenCutParts:0.###}");
        Log($"Space between cut parts in view coordinates: {shortening.SpaceBetweenCutPartsInViewCoordinates:0.###}");
        Log($"Visible area restriction boxes: {boxes.Count}");
        for (var i = 0; i < boxes.Count; i++)
        {
            var box = boxes[i];
            Log(
                $"  box[{i}]: min=({box.MinPoint.X:0.###}, {box.MinPoint.Y:0.###}, {box.MinPoint.Z:0.###}) " +
                $"max=({box.MaxPoint.X:0.###}, {box.MaxPoint.Y:0.###}, {box.MaxPoint.Z:0.###}) " +
                $"size=({box.MaxPoint.X - box.MinPoint.X:0.###} x {box.MaxPoint.Y - box.MinPoint.Y:0.###})");
        }

        Log($"Shortening in X: {mapper.HasShorteningX}");
        Log($"Shortening in Y: {mapper.HasShorteningY}");

        WriteAxisMap("X", mapper.XIntervals);
        WriteAxisMap("Y", mapper.YIntervals);

        DrawDimensionTextBoxes(activeDrawing, view, mapper);
        DrawMarkGeometry(activeDrawing, view);
        DrawPartBlockers(activeDrawing, view);
        WriteLogFile();
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

    private static void DrawDimensionTextBoxes(
        Drawing activeDrawing,
        View view,
        ViewShorteningCoordinateMapper mapper)
    {
        var textBoxes = DimensionDrawingTextBoxDebugReader.Collect(view);

        var mappedInserted = 0;
        foreach (var textBox in textBoxes)
        {
            if (textBox.Polygon.Count < 3)
                continue;

            var mappedPolygon = mapper.HasShortening
                ? mapper.ConvertPolygonToRaw(textBox.Polygon)
                : textBox.Polygon;
            if (DrawPolygon(view, mappedPolygon, DrawingColors.Magenta))
                mappedInserted++;
        }

        if (mappedInserted > 0)
            activeDrawing.CommitChanges();

        Log($"Dimension text boxes: {textBoxes.Count}");
        Log($"Drawn mapped dimension text boxes: {mappedInserted}");
    }

    private static void DrawMarkGeometry(Drawing activeDrawing, View view)
    {
        var marks = MarkDrawingGeometryDebugReader.Collect(view);

        var rawInserted = 0;
        foreach (var mark in marks)
        {
            if (mark.Geometry.Corners.Count >= 3 && DrawPolygon(view, mark.Geometry.Corners, DrawingColors.Black))
                rawInserted++;
        }

        if (rawInserted > 0)
            activeDrawing.CommitChanges();

        Log($"Marks: {marks.Count}");
        Log($"Drawn unconverted mark geometry items: {rawInserted}");
    }

    private static void DrawPartBlockers(Drawing activeDrawing, View view)
    {
        var parts = PartBlockerGeometryDebugReader.Collect(view);

        var unconvertedInserted = 0;
        foreach (var part in parts)
        {
            if (part.Polygon.Count < 3)
                continue;

            // Yellow: unconverted polygon as production force-flow consumes it today.
            if (DrawPolygon(view, part.Polygon, DrawingColors.Yellow))
                unconvertedInserted++;
        }

        if (unconvertedInserted > 0)
            activeDrawing.CommitChanges();

        Log($"Part blockers: {parts.Count}");
        Log($"Drawn unconverted part blocker polygons: {unconvertedInserted}");
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

    private static void WriteAxisMap(string axisName, IReadOnlyList<ViewShorteningInterval> intervals)
    {
        Log($"{axisName} visible intervals: {intervals.Count}");
        for (var i = 0; i < intervals.Count; i++)
            Log($"  {axisName}[{i}]: {intervals[i].Min:0.###}..{intervals[i].Max:0.###}");

        if (intervals.Count <= 1)
            return;

        Log($"{axisName} gaps:");
        for (var i = 1; i < intervals.Count; i++)
        {
            var gapStart = intervals[i - 1].Max;
            var gapEnd = intervals[i].Min;
            Log($"  gap[{i - 1}]: {gapStart:0.###}..{gapEnd:0.###} size={gapEnd - gapStart:0.###}");
        }
    }

    private static void Log(string message)
    {
        LogLines.Add($"{DateTime.Now:HH:mm:ss.fff} {message}");
    }

    private static void WriteLogFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "TeklaMcpServer.RestrictionBoxProbe.log");
        File.WriteAllLines(path, LogLines);
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
