using System.Collections.Generic;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.DrawingPresentationModel;
using Tekla.Structures.Geometry3d;

namespace TeklaMcpServer.Api.Drawing;

internal static class DimensionAngleDebugOverlayBuilder
{
    private const double Epsilon = 1e-9;

    internal static List<DrawingDebugShape> CreateShapes(AngleDimension dimension, Tekla.Structures.Drawing.View view)
    {
        var shapes = new List<DrawingDebugShape>();
        var viewId = view.GetIdentifier().ID;
        var origin = dimension.Origin;

        if (!TryNormalize(dimension.Point1.X - origin.X, dimension.Point1.Y - origin.Y, out var first)
            || !TryNormalize(dimension.Point2.X - origin.X, dimension.Point2.Y - origin.Y, out var second))
        {
            return shapes;
        }

        var firstLength = Length(dimension.Point1.X - origin.X, dimension.Point1.Y - origin.Y);
        var secondLength = Length(dimension.Point2.X - origin.X, dimension.Point2.Y - origin.Y);
        var rayLength = GetRayLength(firstLength, secondLength);
        AddLine(shapes, viewId, origin, first, rayLength, "Black", "SolidLine");
        AddLine(shapes, viewId, origin, second, rayLength, "Black", "SolidLine");

        var bisector = ResolveBisector(first, second);
        AddLine(shapes, viewId, origin, bisector, rayLength, "Cyan", "DashDot");

        var scale = TryGetViewScale(view);
        AddRadiusCandidate(shapes, viewId, origin, first, second, bisector, dimension.Distance, "distance", "Red");
        AddRadiusCandidate(shapes, viewId, origin, first, second, bisector, dimension.Distance * scale, "distance*scale", "Blue");
        if (scale > Epsilon)
            AddRadiusCandidate(shapes, viewId, origin, first, second, bisector, dimension.Distance / scale, "distance/scale", "Green");

        return shapes;
    }

    internal static List<DrawingDebugShape> CreatePresentationShapes(AngleDimension dimension, Tekla.Structures.Drawing.View view, Segment segment)
    {
        var shapes = new List<DrawingDebugShape>();
        // Presentation coords are in paper space. To draw in view space:
        // view_coord = paper_coord * viewScale  (since paper = view / scale, and origin cancels out)
        var scale = TryGetViewScale(view);
        var viewId = view.GetIdentifier().ID;
        foreach (var primitive in segment.Primitives)
            AddPresentationPrimitiveShapes(shapes, viewId, primitive, scale);

        return shapes;
    }

    private static void AddPresentationPrimitiveShapes(List<DrawingDebugShape> shapes, int viewId, PrimitiveBase primitive, double scale)
    {
        if (primitive is Segment segment)
        {
            foreach (var nested in segment.Primitives)
                AddPresentationPrimitiveShapes(shapes, viewId, nested, scale);
        }
        else if (primitive is PrimitiveGroup group)
        {
            foreach (var nested in group.Primitives)
                AddPresentationPrimitiveShapes(shapes, viewId, nested, scale);
        }
        else if (primitive is ArcPrimitive arc)
        {
            AddArcPrimitiveShape(shapes, viewId, arc, scale);
        }
        else if (primitive is TextPrimitive text)
        {
            var x = text.Position.X * scale;
            var y = text.Position.Y * scale;
            shapes.Add(new DrawingDebugShape { Kind = "cross", ViewId = viewId, X1 = x, Y1 = y, Size = 3.0, Color = "Magenta", LineType = "SolidLine" });
            shapes.Add(new DrawingDebugShape { Kind = "text", ViewId = viewId, X1 = x + 2.0, Y1 = y + 2.0, Text = "txt", Color = "Magenta", TextHeight = 0.8 });
        }
    }

    private static void AddArcPrimitiveShape(List<DrawingDebugShape> shapes, int viewId, ArcPrimitive arc, double scale)
    {
        var geometry = arc.GetArc();
        var radius = geometry.Circle.Radius * scale;
        if (radius <= Epsilon)
            return;

        var cx = geometry.Circle.Center.X * scale;
        var cy = geometry.Circle.Center.Y * scale;

        const int SegmentCount = 48;
        var points = new List<double[]>(SegmentCount + 1);
        var startAngle = geometry.StartAngle.Radians;
        var deltaAngle = geometry.DeltaAngle.Radians;
        for (var i = 0; i <= SegmentCount; i++)
        {
            var angle = startAngle + (deltaAngle * i / SegmentCount);
            points.Add(
            [
                System.Math.Round(cx + (System.Math.Cos(angle) * radius), 3),
                System.Math.Round(cy + (System.Math.Sin(angle) * radius), 3)
            ]);
        }

        shapes.Add(new DrawingDebugShape { Kind = "polyline", ViewId = viewId, Points = points, Color = "Orange", LineType = "SolidLine" });
        shapes.Add(new DrawingDebugShape { Kind = "cross", ViewId = viewId, X1 = cx, Y1 = cy, Size = 4.0, Color = "Orange", LineType = "SolidLine" });
    }

