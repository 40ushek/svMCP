using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

internal static class DimensionTextPolygonPlacementHelper
{
    internal static List<double[]>? CreateFallbackPolygon(
        DrawingLineInfo dimensionLine,
        double widthAlongLine,
        double heightPerpendicularToLine,
        double viewScale,
        DimensionTextPlacementContext placementContext,
        (double X, double Y)? upDirection)
    {
        var polygon = CreateDimStyleTextPolygon(dimensionLine, widthAlongLine, heightPerpendicularToLine, viewScale);
        if (polygon == null || polygon.Count == 0)
            return polygon;

        return ApplyTextPlacement(polygon, placementContext, upDirection);
    }

    internal static List<double[]>? CreateDimStyleTextPolygon(
        DrawingLineInfo dimensionLine,
        double widthAlongLine,
        double heightPerpendicularToLine,
        double viewScale)
    {
        var polygon = CreateOrientedTextPolygon(dimensionLine, widthAlongLine, heightPerpendicularToLine);
        if (polygon == null || polygon.Count == 0)
            return polygon;

        if (viewScale <= 1e-6)
            return polygon;

        if (!DimensionPlacementHeuristics.TryGetDimStyleLineVector(dimensionLine, out var lineVector))
            return polygon;

        var alongLineOffset = DimensionPlacementHeuristics.GetDimStyleAlongLineOffset(viewScale);
        return OffsetPolygon(polygon, lineVector.X * alongLineOffset, lineVector.Y * alongLineOffset);
    }

    internal static List<double[]>? ApplyTextPlacement(
        List<double[]>? polygon,
        DimensionTextPlacementContext placementContext,
        (double X, double Y)? upDirection)
    {
        if (polygon == null || polygon.Count == 0)
            return polygon;

        var placedPolygon = polygon;
        if (string.Equals(placementContext.TextPlacing, "AboveDimensionLine", System.StringComparison.OrdinalIgnoreCase)
            && upDirection.HasValue)
        {
            var height = GetPolygonSpanAlongDirection(placedPolygon, upDirection.Value.X, upDirection.Value.Y);
            if (height > 1e-6)
            {
                var offset = DimensionPlacementHeuristics.GetAboveLineTextOffset(height, placementContext.SideSign);
                placedPolygon = OffsetPolygon(
                    placedPolygon,
                    upDirection.Value.X * offset,
                    upDirection.Value.Y * offset);
            }
        }

        return placedPolygon;
    }

    private static List<double[]>? CreateOrientedTextPolygon(
        DrawingLineInfo dimensionLine,
        double widthAlongLine,
        double heightPerpendicularToLine)
    {
        if (widthAlongLine <= 1e-6 || heightPerpendicularToLine <= 1e-6)
            return null;

        if (!TeklaDrawingDimensionsApi.TryNormalizeDirection(
                dimensionLine.EndX - dimensionLine.StartX,
                dimensionLine.EndY - dimensionLine.StartY,
                out var axis))
        {
            return null;
        }

        var centerX = (dimensionLine.StartX + dimensionLine.EndX) / 2.0;
        var centerY = (dimensionLine.StartY + dimensionLine.EndY) / 2.0;
        var normalX = -axis.Y;
        var normalY = axis.X;
        var halfWidth = widthAlongLine / 2.0;
        var halfHeight = heightPerpendicularToLine / 2.0;

        var corners = new[]
        {
            (X: centerX - (axis.X * halfWidth) - (normalX * halfHeight), Y: centerY - (axis.Y * halfWidth) - (normalY * halfHeight)),
            (X: centerX + (axis.X * halfWidth) - (normalX * halfHeight), Y: centerY + (axis.Y * halfWidth) - (normalY * halfHeight)),
            (X: centerX + (axis.X * halfWidth) + (normalX * halfHeight), Y: centerY + (axis.Y * halfWidth) + (normalY * halfHeight)),
            (X: centerX - (axis.X * halfWidth) + (normalX * halfHeight), Y: centerY - (axis.Y * halfWidth) + (normalY * halfHeight))
        };

        return corners
            .Select(static corner => new[] { System.Math.Round(corner.X, 3), System.Math.Round(corner.Y, 3) })
            .ToList();
    }

    private static double GetPolygonSpanAlongDirection(
        IReadOnlyList<double[]> polygon,
        double axisX,
        double axisY)
    {
        if (!TeklaDrawingDimensionsApi.TryNormalizeDirection(axisX, axisY, out var normalizedAxis))
            return 0.0;

        var min = double.MaxValue;
        var max = double.MinValue;
        foreach (var point in polygon)
        {
            var projection = (point[0] * normalizedAxis.X) + (point[1] * normalizedAxis.Y);
            min = System.Math.Min(min, projection);
            max = System.Math.Max(max, projection);
        }

        return max > min ? max - min : 0.0;
    }

    private static List<double[]> OffsetPolygon(
        IReadOnlyList<double[]> polygon,
        double offsetX,
        double offsetY)
    {
        return polygon
            .Select(point => new[]
            {
                System.Math.Round(point[0] + offsetX, 3),
                System.Math.Round(point[1] + offsetY, 3)
            })
            .ToList();
    }
}
