namespace TeklaMcpServer.Api.Drawing;

internal sealed class DimensionViewPlacementInfo
{
    public bool HasPartsBounds { get; set; }
    public string PartsBoundsSide { get; set; } = string.Empty;
    public bool IsOutsidePartsBounds { get; set; }
    public bool IntersectsPartsBounds { get; set; }
    public double? OffsetFromPartsBounds { get; set; }
    public double? ReferenceLineLength { get; set; }
    public double Distance { get; set; }
    public int TopDirection { get; set; }
    public double ViewScale { get; set; }
}

internal static class DimensionViewPlacementInfoBuilder
{
    private const double BoundsTolerance = 1.0;

    public static DimensionViewPlacementInfo Build(
        DimensionContext? dimensionContext,
        DimensionViewContext? viewContext)
    {
        var info = new DimensionViewPlacementInfo
        {
            HasPartsBounds = viewContext?.PartsBounds != null,
            ReferenceLineLength = dimensionContext?.ReferenceLine?.Length,
            Distance = dimensionContext?.Distance ?? 0,
            TopDirection = dimensionContext?.Item.TopDirection ?? 0,
            ViewScale = viewContext?.ViewScale ?? dimensionContext?.ViewScale ?? 0
        };

        if (dimensionContext?.ReferenceLine == null || viewContext?.PartsBounds == null)
            return info;

        var line = dimensionContext.ReferenceLine;
        var bounds = viewContext.PartsBounds;
        var lineBounds = TeklaDrawingDimensionsApi.CreateBoundsFromLine(line);
        info.IntersectsPartsBounds = Intersects(bounds, lineBounds);

        var dx = line.EndX - line.StartX;
        var dy = line.EndY - line.StartY;
        var midX = (line.StartX + line.EndX) / 2.0;
        var midY = (line.StartY + line.EndY) / 2.0;
        var isHorizontal = System.Math.Abs(dy) <= System.Math.Abs(dx) * 0.01;
        var isVertical = System.Math.Abs(dx) <= System.Math.Abs(dy) * 0.01;

        if (isHorizontal)
        {
            if (midY > bounds.MaxY + BoundsTolerance)
            {
                info.PartsBoundsSide = "top";
                info.IsOutsidePartsBounds = true;
                info.OffsetFromPartsBounds = System.Math.Round(midY - bounds.MaxY, 3);
                return info;
            }

            if (midY < bounds.MinY - BoundsTolerance)
            {
                info.PartsBoundsSide = "bottom";
                info.IsOutsidePartsBounds = true;
                info.OffsetFromPartsBounds = System.Math.Round(bounds.MinY - midY, 3);
                return info;
            }

            info.PartsBoundsSide = "overlap";
            info.OffsetFromPartsBounds = 0;
            return info;
        }

        if (isVertical)
        {
            if (midX > bounds.MaxX + BoundsTolerance)
            {
                info.PartsBoundsSide = "right";
                info.IsOutsidePartsBounds = true;
                info.OffsetFromPartsBounds = System.Math.Round(midX - bounds.MaxX, 3);
                return info;
            }

            if (midX < bounds.MinX - BoundsTolerance)
            {
                info.PartsBoundsSide = "left";
                info.IsOutsidePartsBounds = true;
                info.OffsetFromPartsBounds = System.Math.Round(bounds.MinX - midX, 3);
                return info;
            }

            info.PartsBoundsSide = "overlap";
            info.OffsetFromPartsBounds = 0;
            return info;
        }

        var topDistance = midY - bounds.MaxY;
        var bottomDistance = bounds.MinY - midY;
        var leftDistance = bounds.MinX - midX;
        var rightDistance = midX - bounds.MaxX;

        var maxDistance = 0.0;
        var side = "overlap";

        if (topDistance > maxDistance)
        {
            maxDistance = topDistance;
            side = "top";
        }

        if (bottomDistance > maxDistance)
        {
            maxDistance = bottomDistance;
            side = "bottom";
        }

        if (leftDistance > maxDistance)
        {
            maxDistance = leftDistance;
            side = "left";
        }

        if (rightDistance > maxDistance)
        {
            maxDistance = rightDistance;
            side = "right";
        }

        info.PartsBoundsSide = side;
        if (maxDistance > BoundsTolerance)
        {
            info.IsOutsidePartsBounds = true;
            info.OffsetFromPartsBounds = System.Math.Round(maxDistance, 3);
        }
        else
        {
            info.OffsetFromPartsBounds = 0;
        }

        return info;
    }

    private static bool Intersects(DrawingBoundsInfo bounds, DrawingBoundsInfo lineBounds)
    {
        return !(lineBounds.MaxX < bounds.MinX - BoundsTolerance ||
                 lineBounds.MinX > bounds.MaxX + BoundsTolerance ||
                 lineBounds.MaxY < bounds.MinY - BoundsTolerance ||
                 lineBounds.MinY > bounds.MaxY + BoundsTolerance);
    }
}
