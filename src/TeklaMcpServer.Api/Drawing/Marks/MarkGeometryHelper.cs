using System.Reflection;
using Tekla.Structures.Drawing;
using Tekla.Structures.Model;
using TeklaMcpServer.Api.Algorithms.Geometry;

namespace TeklaMcpServer.Api.Drawing;

public sealed class MarkGeometryInfo
{
    public double CenterX { get; set; }
    public double CenterY { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double MinX { get; set; }
    public double MinY { get; set; }
    public double MaxX { get; set; }
    public double MaxY { get; set; }
    public double AngleDeg { get; set; }
    public double AxisDx { get; set; }
    public double AxisDy { get; set; }
    public bool HasAxis { get; set; }
    public bool IsReliable { get; set; }
    public string Source { get; set; } = string.Empty;
    public List<double[]> Corners { get; set; } = new();
}

// NOTE: Critical for drawing mark polygon geometry used by debug and overlap logic.
// Do not change this code without prior warning and approval.
// Do not delete or replace behavior without a 1:1 validated equivalent.
public static class MarkGeometryHelper
{
    private const double Epsilon = 1e-9;

    public static MarkGeometryInfo Build(Mark mark, Model model, int? viewId = null)
    {
        var bbox = mark.GetAxisAlignedBoundingBox();
        var objectAligned = mark.GetObjectAlignedBoundingBox();
        var centerX = (bbox.MinPoint.X + bbox.MaxPoint.X) / 2.0;
        var centerY = (bbox.MinPoint.Y + bbox.MaxPoint.Y) / 2.0;

        if (mark.Placing is LeaderLinePlacing)
            return BuildFromObjectAlignedBox(objectAligned, "ObjectAlignedBoundingBox", true);

        if (mark.Placing is BaseLinePlacing baseLinePlacing)
        {
            if (TryGetRelatedPartAxisInView(mark, model, viewId, out var partAxisDx, out var partAxisDy))
                return BuildFromAxis(centerX, centerY, objectAligned.Width, objectAligned.Height, partAxisDx, partAxisDy, mark.Attributes.Angle, "RelatedPartAxis", true);

            if (TryGetBaselineAxis(baseLinePlacing, out var placingAxisDx, out var placingAxisDy))
                return BuildFromAxis(centerX, centerY, objectAligned.Width, objectAligned.Height, placingAxisDx, placingAxisDy, mark.Attributes.Angle, "BaseLinePlacingAxisFallback", true);

            var rad = mark.Attributes.Angle * Math.PI / 180.0;
            var angleDx = Math.Cos(rad);
            var angleDy = Math.Sin(rad);
            if (Math.Abs(angleDx) >= 0.001 || Math.Abs(angleDy) >= 0.001)
                return BuildFromAxis(centerX, centerY, objectAligned.Width, objectAligned.Height, angleDx, angleDy, mark.Attributes.Angle, "MarkAngleFallback", false);
        }

        return BuildFromObjectAlignedBox(objectAligned, "ObjectAlignedBoundingBoxFallback", false);
    }

    private static bool TryGetBaselineAxis(BaseLinePlacing baseLinePlacing, out double axisDx, out double axisDy)
    {
        axisDx = baseLinePlacing.EndPoint.X - baseLinePlacing.StartPoint.X;
        axisDy = baseLinePlacing.EndPoint.Y - baseLinePlacing.StartPoint.Y;
        var axisLength = Math.Sqrt((axisDx * axisDx) + (axisDy * axisDy));
        if (axisLength < 0.001)
            return false;

        axisDx /= axisLength;
        axisDy /= axisLength;
        return true;
    }

    private static MarkGeometryInfo BuildFromObjectAlignedBox(RectangleBoundingBox objectAligned, string source, bool isReliable)
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

