using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace TeklaMcpServer.Api.Drawing;

internal static class DrawingTextMeasurementHelper
{
    private const double Epsilon = 1e-9;
    private const float MeasurementEmSize = 100.0f;

    internal static bool TryMeasureText(
        string? text,
        string? fontName,
        double height,
        out double width,
        out double measuredHeight)
    {
        width = 0.0;
        measuredHeight = 0.0;

        if (string.IsNullOrEmpty(text) || height <= Epsilon)
            return false;

        using var path = new GraphicsPath();
        using var format = (StringFormat)StringFormat.GenericTypographic.Clone();
        var fontFamily = TryCreateFontFamily(fontName, out var disposeFontFamily);

        try
        {
            path.AddString(
                text,
                fontFamily,
                (int)FontStyle.Regular,
                emSize: MeasurementEmSize,
                origin: PointF.Empty,
                format);

            var bounds = path.GetBounds();
            if (bounds.Width <= 0.0f || bounds.Height <= 0.0f)
                return false;

            var heightScale = height / bounds.Height;
            width = bounds.Width * heightScale;
            measuredHeight = bounds.Height * (height / MeasurementEmSize);
            return width > Epsilon;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (disposeFontFamily)
                fontFamily.Dispose();
        }
    }

    private static FontFamily TryCreateFontFamily(string? fontName, out bool dispose)
    {
        if (!string.IsNullOrWhiteSpace(fontName))
        {
            try
            {
                dispose = true;
                return new FontFamily(fontName);
            }
            catch { }
        }

        try
        {
            dispose = true;
            return new FontFamily("Arial");
        }
        catch { }

        dispose = false;
        return FontFamily.GenericSansSerif;
    }
}
