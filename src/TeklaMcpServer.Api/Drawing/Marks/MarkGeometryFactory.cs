using Tekla.Structures.Drawing;

namespace TeklaMcpServer.Api.Drawing;

internal static class MarkGeometryFactory
{
    public static MarkGeometryInfo BuildFromObjectAlignedBox(RectangleBoundingBox objectAligned, string source, bool isReliable)
    {
        var corners = new List<double[]>
        {
            new[] { objectAligned.LowerLeft.X, objectAligned.LowerLeft.Y },
            new[] { objectAligned.UpperLeft.X, objectAligned.UpperLeft.Y },
            new[] { objectAligned.UpperRight.X, objectAligned.UpperRight.Y },
            new[] { objectAligned.LowerRight.X, objectAligned.LowerRight.Y }
        };

        var minX = Math.Min(Math.Min(corners[0][0], corners[1][0]), Math.Min(corners[2][0], corners[3][0]));
        var maxX = Math.Max(Math.Max(corners[0][0], corners[1][0]), Math.Max(corners[2][0], corners[3][0]));
        var minY = Math.Min(Math.Min(corners[0][1], corners[1][1]), Math.Min(corners[2][1], corners[3][1]));
        var maxY = Math.Max(Math.Max(corners[0][1], corners[1][1]), Math.Max(corners[2][1], corners[3][1]));

        return new MarkGeometryInfo
        {
            CenterX = (objectAligned.MinPoint.X + objectAligned.MaxPoint.X) / 2.0,
            CenterY = (objectAligned.MinPoint.Y + objectAligned.MaxPoint.Y) / 2.0,
            Width = objectAligned.Width,
            Height = objectAligned.Height,
            MinX = minX,
            MinY = minY,
            MaxX = maxX,
            MaxY = maxY,
            AngleDeg = objectAligned.AngleToAxis,
            HasAxis = false,
            IsReliable = isReliable,
            Source = source,
            Corners = corners
        };
    }

    public static MarkGeometryInfo BuildFromAxis(
        double centerX,
        double centerY,
        double objectWidth,
        double objectHeight,
        double axisDx,
        double axisDy,
        double textAngleDeg,
        string source,
        bool isReliable)
    {
        var vx = -axisDy;
        var vy = axisDx;
        var (widthAlongAxis, heightPerpendicularToAxis) = MarkGeometryMath.ResolveDimensionsForAxis(
            objectWidth,
            objectHeight,
            axisDx,
            axisDy,
            textAngleDeg);
        var halfWidth = widthAlongAxis / 2.0;
        var halfHeight = heightPerpendicularToAxis / 2.0;

        var p1 = new[] { centerX - (axisDx * halfWidth) - (vx * halfHeight), centerY - (axisDy * halfWidth) - (vy * halfHeight) };
        var p2 = new[] { centerX + (axisDx * halfWidth) - (vx * halfHeight), centerY + (axisDy * halfWidth) - (vy * halfHeight) };
        var p3 = new[] { centerX + (axisDx * halfWidth) + (vx * halfHeight), centerY + (axisDy * halfWidth) + (vy * halfHeight) };
        var p4 = new[] { centerX - (axisDx * halfWidth) + (vx * halfHeight), centerY - (axisDy * halfWidth) + (vy * halfHeight) };

        var minX = Math.Min(Math.Min(p1[0], p2[0]), Math.Min(p3[0], p4[0]));
        var maxX = Math.Max(Math.Max(p1[0], p2[0]), Math.Max(p3[0], p4[0]));
        var minY = Math.Min(Math.Min(p1[1], p2[1]), Math.Min(p3[1], p4[1]));
        var maxY = Math.Max(Math.Max(p1[1], p2[1]), Math.Max(p3[1], p4[1]));

        return new MarkGeometryInfo
        {
            CenterX = centerX,
            CenterY = centerY,
            Width = widthAlongAxis,
            Height = heightPerpendicularToAxis,
            MinX = minX,
            MinY = minY,
            MaxX = maxX,
            MaxY = maxY,
            AngleDeg = Math.Atan2(axisDy, axisDx) * (180.0 / Math.PI),
            AxisDx = axisDx,
            AxisDy = axisDy,
            HasAxis = true,
            IsReliable = isReliable,
            Source = source,
            Corners = new List<double[]> { p1, p2, p3, p4 }
        };
    }
}
