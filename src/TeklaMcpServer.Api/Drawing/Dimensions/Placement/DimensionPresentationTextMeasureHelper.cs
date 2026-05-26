using Tekla.Structures.DrawingPresentationModel;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class DimensionPresentationTextMeasurement
{
    public string Text { get; set; } = string.Empty;
    public string? Font { get; set; }
    public double Height { get; set; }
    public double Width { get; set; }
    public double WidthFromProportion { get; set; }
    public bool GlyphMeasured { get; set; }
}

internal static class DimensionPresentationTextMeasureHelper
{
    private const double Epsilon = 1e-9;

    internal static DimensionPresentationTextMeasurement Measure(
        TextPrimitive textPrimitive,
        double scale,
        string? fallbackText = null)
    {
        var safeScale = scale > Epsilon ? scale : 1.0;
        var text = textPrimitive.Text ?? fallbackText ?? string.Empty;
        var height = textPrimitive.Height * safeScale;
        var widthFromProportion = textPrimitive.Height * textPrimitive.Proportion * safeScale;
        var glyphMeasured = DrawingTextMeasurementHelper.TryMeasureText(
            text,
            textPrimitive.Font,
            height,
            out var glyphWidth);

        return new DimensionPresentationTextMeasurement
        {
            Text = text,
            Font = textPrimitive.Font,
            Height = height,
            Width = glyphMeasured ? glyphWidth : widthFromProportion,
            WidthFromProportion = widthFromProportion,
            GlyphMeasured = glyphMeasured
        };
    }
}
