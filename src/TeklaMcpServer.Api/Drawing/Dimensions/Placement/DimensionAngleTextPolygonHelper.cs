using System.Collections.Generic;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.DrawingPresentationModel;
using DrawingView = Tekla.Structures.Drawing.View;
using PresentationConnection = Tekla.Structures.DrawingPresentationModelInterface.Connection;

namespace TeklaMcpServer.Api.Drawing;

internal static class DimensionAngleTextPolygonHelper
{
    private const double Epsilon = 1e-9;

    // AngleDimension has no child text objects; text OBB is reconstructed from
    // presentation primitives (primary) with analytical fallback.
    internal static List<double[]>? TryCreateTextPolygon(
        AngleDimension dimension,
        DrawingView view,
        PresentationConnection? presentationConnection = null,
        List<string>? diagnostics = null)
    {
        if (dimension == null || view == null)
            return null;

        var scale = TryGetViewScale(view);

        var textValue = TryFormatAngleText(dimension);
        if (string.IsNullOrWhiteSpace(textValue))
            return null;

        // Primary: use presentation TextPrimitive for center, orientation, and size.
        if (TryGetTextPlacementFromPresentation(
                dimension, scale, textValue,
                presentationConnection,
                out var center, out var widthAxis, out var heightAxis,
                out var measurement))
        {
            diagnostics?.Add(
                $"angleTextMeasurement dimension={dimension.GetIdentifier().ID}, text=\"{measurement.Text}\", font=\"{measurement.Font}\", glyphMeasured={measurement.GlyphMeasured}, width={measurement.Width:0.###}, height={measurement.Height:0.###}, widthFromProportion={measurement.WidthFromProportion:0.###}");
            return CreateOrientedPolygon(center, widthAxis, heightAxis, measurement.Width, measurement.Height);
        }

        // Fallback: analytical placement along bisector.
        var fallbackMeasurement = TryCreateFallbackTextMeasurement(dimension, scale, textValue);
        if (fallbackMeasurement != null
            && TryCreateAnalyticalAxes(dimension, scale, fallbackMeasurement.Height,
                out center, out widthAxis, out heightAxis))
        {
            return CreateOrientedPolygon(center, widthAxis, heightAxis, fallbackMeasurement.Width, fallbackMeasurement.Height);
        }

        return null;
    }

