using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingPresentationModel;
using Tekla.Structures.DrawingPresentationModelInterface;
using Tekla.Structures.Geometry3d;
using PresentationConnection = Tekla.Structures.DrawingPresentationModelInterface.Connection;
using DrawingView = Tekla.Structures.Drawing.View;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class DimensionPresentationTextBox
{
    public int SourceObjectId { get; set; }
    public string SourceObjectKind { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public double PositionX { get; set; }
    public double PositionY { get; set; }
    public double Angle { get; set; }
    public double Height { get; set; }
    public double Proportion { get; set; }
    public double ViewScale { get; set; }
    public double ViewPositionX { get; set; }
    public double ViewPositionY { get; set; }
    public double ViewHeight { get; set; }
    public double ViewWidthFromProportion { get; set; }
    public List<double[]> Polygon { get; set; } = [];
}

internal static class DimensionPresentationTextBoxCollector
{
    internal static List<DimensionPresentationTextBox> Collect(
        PresentationConnection? connection,
        int sourceObjectId,
        string sourceObjectKind,
        DrawingView view,
        Text.TextAttributes textAttributes)
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
                .Select(textPrimitive => CreateTextBox(sourceObjectId, sourceObjectKind, view, textAttributes, viewScale, textPrimitive))
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
        DrawingView view,
        Text.TextAttributes textAttributes,
        double viewScale,
        TextPrimitive textPrimitive)
    {
        var scale = viewScale > 1e-9 ? viewScale : 1.0;
        var insertX = textPrimitive.Position.X * scale;
        var insertY = textPrimitive.Position.Y * scale;
        var text = textPrimitive.Text ?? string.Empty;
        var presentationHeight = textPrimitive.Height * scale;
        var presentationWidth = textPrimitive.Height * textPrimitive.Proportion * scale;
        var measuredSize = TryMeasureTextSize(view, text, textAttributes);
        var height = measuredSize?.Height ?? presentationHeight;
        var width = measuredSize?.Width ?? presentationWidth;
        var angle = textPrimitive.Angle;
        var widthAxis = (X: System.Math.Cos(angle), Y: System.Math.Sin(angle));
        var heightAxis = (X: -System.Math.Sin(angle), Y: System.Math.Cos(angle));

        return new DimensionPresentationTextBox
        {
            SourceObjectId = sourceObjectId,
            SourceObjectKind = sourceObjectKind,
            Text = text,
            PositionX = textPrimitive.Position.X,
            PositionY = textPrimitive.Position.Y,
            Angle = angle,
            Height = textPrimitive.Height,
            Proportion = textPrimitive.Proportion,
            ViewScale = scale,
            ViewPositionX = insertX,
            ViewPositionY = insertY,
            ViewHeight = height,
            ViewWidthFromProportion = presentationWidth,
            Polygon =
            [
                CreatePoint(insertX, insertY, widthAxis, heightAxis, 0.0, 0.0),
                CreatePoint(insertX, insertY, widthAxis, heightAxis, 0.0, height),
                CreatePoint(insertX, insertY, widthAxis, heightAxis, width, height),
                CreatePoint(insertX, insertY, widthAxis, heightAxis, width, 0.0)
            ]
        };
    }

    private static double[] CreatePoint(
        double insertX,
        double insertY,
        (double X, double Y) widthAxis,
        (double X, double Y) heightAxis,
        double widthOffset,
        double heightOffset)
        =>
        [
            System.Math.Round(insertX + widthAxis.X * widthOffset + heightAxis.X * heightOffset, 3),
            System.Math.Round(insertY + widthAxis.Y * widthOffset + heightAxis.Y * heightOffset, 3)
        ];

    private static string BuildKey(DimensionPresentationTextBox box)
    {
        var polygonKey = string.Join(
            "|",
            box.Polygon.Select(static point => $"{point[0]:0.###},{point[1]:0.###}"));

        return $"{box.Text}::{polygonKey}";
    }

    private static (double Width, double Height)? TryMeasureTextSize(
        DrawingView view,
        string textValue,
        Text.TextAttributes textAttributes)
    {
        if (string.IsNullOrEmpty(textValue))
            return null;

        Text? text = null;
        try
        {
            var placing = new AlongLinePlacing(new Point(0.0, 0.0, 0.0), new Point(1000.0, 0.0, 0.0));
            text = new Text(view, new Point(0.0, 0.0, 0.0), textValue, placing, textAttributes);
            if (!text.Insert())
                return null;

            var box = text.GetObjectAlignedBoundingBox();
            return (box.Width, box.Height);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (text != null)
                try { text.Delete(); } catch { }
        }
    }

    private static double TryGetViewScale(DrawingView view)
    {
        try { return view.Attributes?.Scale > 1e-9 ? view.Attributes.Scale : 1.0; }
        catch { return 1.0; }
    }
}
