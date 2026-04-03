using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.Geometry3d;

namespace TeklaMcpServer.Api.Drawing;

internal static class DimensionProjectionHelper
{
    internal static DrawingLineInfo? TryCreateCommonReferenceLine(
        IReadOnlyList<(double X, double Y)> points,
        (double X, double Y) upDirection,
        double distance,
        out (double X, double Y) direction)
    {
        direction = default;
        if (points.Count == 0)
            return null;

        var rawDirection = TeklaDrawingDimensionsApi.CanonicalizeDirection(-upDirection.Y, upDirection.X);
        if (!TeklaDrawingDimensionsApi.TryNormalizeDirection(rawDirection.X, rawDirection.Y, out var normalizedDirection))
            return null;

        direction = normalizedDirection;

        var offsetProjection = points.Max(point => Project(point.X, point.Y, upDirection.X, upDirection.Y)) + distance;
        var minAlongProjection = points.Min(point => Project(point.X, point.Y, normalizedDirection.X, normalizedDirection.Y));
        var maxAlongProjection = points.Max(point => Project(point.X, point.Y, normalizedDirection.X, normalizedDirection.Y));

        var start = CreatePointOnDimensionLine(minAlongProjection, offsetProjection, normalizedDirection, upDirection);
        var end = CreatePointOnDimensionLine(maxAlongProjection, offsetProjection, normalizedDirection, upDirection);
        return TeklaDrawingDimensionsApi.CreateLineInfo(start.X, start.Y, end.X, end.Y);
    }

    internal static (double X, double Y) CreateReferencePoint(
        double pointX,
        double pointY,
        (double X, double Y) upDirection,
        double distance)
    {
        return (
            System.Math.Round(pointX + (upDirection.X * distance), 3),
            System.Math.Round(pointY + (upDirection.Y * distance), 3));
    }

    internal static (double X, double Y) ProjectPointToReferenceLine(
        double pointX,
        double pointY,
        double lineOriginX,
        double lineOriginY,
        double lineDirectionX,
        double lineDirectionY)
    {
        var projection = ((pointX - lineOriginX) * lineDirectionX) + ((pointY - lineOriginY) * lineDirectionY);
        return (
            System.Math.Round(lineOriginX + (projection * lineDirectionX), 3),
            System.Math.Round(lineOriginY + (projection * lineDirectionY), 3));
    }

    internal static (double X, double Y) CreatePointOnDimensionLine(
        double alongProjection,
        double offsetProjection,
        (double X, double Y) direction,
        (double X, double Y) upDirection)
    {
        return (
            System.Math.Round((direction.X * alongProjection) + (upDirection.X * offsetProjection), 3),
            System.Math.Round((direction.Y * alongProjection) + (upDirection.Y * offsetProjection), 3));
    }

    internal static Vector BuildPerpendicularOffsetDirection(Point start, Point end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var len = System.Math.Sqrt((dx * dx) + (dy * dy));
        if (len < 1e-6)
            return new Vector(0, 1, 0);

        return new Vector(-dy / len, dx / len, 0);
    }

    private static double Project(double x, double y, double axisX, double axisY) => (x * axisX) + (y * axisY);
}
