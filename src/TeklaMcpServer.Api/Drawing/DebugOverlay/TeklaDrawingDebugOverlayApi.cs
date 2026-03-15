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
            foreach (var created in CreateShapes(view, shape))
            {
                TagOverlayObject(created, request.Group, shape.Kind);
                result.CreatedIds.Add(created.GetIdentifier().ID);
            }
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

    private static IReadOnlyList<Tekla.Structures.Drawing.DrawingObject> CreateShapes(ViewBase view, DrawingDebugShape shape)
    {
        var kind = (shape.Kind ?? string.Empty).Trim().ToLowerInvariant();
        return kind switch
        {
            "line" => CreateSingleShape(CreateLine(view, shape)),
            "rectangle" => CreateSingleShape(CreateRectangle(view, shape)),
            "polyline" => CreateSingleShape(CreatePolyline(view, shape)),
            "polygon" => CreateSingleShape(CreatePolygon(view, shape)),
            "text" => CreateSingleShape(CreateText(view, shape)),
            "cross" => CreateCross(view, shape),
            _ => throw new InvalidOperationException($"Unsupported debug overlay shape kind: {shape.Kind}")
        };
    }

    private static Tekla.Structures.Drawing.DrawingObject? CreateLine(ViewBase view, DrawingDebugShape shape)
    {
        var line = new Tekla.Structures.Drawing.Line(view, new Point(shape.X1, shape.Y1, 0), new Point(shape.X2, shape.Y2, 0));
        ApplyGraphicAttributes(line.Attributes, shape);
        return line.Insert() ? line : null;
    }

    private static Tekla.Structures.Drawing.DrawingObject? CreateRectangle(ViewBase view, DrawingDebugShape shape)
    {
        var rectangle = new Rectangle(view, new Point(shape.X1, shape.Y1, 0), new Point(shape.X2, shape.Y2, 0));
        ApplyGraphicAttributes(rectangle.Attributes, shape);
        return rectangle.Insert() ? rectangle : null;
    }

    private static Tekla.Structures.Drawing.DrawingObject? CreatePolyline(ViewBase view, DrawingDebugShape shape)
    {
        var points = ToPointList(shape.Points);
        if (points.Count < 2)
            throw new InvalidOperationException("Polyline requires at least 2 points.");

        var polyline = new Polyline(view, points);
        ApplyGraphicAttributes(polyline.Attributes, shape);
        return polyline.Insert() ? polyline : null;
    }

    private static Tekla.Structures.Drawing.DrawingObject? CreatePolygon(ViewBase view, DrawingDebugShape shape)
    {
        var points = ToPointList(shape.Points);
        if (points.Count < 3)
            throw new InvalidOperationException("Polygon requires at least 3 points.");

        var polygon = new Polygon(view, points);
        ApplyGraphicAttributes(polygon.Attributes, shape);
        return polygon.Insert() ? polygon : null;
    }

    private static Tekla.Structures.Drawing.DrawingObject? CreateText(ViewBase view, DrawingDebugShape shape)
    {
        var text = new Text(view, new Point(shape.X1, shape.Y1, 0), shape.Text ?? string.Empty);
        ApplyTextAttributes(text.Attributes, shape);
        if (!text.Insert())
            return null;

        return text;
    }

    private static IReadOnlyList<Tekla.Structures.Drawing.DrawingObject> CreateCross(ViewBase view, DrawingDebugShape shape)
    {
        var size = shape.Size > 0 ? shape.Size : 4.0;
        var half = size / 2.0;
        var horizontal = CreateLine(view, new DrawingDebugShape
        {
            Kind = "line",
            X1 = shape.X1 - half,
            Y1 = shape.Y1,
            X2 = shape.X1 + half,
            Y2 = shape.Y1,
            Color = shape.Color,
            LineType = shape.LineType
        });
        var vertical = CreateLine(view, new DrawingDebugShape
        {
            Kind = "line",
            X1 = shape.X1,
            Y1 = shape.Y1 - half,
            X2 = shape.X1,
            Y2 = shape.Y1 + half,
            Color = shape.Color,
            LineType = shape.LineType
        });

        return CreateShapeList(horizontal, vertical);
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

    private static void TagOverlayObject(Tekla.Structures.Drawing.DrawingObject drawingObject, string group, string kind)
    {
        drawingObject.SetUserProperty(OverlayFlagProperty, "1");
        drawingObject.SetUserProperty(OverlayGroupProperty, group ?? string.Empty);
        drawingObject.SetUserProperty(OverlayKindProperty, kind ?? string.Empty);
        drawingObject.Modify();
    }

    private static IReadOnlyList<Tekla.Structures.Drawing.DrawingObject> CreateSingleShape(Tekla.Structures.Drawing.DrawingObject? drawingObject)
    {
        return drawingObject == null ? Array.Empty<Tekla.Structures.Drawing.DrawingObject>() : new[] { drawingObject };
    }

    private static IReadOnlyList<Tekla.Structures.Drawing.DrawingObject> CreateShapeList(params Tekla.Structures.Drawing.DrawingObject?[] drawingObjects)
    {
        return drawingObjects.Where(static o => o != null).Cast<Tekla.Structures.Drawing.DrawingObject>().ToArray();
    }

    private static void ApplyGraphicAttributes(GraphicObject.GraphicObjectAttributes attributes, DrawingDebugShape shape)
    {
        if (TryParseColor(shape.Color, out var color))
            attributes.Line.Color = color;

        if (TryParseLineType(shape.LineType, out var lineType))
            attributes.Line.Type = lineType;
    }

    private static void ApplyTextAttributes(Text.TextAttributes attributes, DrawingDebugShape shape)
    {
        if (Math.Abs(shape.Angle) > 0.001)
            attributes.Angle = shape.Angle;

        if (shape.TextHeight.HasValue && shape.TextHeight.Value > 0)
            attributes.Font.Height = shape.TextHeight.Value;

        if (TryParseColor(shape.Color, out var color))
            attributes.Font.Color = color;
    }

    private static bool TryParseColor(string? value, out DrawingColors color)
    {
        color = default;
        return !string.IsNullOrWhiteSpace(value) && Enum.TryParse(value, true, out color);
    }

    private static bool TryParseLineType(string? value, out LineTypes lineType)
    {
        lineType = LineTypes.SolidLine;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalizedValue = value!.Trim().ToLowerInvariant();
        switch (normalizedValue)
        {
            case "solid":
            case "solidline":
                lineType = LineTypes.SolidLine;
                return true;
            case "dashed":
            case "dashedline":
                lineType = LineTypes.DashedLine;
                return true;
            case "slashed":
            case "slashedline":
                lineType = LineTypes.SlashedLine;
                return true;
            case "dashdot":
                lineType = LineTypes.DashDot;
                return true;
            case "dotted":
            case "dottedline":
                lineType = LineTypes.DottedLine;
                return true;
            case "dashdoubledot":
                lineType = LineTypes.DashDoubleDot;
                return true;
            case "slashdash":
                lineType = LineTypes.SlashDash;
                return true;
            default:
                return false;
        }
    }

    private static ViewBase ResolveView(Tekla.Structures.Drawing.Drawing activeDrawing, int? viewId)
    {
        if (!viewId.HasValue)
            return activeDrawing.GetSheet();

        var views = activeDrawing.GetSheet().GetAllViews();
        while (views.MoveNext())
        {
            if (views.Current is Tekla.Structures.Drawing.View view && view.GetIdentifier().ID == viewId.Value)
                return view;
        }

        throw new ViewNotFoundException(viewId.Value);
    }
}
