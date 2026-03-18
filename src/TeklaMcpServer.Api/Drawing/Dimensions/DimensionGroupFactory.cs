using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

internal static class DimensionGroupFactory
{
    public static List<DimensionGroup> BuildGroups(GetDimensionsResult result) => BuildGroups(result.Dimensions);

    public static List<DimensionGroup> BuildGroups(IEnumerable<DrawingDimensionInfo> dimensions)
    {
        return dimensions
            .GroupBy(static d => new DimensionGroupKey(d.ViewId, d.ViewType ?? string.Empty, d.Orientation ?? string.Empty))
            .Select(BuildGroup)
            .ToList();
    }

    private static DimensionGroup BuildGroup(IGrouping<DimensionGroupKey, DrawingDimensionInfo> group)
    {
        var dimensions = group.ToList();
        var dimensionGroup = new DimensionGroup
        {
            ViewId = group.Key.ViewId,
            ViewType = group.Key.ViewType,
            Orientation = group.Key.Orientation
        };

        foreach (var dimension in dimensions)
        {
            var bounds = dimension.Bounds ?? TeklaDrawingDimensionsApi.CombineBounds(
                dimension.Segments.Select(static s => s.Bounds));
            dimensionGroup.Members.Add(new DimensionGroupMember
            {
                DimensionId = dimension.Id,
                Distance = dimension.Distance,
                Bounds = bounds,
                SortKey = DetermineSortKey(dimension, group.Key.Orientation),
                Dimension = dimension
            });
        }

        dimensionGroup.SortMembers();
        dimensionGroup.RefreshMetrics();

        if (TryGetRepresentativeSegment(dimensions, out var representative))
        {
            dimensionGroup.Direction = BuildDirection(representative);
            dimensionGroup.ReferenceLine = new DimensionReferenceLine
            {
                StartX = representative.StartX,
                StartY = representative.StartY,
                EndX = representative.EndX,
                EndY = representative.EndY
            };
        }

        return dimensionGroup;
    }

    private static double DetermineSortKey(DrawingDimensionInfo dimension, string orientation)
    {
        var bounds = dimension.Bounds ?? TeklaDrawingDimensionsApi.CombineBounds(
            dimension.Segments.Select(static s => s.Bounds));
        if (bounds == null)
            return double.MaxValue;

        return orientation switch
        {
            "horizontal" => bounds.MinY,
            "vertical" => bounds.MinX,
            _ => bounds.MinX + bounds.MinY
        };
    }

    private static bool TryGetRepresentativeSegment(
        IReadOnlyList<DrawingDimensionInfo> dimensions,
        out DimensionSegmentInfo representative)
    {
        representative = new DimensionSegmentInfo();
        var bestLengthSquared = 0.0;

        foreach (var dimension in dimensions)
        {
            foreach (var segment in dimension.Segments)
            {
                var dx = segment.EndX - segment.StartX;
                var dy = segment.EndY - segment.StartY;
                var lengthSquared = (dx * dx) + (dy * dy);
                if (lengthSquared <= bestLengthSquared)
                    continue;

                bestLengthSquared = lengthSquared;
                representative = segment;
            }
        }

        return bestLengthSquared > 0;
    }

    private static (double X, double Y) BuildDirection(DimensionSegmentInfo segment)
    {
        var dx = segment.EndX - segment.StartX;
        var dy = segment.EndY - segment.StartY;
        var length = System.Math.Sqrt((dx * dx) + (dy * dy));
        if (length <= 1e-6)
            return (0, 0);

        return (System.Math.Round(dx / length, 6), System.Math.Round(dy / length, 6));
    }

    private readonly struct DimensionGroupKey
    {
        public DimensionGroupKey(int? viewId, string viewType, string orientation)
        {
            ViewId = viewId;
            ViewType = viewType;
            Orientation = orientation;
        }

        public int? ViewId { get; }
        public string ViewType { get; }
        public string Orientation { get; }
    }
}