    private static void AddRadiusCandidate(
        List<DrawingDebugShape> shapes,
        int viewId,
        Point origin,
        (double X, double Y) first,
        (double X, double Y) second,
        (double X, double Y) bisector,
        double radius,
        string label,
        string color)
    {
        if (radius <= Epsilon)
            return;

        var arc = CreateArcPoints(origin, first, second, radius);
        if (arc.Count >= 2)
        {
            shapes.Add(new DrawingDebugShape
            {
                Kind = "polyline",
                ViewId = viewId,
                Points = arc,
                Color = color,
                LineType = "SolidLine"
            });
        }

        AddCross(shapes, viewId, origin.X + (first.X * radius), origin.Y + (first.Y * radius), color);
        AddCross(shapes, viewId, origin.X + (second.X * radius), origin.Y + (second.Y * radius), color);
        AddCross(shapes, viewId, origin.X + (bisector.X * radius), origin.Y + (bisector.Y * radius), color);

        shapes.Add(new DrawingDebugShape
        {
            Kind = "text",
            ViewId = viewId,
            X1 = origin.X + (bisector.X * radius) + (-bisector.Y * 4.0),
            Y1 = origin.Y + (bisector.Y * radius) + (bisector.X * 4.0),
            Text = label,
            Color = color,
            TextHeight = 0.8
        });
    }

    private static List<double[]> CreateArcPoints(
        Point origin,
        (double X, double Y) first,
        (double X, double Y) second,
        double radius)
    {
        var startAngle = System.Math.Atan2(first.Y, first.X);
        var sweep = System.Math.Atan2(
            (first.X * second.Y) - (first.Y * second.X),
            (first.X * second.X) + (first.Y * second.Y));
        const int SegmentCount = 32;
        var points = new List<double[]>(SegmentCount + 1);
        for (var i = 0; i <= SegmentCount; i++)
        {
            var angle = startAngle + (sweep * i / SegmentCount);
            points.Add(
            [
                System.Math.Round(origin.X + (System.Math.Cos(angle) * radius), 3),
                System.Math.Round(origin.Y + (System.Math.Sin(angle) * radius), 3)
            ]);
        }

        return points;
    }

    private static void AddLine(
        List<DrawingDebugShape> shapes,
        int viewId,
        Point origin,
        (double X, double Y) direction,
        double length,
        string color,
        string lineType)
    {
        shapes.Add(new DrawingDebugShape
        {
            Kind = "line",
            ViewId = viewId,
            X1 = origin.X,
            Y1 = origin.Y,
            X2 = origin.X + (direction.X * length),
            Y2 = origin.Y + (direction.Y * length),
            Color = color,
            LineType = lineType
        });
    }

    private static void AddCross(List<DrawingDebugShape> shapes, int viewId, double x, double y, string color)
    {
        shapes.Add(new DrawingDebugShape
        {
            Kind = "cross",
            ViewId = viewId,
            X1 = x,
            Y1 = y,
            Size = 4.0,
            Color = color,
            LineType = "SolidLine"
        });
    }

    private static (double X, double Y) ResolveBisector((double X, double Y) first, (double X, double Y) second)
    {
        if (TryNormalize(first.X + second.X, first.Y + second.Y, out var bisector))
            return bisector;

        var perpendicular = (-first.Y, first.X);
        var dot = (perpendicular.Item1 * second.X) + (perpendicular.Item2 * second.Y);
        return dot >= 0.0
            ? perpendicular
            : (-perpendicular.Item1, -perpendicular.Item2);
    }

    private static double GetRayLength(double firstLength, double secondLength)
    {
        return System.Math.Max(System.Math.Max(firstLength, secondLength), 50.0);
    }

    private static bool TryNormalize(double x, double y, out (double X, double Y) normalized)
    {
        var length = Length(x, y);
        if (length <= Epsilon)
        {
            normalized = default;
            return false;
        }

        normalized = (x / length, y / length);
        return true;
    }

    private static double TryGetViewScale(Tekla.Structures.Drawing.View view)
    {
        try
        {
            return view.Attributes?.Scale > Epsilon ? view.Attributes.Scale : 1.0;
        }
        catch
        {
            return 1.0;
        }
    }

    private static double Length(double x, double y) => System.Math.Sqrt((x * x) + (y * y));
}
