namespace TeklaMcpServer.Api.Drawing;

internal sealed class DimensionViewPlacementAnalysis
{
    public bool HasPartsBounds { get; set; }
    public string PartsBoundsSide { get; set; } = string.Empty;
    public bool IsOutsidePartsBounds { get; set; }
    public bool IntersectsPartsBounds { get; set; }
    public double? OffsetFromPartsBounds { get; set; }
}

internal static class DimensionViewPlacementAnalyzer
{
    private const double BoundsTolerance = 1.0;

    public static DimensionViewPlacementAnalysis Analyze(
        DimensionContext? dimensionContext,
        DimensionViewContext? viewContext)
    {
        var analysis = new DimensionViewPlacementAnalysis
        {
            HasPartsBounds = viewContext?.PartsBounds != null
        };

        if (dimensionContext?.ReferenceLine == null || viewContext?.PartsBounds == null)
            return analysis;

        var line = dimensionContext.ReferenceLine;
        var bounds = viewContext.PartsBounds;
        var lineBounds = TeklaDrawingDimensionsApi.CreateBoundsFromLine(line);
        analysis.IntersectsPartsBounds = Intersects(bounds, lineBounds);

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
                analysis.PartsBoundsSide = "top";
                analysis.IsOutsidePartsBounds = true;
                analysis.OffsetFromPartsBounds = System.Math.Round(midY - bounds.MaxY, 3);
                return analysis;
            }

            if (midY < bounds.MinY - BoundsTolerance)
            {
                analysis.PartsBoundsSide = "bottom";
                analysis.IsOutsidePartsBounds = true;
                analysis.OffsetFromPartsBounds = System.Math.Round(bounds.MinY - midY, 3);
                return analysis;
            }

            analysis.PartsBoundsSide = "overlap";
            analysis.OffsetFromPartsBounds = 0;
            return analysis;
        }

        if (isVertical)
        {
            if (midX > bounds.MaxX + BoundsTolerance)
            {
                analysis.PartsBoundsSide = "right";
                analysis.IsOutsidePartsBounds = true;
                analysis.OffsetFromPartsBounds = System.Math.Round(midX - bounds.MaxX, 3);
                return analysis;
            }

            if (midX < bounds.MinX - BoundsTolerance)
            {
                analysis.PartsBoundsSide = "left";
                analysis.IsOutsidePartsBounds = true;
                analysis.OffsetFromPartsBounds = System.Math.Round(bounds.MinX - midX, 3);
                return analysis;
            }

            analysis.PartsBoundsSide = "overlap";
            analysis.OffsetFromPartsBounds = 0;
            return analysis;
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

        analysis.PartsBoundsSide = side;
        if (maxDistance > BoundsTolerance)
        {
            analysis.IsOutsidePartsBounds = true;
            analysis.OffsetFromPartsBounds = System.Math.Round(maxDistance, 3);
        }
        else
        {
            analysis.OffsetFromPartsBounds = 0;
        }

        return analysis;
    }

    private static bool Intersects(DrawingBoundsInfo bounds, DrawingBoundsInfo lineBounds)
    {
        return !(lineBounds.MaxX < bounds.MinX - BoundsTolerance ||
                 lineBounds.MinX > bounds.MaxX + BoundsTolerance ||
                 lineBounds.MaxY < bounds.MinY - BoundsTolerance ||
                 lineBounds.MinY > bounds.MaxY + BoundsTolerance);
    }
}
