using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Algorithms.Marks;

internal static class ForceLayoutUnitConverter
{
    public static double NormalizeViewScale(double viewScale) => viewScale > 0.0 ? viewScale : 1.0;

    public static ForceDirectedMarkItem ToPaperSpace(ForceDirectedMarkItem item, double viewScale)
    {
        var scale = NormalizeViewScale(viewScale);
        return new ForceDirectedMarkItem
        {
            Id = item.Id,
            OwnModelId = item.OwnModelId,
            Cx = item.Cx / scale,
            Cy = item.Cy / scale,
            Width = item.Width / scale,
            Height = item.Height / scale,
            CanMove = item.CanMove,
            ConstrainToAxis = item.ConstrainToAxis,
            ReturnToAxisLine = item.ReturnToAxisLine,
            AxisDx = item.AxisDx,
            AxisDy = item.AxisDy,
            AxisOriginX = item.AxisOriginX / scale,
            AxisOriginY = item.AxisOriginY / scale,
            LocalCorners = ScalePoints(item.LocalCorners, scale),
            OwnPolygon = item.OwnPolygon == null ? null : ScalePoints(item.OwnPolygon, scale)
        };
    }

    public static List<ForceDirectedMarkItem> ToPaperSpace(
        IEnumerable<ForceDirectedMarkItem> items,
        double viewScale) =>
        items.Select(item => ToPaperSpace(item, viewScale)).ToList();

    public static PartBbox ToPaperSpace(PartBbox part, double viewScale)
    {
        var scale = NormalizeViewScale(viewScale);
        return new PartBbox(
            part.ModelId,
            part.MinX / scale,
            part.MinY / scale,
            part.MaxX / scale,
            part.MaxY / scale,
            part.Polygon == null ? null : ScalePoints(part.Polygon, scale));
    }

    public static List<PartBbox> ToPaperSpace(IEnumerable<PartBbox> parts, double viewScale) =>
        parts.Select(part => ToPaperSpace(part, viewScale)).ToList();

    private static List<double[]> ScalePoints(IReadOnlyList<double[]> points, double scale) =>
        points
            .Select(point => new[] { point[0] / scale, point[1] / scale })
            .ToList();
}
