using System.Collections.Generic;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.DrawingPresentationModel;
using DrawingView = Tekla.Structures.Drawing.View;
using PresentationConnection = Tekla.Structures.DrawingPresentationModelInterface.Connection;

namespace TeklaMcpServer.Api.Drawing;

internal static class DimensionRadiusTextPolygonHelper
{
    private const double Epsilon = 1e-9;

    // RadiusDimension has no child text objects; text OBB is reconstructed from
    // presentation primitives (primary) with analytical fallback.
    internal static List<double[]>? TryCreateTextPolygon(
        RadiusDimension dimension,
        DrawingView view,
        PresentationConnection? presentationConnection = null)
    {
        if (dimension == null || view == null)
            return null;

        var scale = TryGetViewScale(view);

        // Primary: use presentation TextPrimitive for position, orientation, and size.
        if (TryGetTextPlacementFromPresentation(
                dimension, scale,
                presentationConnection,
                out var center, out var widthAxis, out var heightAxis,
                out var presWidth, out var presHeight))
        {
            if (presWidth > Epsilon && presHeight > Epsilon)
                return CreateOrientedPolygon(center, widthAxis, heightAxis, presWidth, presHeight);
        }

        return null;
    }

    private static bool TryGetTextPlacementFromPresentation(
        RadiusDimension dimension,
        double scale,
        PresentationConnection? presentationConnection,
        out (double X, double Y) center,
        out (double X, double Y) widthAxis,
        out (double X, double Y) heightAxis,
        out double presWidth,
        out double presHeight)
    {
        center = default;
        widthAxis = (1, 0);
        heightAxis = (0, 1);
        presWidth = 0;
        presHeight = 0;

        if (presentationConnection == null)
            return false;

        try
        {
            var segment = presentationConnection.Service.GetObjectPresentation(dimension.GetIdentifier().ID);
            if (segment?.Primitives == null)
                return false;

            var textPrim = FindFirstTextPrimitive(segment.Primitives);
            if (textPrim == null)
                return false;

            var insertX = textPrim.Position.X * scale;
            var insertY = textPrim.Position.Y * scale;

            presHeight = textPrim.Height * scale;
            var proportionWidth = textPrim.Height * textPrim.Proportion * scale;
            var glyphMeasured = DrawingTextMeasurementHelper.TryMeasureText(
                textPrim.Text, textPrim.Font, presHeight, out var glyphWidth, out _);
            presWidth = glyphMeasured ? glyphWidth : proportionWidth;

            var angle = textPrim.Angle;
            var cos = System.Math.Cos(angle);
            var sin = System.Math.Sin(angle);

            // Insert point is bottom-left corner; center = insert + half-width along angle + half-height perpendicular.
            center = (
                insertX + cos * (presWidth / 2.0) - sin * (presHeight / 2.0),
                insertY + sin * (presWidth / 2.0) + cos * (presHeight / 2.0));

            widthAxis = (cos, sin);
            heightAxis = (-sin, cos);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static TextPrimitive? FindFirstTextPrimitive(IList<PrimitiveBase> primitives, int depth = 0)
    {
        if (depth > 4) return null;
        foreach (var prim in primitives)
        {
            if (prim is TextPrimitive text)
                return text;

            IList<PrimitiveBase>? children = null;
            if (prim is PrimitiveGroup g) children = g.Primitives;
            else if (prim is Segment s) children = s.Primitives;

            if (children != null)
            {
                var found = FindFirstTextPrimitive(children, depth + 1);
                if (found != null)
                    return found;
            }
        }
        return null;
    }

    private static List<double[]> CreateOrientedPolygon(
        (double X, double Y) center,
        (double X, double Y) widthAxis,
        (double X, double Y) heightAxis,
        double width,
        double height)
    {
        var halfWidth = width / 2.0;
        var halfHeight = height / 2.0;
        return
        [
            CreatePoint(center, widthAxis, heightAxis, -halfWidth, -halfHeight),
            CreatePoint(center, widthAxis, heightAxis, -halfWidth, halfHeight),
            CreatePoint(center, widthAxis, heightAxis, halfWidth, halfHeight),
            CreatePoint(center, widthAxis, heightAxis, halfWidth, -halfHeight)
        ];
    }

    private static double[] CreatePoint(
        (double X, double Y) center,
        (double X, double Y) widthAxis,
        (double X, double Y) heightAxis,
        double widthOffset,
        double heightOffset)
        =>
        [
            System.Math.Round(center.X + widthAxis.X * widthOffset + heightAxis.X * heightOffset, 3),
            System.Math.Round(center.Y + widthAxis.Y * widthOffset + heightAxis.Y * heightOffset, 3)
        ];

    private static double TryGetViewScale(DrawingView view)
    {
        try { return view.Attributes?.Scale > Epsilon ? view.Attributes.Scale : 1.0; }
        catch { return 1.0; }
    }
}
