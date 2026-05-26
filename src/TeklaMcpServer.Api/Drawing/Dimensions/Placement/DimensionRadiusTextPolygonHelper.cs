using System.Collections.Generic;
using Tekla.Structures.Drawing;
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
        int dimensionId,
        PresentationConnection? presentationConnection = null)
    {
        if (dimension == null || view == null)
            return null;

        var scale = TryGetViewScale(view);

        if (presentationConnection == null)
            return null;

        try
        {
            var segment = presentationConnection.Service.GetObjectPresentation(dimensionId);
            if (segment?.Primitives == null)
                return null;

            var textPrim = FindRadiusTextPrimitive(segment.Primitives);
            if (textPrim == null)
                return null;

            if (!DimensionPresentationTextGeometryHelper.TryComputeObb(
                    textPrim, scale,
                    out var center, out var widthAxis, out var heightAxis,
                    out var measurement))
                return null;

            return DimensionPresentationTextGeometryHelper.CreateOrientedPolygon(
                center, widthAxis, heightAxis, measurement.Width, measurement.Height);
        }
        catch
        {
            return null;
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
        value != null && value.IndexOf('R') >= 0 && ContainsDigit(value);

    private static bool ContainsDigit(string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        foreach (var ch in value!) if (char.IsDigit(ch)) return true;
        return false;
    }

    private static double TryGetViewScale(DrawingView view)
    {
        try { return view.Attributes?.Scale > Epsilon ? view.Attributes.Scale : 1.0; }
        catch { return 1.0; }
    }
}
