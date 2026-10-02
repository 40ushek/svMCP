using System;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>Selects the verified main-profile faces offered on one section side.</summary>
internal sealed class SectionProfileDimensionRule(SectionProfileDimensionSettings settings) : IDimensionRule
{
    public DimensionRuleEvaluation Calculate(DimensionRuleContext context)
    {
        var geometry = context.AxisAlignedGeometry;
        if (geometry == null)
            return new DimensionRuleEvaluation([Refused("axis-aligned snapshot geometry is unavailable")]);
        var input = context.Require<SectionProfileDimensionInput>();
        var side = settings.Side;
        var alongX = side is DimensionChainSide.Top or DimensionChainSide.Bottom;
        var levels = alongX ? input.XLevels : input.YLevels;
        var line = geometry.Points.LinePoints(side);
        double Along(DimensionPoint point) => alongX ? point.X : point.Y;
        double Across(DimensionPoint point) => alongX ? point.Y : point.X;
        var outer = side is DimensionChainSide.Top or DimensionChainSide.Right ? 1 : -1;

        var profile = levels.Select(level => line
            .Where(point => point.Parents.Any(parent => parent.ModelId == input.MainPartId)
                && Math.Abs(Along(point) - level) <= settings.SameCoordinateTolerance)
            .OrderByDescending(point => outer * Across(point))
            .ThenBy(point => point.Id, StringComparer.Ordinal)
            .FirstOrDefault()).ToArray();
        if (profile.Any(point => point == null))
            return new DimensionRuleEvaluation([Refused("main-profile support is not offered on this side")]);

        var points = profile.Select(point => context.GetPoint(point!.Id)).ToArray();
        var segments = points.Zip(points.Skip(1), (first, second) =>
            Math.Round((alongX ? second.X - first.X : second.Y - first.Y), 3));
        return new DimensionRuleEvaluation([new DimensionRuleResult(Direction(alongX), Placement(side, alongX),
            "profile", points, segments: segments)]);
    }

    private DimensionRuleResult Refused(string reason)
    {
        var alongX = settings.Side is DimensionChainSide.Top or DimensionChainSide.Bottom;
        return new DimensionRuleResult(Direction(alongX), Placement(settings.Side, alongX), "profile", [], reason);
    }

    private static DimensionDirection Direction(bool alongX) =>
        new(alongX ? 1 : 0, alongX ? 0 : 1);

    private static OutsideOutlineDimensionPlacement Placement(DimensionChainSide side, bool alongX)
    {
        var outer = side is DimensionChainSide.Top or DimensionChainSide.Right ? 1 : -1;
        return new OutsideOutlineDimensionPlacement(
            new DimensionDirection(alongX ? 0 : outer, alongX ? outer : 0), 1);
    }
}
