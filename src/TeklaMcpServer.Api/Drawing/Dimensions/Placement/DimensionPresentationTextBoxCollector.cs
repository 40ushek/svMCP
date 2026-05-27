using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.DrawingPresentationModel;
using Tekla.Structures.DrawingPresentationModelInterface;
using PresentationConnection = Tekla.Structures.DrawingPresentationModelInterface.Connection;
using DrawingView = Tekla.Structures.Drawing.View;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class DimensionPresentationTextBox
{
    public int SourceObjectId { get; set; }
    public string SourceObjectKind { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string? Font { get; set; }
    public double PositionX { get; set; }
    public double PositionY { get; set; }
    public double Angle { get; set; }
    public double Height { get; set; }
    public double Proportion { get; set; }
    public double ViewScale { get; set; }
    public double ViewPositionX { get; set; }
    public double ViewPositionY { get; set; }
    public double CenterX { get; set; }
    public double CenterY { get; set; }
    public double ViewHeight { get; set; }
    public double ViewWidth { get; set; }
    public double ViewWidthFromProportion { get; set; }
    public bool GlyphMeasured { get; set; }
    public List<double[]> Polygon { get; set; } = [];
}

internal static class DimensionPresentationTextBoxCollector
{
    internal static List<DimensionPresentationTextBox> Collect(
        PresentationConnection? connection,
        int sourceObjectId,
        string sourceObjectKind,
        DrawingView view)
    {
        if (connection == null)
            return [];

        try
        {
            var segment = connection.Service.GetObjectPresentation(sourceObjectId);
            if (segment?.Primitives == null)
                return [];

            var viewScale = TryGetViewScale(view);
            return EnumerateTextPrimitives(segment)
                .Select(textPrimitive => CreateTextBox(sourceObjectId, sourceObjectKind, viewScale, textPrimitive))
                .Where(static textBox => textBox.Polygon.Count >= 4)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    internal static List<DimensionPresentationTextBox> DistinctByGeometry(
        IEnumerable<DimensionPresentationTextBox> boxes)
    {
        var results = new List<DimensionPresentationTextBox>();
        var seen = new HashSet<string>(System.StringComparer.Ordinal);

        foreach (var box in boxes)
        {
            if (seen.Add(BuildKey(box)))
                results.Add(box);
        }

        return results;
    }

    private static IEnumerable<TextPrimitive> EnumerateTextPrimitives(Segment segment)
    {
        foreach (var primitive in segment.Primitives)
        {
            foreach (var textPrimitive in EnumerateTextPrimitives(primitive))
                yield return textPrimitive;
        }
    }

    private static IEnumerable<TextPrimitive> EnumerateTextPrimitives(PrimitiveBase primitive)
    {
        switch (primitive)
        {
            case TextPrimitive textPrimitive:
                yield return textPrimitive;
                yield break;
            case Segment nestedSegment:
                foreach (var nestedPrimitive in nestedSegment.Primitives)
                foreach (var nestedTextPrimitive in EnumerateTextPrimitives(nestedPrimitive))
                    yield return nestedTextPrimitive;
                yield break;
            case PrimitiveGroup group:
                foreach (var groupedPrimitive in group.Primitives)
                foreach (var groupedTextPrimitive in EnumerateTextPrimitives(groupedPrimitive))
                    yield return groupedTextPrimitive;
                yield break;
        }
    }

    private static DimensionPresentationTextBox CreateTextBox(
        int sourceObjectId,
        string sourceObjectKind,
        double viewScale,
        TextPrimitive textPrimitive)
    {
        var scale = viewScale > 1e-9 ? viewScale : 1.0;

        // TryComputeObb establishes the canonical TextPrimitive -> OBB pipeline.
        // If it fails (zero-size text), fall back to a zero-size box at the insert point.
        var hasObb = DimensionPresentationTextGeometryHelper.TryComputeObb(
            textPrimitive, scale,
            out var center, out var widthAxis, out var heightAxis,
            out var measurement);
        if (!hasObb)
            center = (textPrimitive.Position.X * scale, textPrimitive.Position.Y * scale);

        return new DimensionPresentationTextBox
        {
            SourceObjectId = sourceObjectId,
            SourceObjectKind = sourceObjectKind,
            Text = measurement.Text,
            Font = measurement.Font,
            PositionX = textPrimitive.Position.X,
            PositionY = textPrimitive.Position.Y,
            Angle = textPrimitive.Angle,
            Height = textPrimitive.Height,
            Proportion = textPrimitive.Proportion,
            CenterX = center.X,
            CenterY = center.Y,
            ViewScale = scale,
            ViewPositionX = textPrimitive.Position.X * scale,
            ViewPositionY = textPrimitive.Position.Y * scale,
            ViewHeight = measurement.Height,
            ViewWidth = measurement.Width,
            ViewWidthFromProportion = measurement.WidthFromProportion,
            GlyphMeasured = measurement.GlyphMeasured,
            Polygon = DimensionPresentationTextGeometryHelper.CreateOrientedPolygon(
                center, widthAxis, heightAxis, measurement.Width, measurement.Height)
        };
    }

    private static string BuildKey(DimensionPresentationTextBox box)
    {
        var polygonKey = string.Join(
            "|",
            box.Polygon.Select(static point => $"{point[0]:0.###},{point[1]:0.###}"));

        return $"{box.Text}::{polygonKey}";
    }

    private static double TryGetViewScale(DrawingView view)
    {
        try { return view.Attributes?.Scale > 1e-9 ? view.Attributes.Scale : 1.0; }
        catch { return 1.0; }
    }
}