    private static MarkGeometryInfo BuildFromAxis(
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
        var (widthAlongAxis, heightPerpendicularToAxis) = ResolveDimensionsForAxis(
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

    internal static (double WidthAlongAxis, double HeightPerpendicularToAxis) ResolveDimensionsForAxis(
        double objectWidth,
        double objectHeight,
        double axisDx,
        double axisDy,
        double textAngleDeg)
    {
        var axisAngleDeg = Math.Atan2(axisDy, axisDx) * (180.0 / Math.PI);
        var normalizedDelta = NormalizeAngleDelta180(textAngleDeg - axisAngleDeg);

        // Tekla reports OBB width/height relative to text direction.
        // When the text is closer to perpendicular than parallel to the part axis,
        // swap dimensions so width stays along the axis used by layout/debug geometry.
        return normalizedDelta > 45.0
            ? (objectHeight, objectWidth)
            : (objectWidth, objectHeight);
    }

    private static bool TryGetRelatedPartAxisInView(Mark mark, Model model, int? explicitViewId, out double axisDx, out double axisDy)
    {
        axisDx = 0.0;
        axisDy = 0.0;

        var viewId = explicitViewId.GetValueOrDefault();
        if (viewId == 0)
        {
            var ownerView = mark.GetView();
            if (ownerView == null)
                return false;

            viewId = TryGetIdentifierId(ownerView);
        }

        if (viewId == 0)
            return false;

        var related = mark.GetRelatedObjects();
        var partGeometryApi = new TeklaDrawingPartGeometryApi(model);
        while (related.MoveNext())
        {
            if (related.Current is not Tekla.Structures.Drawing.ModelObject drawingModelObject)
                continue;

            var result = partGeometryApi.GetPartGeometryInView(viewId, drawingModelObject.ModelIdentifier.ID);
            if (!result.Success || result.StartPoint.Length < 2 || result.EndPoint.Length < 2)
                continue;

            axisDx = result.EndPoint[0] - result.StartPoint[0];
            axisDy = result.EndPoint[1] - result.StartPoint[1];
            var axisLength = Math.Sqrt((axisDx * axisDx) + (axisDy * axisDy));
            if (axisLength < 0.001)
                continue;

            axisDx /= axisLength;
            axisDy /= axisLength;
            return true;
        }

        return false;
    }

    private static int TryGetIdentifierId(object drawingObject)
    {
        var getIdentifier = drawingObject.GetType().GetMethod("GetIdentifier", BindingFlags.Instance | BindingFlags.Public);
        var identifier = getIdentifier?.Invoke(drawingObject, null);
        var idProperty = identifier?.GetType().GetProperty("ID", BindingFlags.Instance | BindingFlags.Public);
        return idProperty?.GetValue(identifier) as int? ?? 0;
    }

    public static bool PolygonsIntersect(IReadOnlyList<double[]> first, IReadOnlyList<double[]> second)
    {
        return PolygonGeometry.Intersects(first, second);
    }

    public static bool TryGetMinimumTranslationVector(
        IReadOnlyList<double[]> first,
        IReadOnlyList<double[]> second,
        out double axisX,
        out double axisY,
        out double depth)
    {
        return PolygonGeometry.TryGetMinimumTranslationVector(first, second, out axisX, out axisY, out depth);
    }

    public static List<double[]> TranslateLocalCorners(IReadOnlyList<double[]> localCorners, double centerX, double centerY)
    {
        return PolygonGeometry.Translate(localCorners, centerX, centerY);
    }

    public static void GetPolygonBounds(
        IReadOnlyList<double[]> polygon,
        out double minX,
        out double minY,
        out double maxX,
        out double maxY)
    {
        PolygonGeometry.GetBounds(polygon, out minX, out minY, out maxX, out maxY);
    }

    public static bool RectanglesOverlap(
        double firstMinX,
        double firstMinY,
        double firstMaxX,
        double firstMaxY,
        double secondMinX,
        double secondMinY,
        double secondMaxX,
        double secondMaxY)
    {
        return PolygonGeometry.RectanglesOverlap(firstMinX, firstMinY, firstMaxX, firstMaxY, secondMinX, secondMinY, secondMaxX, secondMaxY);
    }

    private static double NormalizeAngleDelta180(double angleDeg)
    {
        var normalized = angleDeg % 180.0;
        if (normalized < 0)
            normalized += 180.0;

        return normalized > 90.0
            ? 180.0 - normalized
            : normalized;
    }
}
