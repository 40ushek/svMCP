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

    // RadiusDimension text OBB is reconstructed from presentation primitives.
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
        out double width,
        out double height)
    {
        center = default;
        widthAxis = (1, 0);
        heightAxis = (0, 1);
        width = 0;
        height = 0;

        if (presentationConnection == null)
            return false;

        try
        {
            var segment = presentationConnection.Service.GetObjectPresentation(dimension.GetIdentifier().ID);
            if (segment?.Primitives == null)
                return false;

            var textPrim = FindRadiusTextPrimitive(segment.Primitives);
            if (textPrim == null)
                return false;

            var insertX = textPrim.Position.X * scale;
            var insertY = textPrim.Position.Y * scale;
            var measurement = DimensionPresentationTextMeasureHelper.Measure(textPrim, scale);
            width = measurement.Width;
            height = measurement.Height;

            var angle = textPrim.Angle;
            var cos = System.Math.Cos(angle);
            var sin = System.Math.Sin(angle);

            // Insert point is bottom-left corner; center = insert + half-width along angle + half-height perpendicular.
            center = (
                insertX + cos * (width / 2.0) - sin * (height / 2.0),
                insertY + sin * (width / 2.0) + cos * (height / 2.0));

            widthAxis = (cos, sin);
            heightAxis = (-sin, cos);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static TextPrimitive? FindRadiusTextPrimitive(IList<PrimitiveBase> primitives, int depth = 0)
    {
        if (depth > 4) return null;
        TextPrimitive? numericFallback = null;
        TextPrimitive? fallback = null;

        foreach (var prim in primitives)
        {
            if (prim is TextPrimitive text)
            {
                if (IsRadiusText(text.Text))
                    return text;

                if (ContainsDigit(text.Text))
                    numericFallback ??= text;

                fallback ??= text;
            }

            IList<PrimitiveBase>? children = null;
            if (prim is PrimitiveGroup g) children = g.Primitives;
            else if (prim is Segment s) children = s.Primitives;

            if (children != null)
            {
                var found = FindRadiusTextPrimitive(children, depth + 1);
                if (IsRadiusText(found?.Text))
                    return found;

                if (ContainsDigit(found?.Text))
                    numericFallback ??= found;

                fallback ??= found;
            }
        }

        return numericFallback ?? fallback;
    }

    private static bool IsRadiusText(string? value) =>
        value != null
        && value.IndexOf('R') >= 0
        && ContainsDigit(value);

    private static bool ContainsDigit(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        foreach (var ch in value!)
        {
            if (char.IsDigit(ch))
                return true;
        }

        return false;
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
