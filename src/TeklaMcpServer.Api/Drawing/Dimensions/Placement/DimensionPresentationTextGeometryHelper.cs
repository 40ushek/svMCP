using System.Collections.Generic;
using Tekla.Structures.DrawingPresentationModel;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// Converts a presentation TextPrimitive into a view-space oriented bounding box.
///
/// Contract: TextPrimitive.Position is the bottom-left (baseline) insert point of the text
/// in paper-space coordinates. To get view-space coordinates multiply by viewScale.
/// The OBB center is computed as: insert + halfWidth * widthAxis + halfHeight * heightAxis,
/// where widthAxis = (cos(angle), sin(angle)) and heightAxis = (-sin(angle), cos(angle)).
/// Position -> bottom-left insert; center -> insert + halfWidth along widthAxis + halfHeight along heightAxis.
/// </summary>
internal static class DimensionPresentationTextGeometryHelper
{
    private const double Epsilon = 1e-9;

    /// <summary>
    /// Computes the view-space OBB center and axes from a TextPrimitive.
    /// Returns false if measurement yields zero size.
    /// </summary>
    internal static bool TryComputeObb(
        TextPrimitive textPrimitive,
        double viewScale,
        out (double X, double Y) center,
        out (double X, double Y) widthAxis,
        out (double X, double Y) heightAxis,
        out DimensionPresentationTextMeasurement measurement,
        string? fallbackText = null)
    {
        center = default;
        widthAxis = (1, 0);
        heightAxis = (0, 1);
        measurement = DimensionPresentationTextMeasureHelper.Measure(textPrimitive, viewScale, fallbackText);

        if (measurement.Width <= Epsilon || measurement.Height <= Epsilon)
            return false;

        // Position is paper-space bottom-left corner -> convert to view space.
        var insertX = textPrimitive.Position.X * viewScale;
        var insertY = textPrimitive.Position.Y * viewScale;

        var angle = textPrimitive.Angle;
        var cos = System.Math.Cos(angle);
        var sin = System.Math.Sin(angle);

        widthAxis = (cos, sin);
        heightAxis = (-sin, cos);

        // Center = bottom-left + halfWidth along text direction + halfHeight perpendicular.
        center = (
            insertX + cos * (measurement.Width / 2.0) - sin * (measurement.Height / 2.0),
            insertY + sin * (measurement.Width / 2.0) + cos * (measurement.Height / 2.0));

        return true;
    }

    internal static List<double[]> CreateOrientedPolygon(
        (double X, double Y) center,
        (double X, double Y) widthAxis,
        (double X, double Y) heightAxis,
        double width,
        double height)
    {
        var hw = width / 2.0;
        var hh = height / 2.0;
        return
        [
            CreatePoint(center, widthAxis, heightAxis, -hw, -hh),
            CreatePoint(center, widthAxis, heightAxis, -hw,  hh),
            CreatePoint(center, widthAxis, heightAxis,  hw,  hh),
            CreatePoint(center, widthAxis, heightAxis,  hw, -hh)
        ];
    }

    private static double[] CreatePoint(
        (double X, double Y) center,
        (double X, double Y) widthAxis,
        (double X, double Y) heightAxis,
        double dw,
        double dh)
        =>
        [
            System.Math.Round(center.X + widthAxis.X * dw + heightAxis.X * dh, 3),
            System.Math.Round(center.Y + widthAxis.Y * dw + heightAxis.Y * dh, 3)
        ];
}
