using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class DimensionGroupPairSpacing
{
    public int FirstDimensionId { get; set; }
    public int SecondDimensionId { get; set; }
    public double Distance { get; set; }
    public bool IsOverlap => Distance < 0;
}

internal sealed class DimensionGroupSpacingAnalysis
{
    public int? ViewId { get; set; }
    public string ViewType { get; set; } = string.Empty;
    public string Orientation { get; set; } = string.Empty;
    public bool HasOverlaps { get; set; }
    public double? MinimumDistance { get; set; }
    public List<DimensionGroupPairSpacing> Pairs { get; } = [];
}

internal static class DimensionGroupSpacingAnalyzer
{
    public static DimensionGroupSpacingAnalysis Analyze(DimensionGroup group)
    {
        var analysis = new DimensionGroupSpacingAnalysis
        {
            ViewId = group.ViewId,
            ViewType = group.ViewType,
            Orientation = group.Orientation
        };

        var intervals = group.Members
            .Select(member => (Member: member, Interval: TryGetInterval(member, group)))
            .Where(static item => item.Interval != null)
            .Select(static item => (item.Member, Interval: item.Interval!.Value))
            .OrderBy(static item => item.Interval.Min)
            .ToList();

        for (var i = 0; i < intervals.Count - 1; i++)
        {
            var current = intervals[i];
            var next = intervals[i + 1];
            var distance = System.Math.Round(next.Interval.Min - current.Interval.Max, 3);

            analysis.Pairs.Add(new DimensionGroupPairSpacing
            {
                FirstDimensionId = current.Member.DimensionId,
                SecondDimensionId = next.Member.DimensionId,
                Distance = distance
            });
        }

        if (analysis.Pairs.Count > 0)
        {
            analysis.MinimumDistance = analysis.Pairs.Min(static pair => pair.Distance);
            analysis.HasOverlaps = analysis.Pairs.Any(static pair => pair.IsOverlap);
        }

        return analysis;
    }

    private static AxisInterval? TryGetInterval(DimensionGroupMember member, DimensionGroup group)
    {
        var bounds = member.Bounds;
        if (bounds == null)
            return null;

        return group.Orientation switch
        {
            "horizontal" => new AxisInterval(bounds.MinY, bounds.MaxY),
            "vertical" => new AxisInterval(bounds.MinX, bounds.MaxX),
            _ => TryGetProjectedInterval(bounds, group.Direction)
        };
    }

    private static AxisInterval? TryGetProjectedInterval(DrawingBoundsInfo bounds, (double X, double Y)? direction)
    {
        if (!direction.HasValue)
            return new AxisInterval(bounds.MinX + bounds.MinY, bounds.MaxX + bounds.MaxY);

        var normalX = -direction.Value.Y;
        var normalY = direction.Value.X;
        var normalLength = System.Math.Sqrt((normalX * normalX) + (normalY * normalY));
        if (normalLength <= 1e-6)
            return null;

        normalX /= normalLength;
        normalY /= normalLength;

        var projections = new[]
        {
            Project(bounds.MinX, bounds.MinY, normalX, normalY),
            Project(bounds.MinX, bounds.MaxY, normalX, normalY),
            Project(bounds.MaxX, bounds.MinY, normalX, normalY),
            Project(bounds.MaxX, bounds.MaxY, normalX, normalY)
        };

        return new AxisInterval(projections.Min(), projections.Max());
    }

    private static double Project(double x, double y, double axisX, double axisY) => (x * axisX) + (y * axisY);

    private readonly struct AxisInterval
    {
        public AxisInterval(double min, double max)
        {
            Min = min;
            Max = max;
        }

        public double Min { get; }
        public double Max { get; }
    }
}
