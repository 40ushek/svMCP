using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>Optional geometry adapter for current four-side rules, not a requirement of all rules.</summary>
internal sealed class AxisAlignedDimensionGeometry(DimensionPointCatalog points, GeometryGroupExtent extent)
{
    public DimensionPointCatalog Points { get; } = points;
    public GeometryGroupExtent Extent { get; } = extent;

    internal (double Min, double Max) SideExtremes(DimensionChainSide side, double tolerance)
    {
        var alongX = side is DimensionChainSide.Top or DimensionChainSide.Bottom;
        return SideExtremes(Points, side, alongX ? Extent.MinX : Extent.MinY,
            alongX ? Extent.MaxX : Extent.MaxY, tolerance);
    }

    internal static (double Min, double Max) SideExtremes(DimensionPointCatalog points,
        DimensionChainSide side, double min, double max, double tolerance)
    {
        var alongX = side is DimensionChainSide.Top or DimensionChainSide.Bottom;
        var line = points.LinePoints(side);
        if (line.Count == 0) return (min, max);
        double Across(DimensionPoint p) => alongX ? p.Y : p.X;
        var middle = (line.Max(Across) + line.Min(Across)) / 2;
        var outer = side is DimensionChainSide.Top or DimensionChainSide.Right ? 1 : -1;
        var half = line.Where(p => outer * (Across(p) - middle) >= -tolerance).ToArray();
        if (half.Length == 0) return (min, max);
        return (half.Min(p => alongX ? p.X : p.Y), half.Max(p => alongX ? p.X : p.Y));
    }
}
