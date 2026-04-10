namespace TeklaMcpServer.Api.Drawing;

internal static class MarkGeometryFactory
{
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

    public static MarkGeometryInfo BuildFromProjectedPolygon(
        IReadOnlyList<double[]> polygon,
        double axisDx,
        double axisDy,
        string source,
        bool isReliable)
    {
        var axisLength = Math.Sqrt((axisDx * axisDx) + (axisDy * axisDy));
        if (axisLength < 0.001)
            return BuildFromPolygon(polygon, source, isReliable: false);

        axisDx /= axisLength;
        axisDy /= axisLength;
        var vx = -axisDy;
        var vy = axisDx;

        var minU = double.MaxValue;
        var maxU = double.MinValue;
        var minV = double.MaxValue;
        var maxV = double.MinValue;
        foreach (var point in polygon)
        {
            var u = (point[0] * axisDx) + (point[1] * axisDy);
            var v = (point[0] * vx) + (point[1] * vy);
            minU = Math.Min(minU, u);
            maxU = Math.Max(maxU, u);
            minV = Math.Min(minV, v);
            maxV = Math.Max(maxV, v);
        }

        var widthAlongAxis = maxU - minU;
        var heightPerpendicularToAxis = maxV - minV;
        var centerU = (minU + maxU) / 2.0;
        var centerV = (minV + maxV) / 2.0;
        var centerX = (axisDx * centerU) + (vx * centerV);
        var centerY = (axisDy * centerU) + (vy * centerV);
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