    private static bool TryGetTextPlacementFromPresentation(
        AngleDimension dimension,
        double scale,
        string expectedText,
        PresentationConnection? presentationConnection,
        out (double X, double Y) center,
        out (double X, double Y) widthAxis,
        out (double X, double Y) heightAxis,
        out DimensionPresentationTextMeasurement measurement)
    {
        center = default;
        widthAxis = default;
        heightAxis = default;
        measurement = new DimensionPresentationTextMeasurement();

        if (presentationConnection == null)
            return false;

        try
        {
            var segment = presentationConnection.Service.GetObjectPresentation(dimension.GetIdentifier().ID);
            if (segment?.Primitives == null)
                return false;

            var textPrim = FindAngleTextPrimitive(segment.Primitives, expectedText);
            if (textPrim == null)
                return false;

            measurement = DimensionPresentationTextMeasureHelper.Measure(textPrim, scale, expectedText);

            // Presentation coords are paper space (view / scale). Multiply by scale → view coords.
            var insertX = textPrim.Position.X * scale;
            var insertY = textPrim.Position.Y * scale;

            // Position is left baseline corner. Center = insert + width/2 along angle + height/2 perpendicular upward.
            var angle = textPrim.Angle;
            var cos = System.Math.Cos(angle);
            var sin = System.Math.Sin(angle);
            center = (
                insertX + cos * (measurement.Width / 2.0) - sin * (measurement.Height / 2.0),
                insertY + sin * (measurement.Width / 2.0) + cos * (measurement.Height / 2.0));

            widthAxis = (cos, sin);
            heightAxis = (-sin, cos);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // Prefer the exact angle value, then any degree text to avoid picking up leader labels.
    private static TextPrimitive? FindAngleTextPrimitive(
        IList<PrimitiveBase> primitives,
        string expectedText,
        int depth = 0)
    {
        if (depth > 4) return null;
        TextPrimitive? degreeFallback = null;
        TextPrimitive? fallback = null;

        foreach (var prim in primitives)
        {
            if (prim is TextPrimitive text)
            {
                if (IsExpectedAngleText(text.Text, expectedText))
                    return text;
                if (ContainsDegreeText(text.Text))
                    degreeFallback ??= text;
                fallback ??= text;
            }

            IList<PrimitiveBase>? children = null;
            if (prim is PrimitiveGroup g) children = g.Primitives;
            else if (prim is Segment s) children = s.Primitives;

            if (children != null)
            {
                var found = FindAngleTextPrimitive(children, expectedText, depth + 1);
                if (IsExpectedAngleText(found?.Text, expectedText))
                    return found;
                if (ContainsDegreeText(found?.Text))
                    degreeFallback ??= found;
                fallback ??= found;
            }
        }

        return degreeFallback ?? fallback;
    }

    private static bool IsExpectedAngleText(string? candidate, string expectedText)
    {
        var normalizedCandidate = NormalizeAngleText(candidate);
        var normalizedExpected = NormalizeAngleText(expectedText);
        if (normalizedCandidate.Length == 0 || normalizedExpected.Length == 0)
            return false;

        return normalizedCandidate == normalizedExpected
            || normalizedCandidate.Contains(normalizedExpected)
            || normalizedExpected.Contains(normalizedCandidate);
    }

    private static bool ContainsDegreeText(string? value) =>
        value != null && value.Contains("°");

    private static string NormalizeAngleText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value!
            .Replace(" ", string.Empty)
            .Replace("\t", string.Empty)
            .Replace(",", ".")
            .Trim();
    }

    private static bool TryCreateAnalyticalAxes(
        AngleDimension dimension,
        double scale,
        double textHeight,
        out (double X, double Y) center,
        out (double X, double Y) widthAxis,
        out (double X, double Y) heightAxis)
    {
        center = default;
        widthAxis = default;
        heightAxis = default;

        var origin = dimension.Origin;
        if (!TryNormalize(dimension.Point1.X - origin.X, dimension.Point1.Y - origin.Y, out var first)
            || !TryNormalize(dimension.Point2.X - origin.X, dimension.Point2.Y - origin.Y, out var second))
            return false;

        if (!TryNormalize(first.X + second.X, first.Y + second.Y, out var bisector))
        {
            var perp = (-first.Y, first.X);
            bisector = Dot(perp, second) >= 0.0 ? perp : (-perp.Item1, -perp.Item2);
        }

        var arcRadius = dimension.Distance * scale;
        if (arcRadius <= Epsilon)
            return false;

        var centerDistance = arcRadius + (System.Math.Max(0.0, textHeight) / 2.0);
        center = (origin.X + bisector.X * centerDistance, origin.Y + bisector.Y * centerDistance);

        var tangent = (-bisector.Y, bisector.X);
        if (!TryNormalize(tangent.Item1, tangent.Item2, out widthAxis))
            return false;

        heightAxis = bisector;
        return true;
    }

    private static string TryFormatAngleText(AngleDimension dimension)
    {
        try
        {
            return dimension.GetAngle().ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "°";
        }
        catch
        {
            return string.Empty;
        }
    }

    private static DimensionPresentationTextMeasurement? TryCreateFallbackTextMeasurement(
        AngleDimension dimension,
        double scale,
        string textValue)
    {
        try
        {
            var attributes = dimension.Attributes;
            var height = TryReadTextHeight(attributes.Text) * (scale > Epsilon ? scale : 1.0);
            if (height <= Epsilon)
                return null;

            var font = TryReadFontName(attributes.Text.Font);
            var glyphMeasured = DrawingTextMeasurementHelper.TryMeasureText(
                textValue,
                font,
                height,
                out var width);

            if (!glyphMeasured)
                return null;

            return new DimensionPresentationTextMeasurement
            {
                Text = textValue,
                Font = font,
                Height = height,
                Width = width,
                WidthFromProportion = 0.0,
                GlyphMeasured = true
            };
        }
        catch
        {
            return null;
        }
    }

    private static double TryReadTextHeight(object textAttributes)
    {
        var property = textAttributes.GetType().GetProperty("Height");
        var value = property?.GetValue(textAttributes);
        try { return value == null ? 0.0 : System.Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture); }
        catch { return 0.0; }
    }

    private static string? TryReadFontName(object? fontAttributes)
    {
        if (fontAttributes == null)
            return null;

        foreach (var propertyName in new[] { "Name", "FontName", "Typeface" })
        {
            var property = fontAttributes.GetType().GetProperty(propertyName);
            var value = property?.GetValue(fontAttributes)?.ToString();
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return fontAttributes.ToString();
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

    private static bool TryNormalize(double x, double y, out (double X, double Y) normalized)
    {
        var len = System.Math.Sqrt(x * x + y * y);
        if (len <= Epsilon) { normalized = default; return false; }
        normalized = (x / len, y / len);
        return true;
    }

    private static double TryGetViewScale(DrawingView view)
    {
        try { return view.Attributes?.Scale > Epsilon ? view.Attributes.Scale : 1.0; }
        catch { return 1.0; }
    }

    private static double Dot((double X, double Y) a, (double X, double Y) b) =>
        a.X * b.X + a.Y * b.Y;
}
