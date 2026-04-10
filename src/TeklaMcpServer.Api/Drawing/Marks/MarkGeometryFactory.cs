using Tekla.Structures.Drawing;

namespace TeklaMcpServer.Api.Drawing;

internal static class MarkGeometryFactory
{
    public static bool TryGetObjectAlignedBoundingBox(Mark mark, out RectangleBoundingBox box)
    {
        try
        {
            box = mark.GetObjectAlignedBoundingBox();
            return box.Width >= 0.001 || box.Height >= 0.001;
        }
        catch
        {
            box = null!;
            return false;
        }
    }

    public static MarkGeometryInfo BuildFromPolygon(IReadOnlyList<double[]> polygon, string source, bool isReliable)
    {
        MarkGeometryMath.GetPolygonBounds(polygon, out var minX, out var minY, out var maxX, out var maxY);
        var corners = polygon.Select(static point => new[] { point[0], point[1] }).ToList();

        return new MarkGeometryInfo
        {
            CenterX = (minX + maxX) / 2.0,
            CenterY = (minY + maxY) / 2.0,
            Width = maxX - minX,
            Height = maxY - minY,
            MinX = minX,
            MinY = minY,
            MaxX = maxX,
            MaxY = maxY,
            AngleDeg = 0.0,
            HasAxis = false,
            IsReliable = isReliable,
            Source = source,
            Corners = corners
        };
    }

    public static MarkGeometryInfo BuildFromObjectAlignedBox(RectangleBoundingBox box, string source, bool isReliable)
    {
        var centerX = (box.MinPoint.X + box.MaxPoint.X) / 2.0;
        var centerY = (box.MinPoint.Y + box.MaxPoint.Y) / 2.0;

        return new MarkGeometryInfo
        {
            CenterX = centerX,
            CenterY = centerY,
            Width = box.Width,
            Height = box.Height,
            MinX = box.MinPoint.X,
            MinY = box.MinPoint.Y,
            MaxX = box.MaxPoint.X,
            MaxY = box.MaxPoint.Y,
            AngleDeg = 0.0,
            HasAxis = false,
            IsReliable = isReliable,
            Source = source,
            Corners = new List<double[]>
            {
                new[] { box.LowerLeft.X, box.LowerLeft.Y },
                new[] { box.UpperLeft.X, box.UpperLeft.Y },
                new[] { box.UpperRight.X, box.UpperRight.Y },
                new[] { box.LowerRight.X, box.LowerRight.Y }
            }
        };
    }

    public static MarkGeometryInfo BuildFromObjectAlignedBoxAndAxis(
        RectangleBoundingBox box,
        double axisDx,
        double axisDy,
        string source,
        bool isReliable)
    {
        var axisLength = Math.Sqrt((axisDx * axisDx) + (axisDy * axisDy));
        if (axisLength < 0.001)
            return BuildFromObjectAlignedBox(box, source, isReliable: false);

        axisDx /= axisLength;
        axisDy /= axisLength;

        var centerX = (box.MinPoint.X + box.MaxPoint.X) / 2.0;
        var centerY = (box.MinPoint.Y + box.MaxPoint.Y) / 2.0;
        var halfWidth = box.Width / 2.0;
        var halfHeight = box.Height / 2.0;
        var vx = -axisDy;
        var vy = axisDx;

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
            Width = box.Width,
            Height = box.Height,
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

    public static MarkGeometryInfo BuildFromInsertionPoint(double x, double y, string source, bool isReliable)
    {
        return new MarkGeometryInfo
        {
            CenterX = x,
            CenterY = y,
            Width = 0.0,
            Height = 0.0,
            MinX = x,
            MinY = y,
            MaxX = x,
            MaxY = y,
            AngleDeg = 0.0,
            HasAxis = false,
            IsReliable = isReliable,
            Source = source,
            Corners = new List<double[]> { new[] { x, y } }
        };
    }
}
