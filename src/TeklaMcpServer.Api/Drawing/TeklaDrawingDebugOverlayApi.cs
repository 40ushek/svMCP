using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Geometry3d;

namespace TeklaMcpServer.Api.Drawing;

public sealed class TeklaDrawingDebugOverlayApi : IDrawingDebugOverlayApi
{
    private const string OverlayFlagProperty = "SVMCP_DEBUG_OVERLAY";
    private const string OverlayGroupProperty = "SVMCP_DEBUG_GROUP";
    private const string OverlayKindProperty = "SVMCP_DEBUG_KIND";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public DrawingDebugOverlayResult DrawOverlay(string requestJson)
    {
        var activeDrawing = new DrawingHandler().GetActiveDrawing();
        if (activeDrawing == null)
            throw new DrawingNotOpenException();

        var request = JsonSerializer.Deserialize<DrawingDebugOverlayRequest>(requestJson ?? string.Empty, JsonOptions)
            ?? throw new InvalidOperationException("Invalid debug overlay request JSON.");

        if (request.Shapes.Count == 0)
            return new DrawingDebugOverlayResult { Group = request.Group };

        var result = new DrawingDebugOverlayResult { Group = request.Group };
        if (request.ClearGroupFirst)
        {
            var cleared = ClearOverlay(request.Group);
            result.ClearedCount = cleared.ClearedCount;
        }

        foreach (var shape in request.Shapes)
        {
            var view = ResolveView(activeDrawing, shape.ViewId);
            var created = CreateShape(view, shape);
            if (created == null)
                continue;

            TagOverlayObject(created, request.Group, shape.Kind);
            result.CreatedIds.Add(created.GetIdentifier().ID);
        }

        result.CreatedCount = result.CreatedIds.Count;
        if (result.ClearedCount > 0 || result.CreatedCount > 0)
            activeDrawing.CommitChanges("(MCP) DebugOverlay");

        return result;
    }

    public ClearDrawingDebugOverlayResult ClearOverlay(string? group)
    {
        var activeDrawing = new DrawingHandler().GetActiveDrawing();
        if (activeDrawing == null)
            throw new DrawingNotOpenException();

        var clearedCount = 0;
        string normalizedGroup = string.IsNullOrWhiteSpace(group) ? string.Empty : group!.Trim();
        var objects = activeDrawing.GetSheet().GetAllObjects();
        while (objects.MoveNext())
        {
            if (objects.Current is not DrawingObject drawingObject)
                continue;

            var flag = string.Empty;
            if (!drawingObject.GetUserProperty(OverlayFlagProperty, ref flag) || !string.Equals(flag, "1", StringComparison.Ordinal))
                continue;

            if (normalizedGroup.Length > 0)
            {
                var objectGroup = string.Empty;
                drawingObject.GetUserProperty(OverlayGroupProperty, ref objectGroup);
                if (!string.Equals(objectGroup, normalizedGroup, StringComparison.Ordinal))
                    continue;
            }

            if (!drawingObject.Delete())
                continue;

            clearedCount++;
        }

        if (clearedCount > 0)
            activeDrawing.CommitChanges("(MCP) ClearDebugOverlay");

        return new ClearDrawingDebugOverlayResult
        {
            Group = normalizedGroup,
            ClearedCount = clearedCount
        };
    }

    private static DrawingObject? CreateShape(ViewBase view, DrawingDebugShape shape)
    {
        var kind = (shape.Kind ?? string.Empty).Trim().ToLowerInvariant();
        return kind switch
        {
            "line" => CreateLine(view, shape),
            "rectangle" => CreateRectangle(view, shape),
            "polyline" => CreatePolyline(view, shape),
            "polygon" => CreatePolygon(view, shape),
            "text" => CreateText(view, shape),
            _ => throw new InvalidOperationException($"Unsupported debug overlay shape kind: {shape.Kind}")
        };
    }

    private static DrawingObject? CreateLine(ViewBase view, DrawingDebugShape shape)
    {
        var line = new Tekla.Structures.Drawing.Line(view, new Point(shape.X1, shape.Y1, 0), new Point(shape.X2, shape.Y2, 0));
        return line.Insert() ? line : null;
    }

    private static DrawingObject? CreateRectangle(ViewBase view, DrawingDebugShape shape)
    {
        var rectangle = new Rectangle(view, new Point(shape.X1, shape.Y1, 0), new Point(shape.X2, shape.Y2, 0));
        return rectangle.Insert() ? rectangle : null;
    }

    private static DrawingObject? CreatePolyline(ViewBase view, DrawingDebugShape shape)
    {
        var points = ToPointList(shape.Points);
        if (points.Count < 2)
            throw new InvalidOperationException("Polyline requires at least 2 points.");

        var polyline = new Polyline(view, points);
        return polyline.Insert() ? polyline : null;
    }

    private static DrawingObject? CreatePolygon(ViewBase view, DrawingDebugShape shape)
    {
        var points = ToPointList(shape.Points);
        if (points.Count < 3)
            throw new InvalidOperationException("Polygon requires at least 3 points.");

        var polygon = new Polygon(view, points);
        return polygon.Insert() ? polygon : null;
    }

    private static DrawingObject? CreateText(ViewBase view, DrawingDebugShape shape)
    {
        var text = new Text(view, new Point(shape.X1, shape.Y1, 0), shape.Text ?? string.Empty);
        if (!text.Insert())
            return null;

        if (Math.Abs(shape.Angle) > 0.001)
        {
            text.Attributes.Angle = shape.Angle;
            text.Modify();
        }

        return text;
    }

    private static PointList ToPointList(IEnumerable<double[]> points)
    {
        var list = new PointList();
        foreach (var point in points ?? Enumerable.Empty<double[]>())
        {
            if (point == null || point.Length < 2)
                continue;

            list.Add(new Point(point[0], point[1], point.Length >= 3 ? point[2] : 0));
        }

        return list;
    }

    private static void TagOverlayObject(DrawingObject drawingObject, string group, string kind)
    {
        drawingObject.SetUserProperty(OverlayFlagProperty, "1");
        drawingObject.SetUserProperty(OverlayGroupProperty, group ?? string.Empty);
        drawingObject.SetUserProperty(OverlayKindProperty, kind ?? string.Empty);
        drawingObject.Modify();
    }

    private static ViewBase ResolveView(Tekla.Structures.Drawing.Drawing activeDrawing, int? viewId)
    {
        if (!viewId.HasValue)
            return activeDrawing.GetSheet();

        var views = activeDrawing.GetSheet().GetAllViews();
        while (views.MoveNext())
        {
            if (views.Current is View view && view.GetIdentifier().ID == viewId.Value)
                return view;
        }

        throw new ViewNotFoundException(viewId.Value);
    }
}
