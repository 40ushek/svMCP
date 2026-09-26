using System;
using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class OverallDimensionRule(OverallDimensionSettings settings) : IDimensionRule
{
    public DimensionRuleEvaluation Calculate(DimensionRuleContext context)
    {
        var results = new List<DimensionRuleResult>();
        if (settings.HorizontalSide is { } horizontal)
            results.Add(Calculate(context, horizontal, alongX: true));
        if (settings.VerticalSide is { } vertical)
            results.Add(Calculate(context, vertical, alongX: false));
        return new DimensionRuleEvaluation(results);
    }

    private DimensionRuleResult Calculate(DimensionRuleContext context, DimensionChainSide side, bool alongX)
    {
        var direction = new DimensionDirection(alongX ? 1 : 0, alongX ? 0 : 1);
        var outer = side is DimensionChainSide.Top or DimensionChainSide.Right ? 1 : -1;
        var placement = new OutsideOutlineDimensionPlacement(
            new DimensionDirection(alongX ? 0 : outer, alongX ? outer : 0), "second");
        var geometry = context.AxisAlignedGeometry;
        if (geometry == null)
            return new(direction, placement, "overall", [], "axis-aligned snapshot geometry is unavailable");
        // Preserve the horizontal preview's side-specific extremes. Height is the
        // entire panel extent, including a taller end on an asymmetric panel.
        var (min, max) = alongX
            ? geometry.SideExtremes(side, settings.PositionTolerance)
            : (geometry.Extent.MinY, geometry.Extent.MaxY);
        double Coordinate(DimensionPoint p) => alongX ? p.X : p.Y;
        var line = geometry.Points.LinePoints(side);
        DimensionPoint? Find(double value) => line
            .Where(p => Math.Abs(Coordinate(p) - value) <= settings.PositionTolerance)
            .OrderByDescending(p => outer * (alongX ? p.Y : p.X)).FirstOrDefault();
        var first = Find(min);
        var last = Find(max);
        if (first == null || last == null)
            return new(direction, placement, "overall", [], "panel extremes are not supported by existing dimension points");
        return new(direction, placement, "overall", [context.GetPoint(first.Id), context.GetPoint(last.Id)],
            segments: new[] { Math.Round(max - min, 3) });
    }
}
