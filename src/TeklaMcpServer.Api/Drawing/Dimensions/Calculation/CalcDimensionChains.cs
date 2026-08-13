using System;
using System.Collections.Generic;
using System.Linq;
using SolidContacts;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// Calculates the initial, deliberately over-complete horizontal and vertical chains of
/// one geometry group. It knows no Tekla runtime, drawing subject, policy, or group merge.
/// </summary>
public static class CalcDimensionChains
{
    /// <summary>
    /// The same arithmetic tolerance that flattened the planar source geometry. It joins
    /// coordinates that differ through projection or Clipper arithmetic; it is not an
    /// angle or a manufacturing gap tolerance.
    /// </summary>
    public const double PositionCoincidenceToleranceMm = RegionFlattener.CoincidenceTolerance;

    public static void Apply(GeometryGroup group)
    {
        if (group == null)
            throw new ArgumentNullException(nameof(group));
        if (group.DimensionChains != null)
        {
            throw new InvalidOperationException(
                $"Geometry group '{group.Id}' already has calculated chains. Create a new group to recalculate it.");
        }

        var chains = NewChains();
        var extentShapes = group.Boundary == null ? group.Shapes : [group.Boundary];
        var extentPoints = Points(extentShapes).ToList();

        if (extentPoints.Count == 0)
        {
            throw new InvalidOperationException(
                $"Geometry group '{group.Id}' has no planar points from which to calculate dimension chains.");
        }

        var extents = GroupExtents.From(extentPoints);
        AddExtremes(chains, extentPoints, extents);

        foreach (var shape in group.Shapes)
            AddShape(chains, shape, extents);

        // The union contour can contain a real step which none of the member shapes
        // names. It is a source of positions too, even when callers keep it separate.
        if (group.Boundary != null && !group.Shapes.Any(shape => ReferenceEquals(shape, group.Boundary)))
            AddShape(chains, group.Boundary, extents);

        foreach (var chain in chains)
            chain.FinalizePositions(PositionCoincidenceToleranceMm);

        group.SetDimensionChains(new DimensionChainSet(chains));
    }

    private static List<DimensionChain> NewChains() =>
    [
        new(DimensionChainSide.Top, new Vec3(1, 0, 0), new Vec3(0, 1, 0)),
        new(DimensionChainSide.Bottom, new Vec3(1, 0, 0), new Vec3(0, -1, 0)),
        new(DimensionChainSide.Left, new Vec3(0, 1, 0), new Vec3(-1, 0, 0)),
        new(DimensionChainSide.Right, new Vec3(0, 1, 0), new Vec3(1, 0, 0))
    ];

    private static void AddExtremes(
        IReadOnlyList<DimensionChain> chains,
        IReadOnlyList<SourcePoint> points,
        GroupExtents extents)
    {
        AddExtent(chains, DimensionChainSide.Top, extents.MinX, points.Where(point => Same(point.Point.X, extents.MinX)));
        AddExtent(chains, DimensionChainSide.Top, extents.MaxX, points.Where(point => Same(point.Point.X, extents.MaxX)));
        AddExtent(chains, DimensionChainSide.Bottom, extents.MinX, points.Where(point => Same(point.Point.X, extents.MinX)));
        AddExtent(chains, DimensionChainSide.Bottom, extents.MaxX, points.Where(point => Same(point.Point.X, extents.MaxX)));
        AddExtent(chains, DimensionChainSide.Left, extents.MinY, points.Where(point => Same(point.Point.Y, extents.MinY)));
        AddExtent(chains, DimensionChainSide.Left, extents.MaxY, points.Where(point => Same(point.Point.Y, extents.MaxY)));
        AddExtent(chains, DimensionChainSide.Right, extents.MinY, points.Where(point => Same(point.Point.Y, extents.MinY)));
        AddExtent(chains, DimensionChainSide.Right, extents.MaxY, points.Where(point => Same(point.Point.Y, extents.MaxY)));
    }

    private static void AddExtent(
        IReadOnlyList<DimensionChain> chains,
        DimensionChainSide side,
        double coordinate,
        IEnumerable<SourcePoint> sources)
    {
        var chain = Chain(chains, side);
        foreach (var source in sources)
            chain.Add(coordinate, Support(source, DimensionChainPositionSupportKind.GroupExtent));
    }

    private static void AddShape(
        IReadOnlyList<DimensionChain> chains,
        GeometryGroupShape shape,
        GroupExtents extents)
    {
        var points = shape.Shape.Points;
        if (points.Count == 0)
        {
            // An Empty shape offers no candidate. Its read/completeness status belongs to
            // the adapter that built the snapshot, not to this pure calculation.
            return;
        }

        if (shape.Shape.Kind == PlanarShapeKind.Point)
        {
            AddCorner(chains, new SourcePoint(shape, points[0], 0), DimensionChainPositionSupportKind.PointShape, extents);
            return;
        }

        var edgeCount = shape.Shape.Kind == PlanarShapeKind.Polygon ? points.Count : points.Count - 1;
        for (var startIndex = 0; startIndex < edgeCount; startIndex++)
        {
            var endIndex = (startIndex + 1) % points.Count;
            var start = new SourcePoint(shape, points[startIndex], startIndex);
            var end = new SourcePoint(shape, points[endIndex], endIndex);
            var deltaX = Math.Abs(end.Point.X - start.Point.X);
            var deltaY = Math.Abs(end.Point.Y - start.Point.Y);

            if (deltaX <= PositionCoincidenceToleranceMm)
            {
                AddX(chains, start, DimensionChainPositionSupportKind.AxisAlignedEdge, extents);
                AddX(chains, end, DimensionChainPositionSupportKind.AxisAlignedEdge, extents);
            }
            else if (deltaY <= PositionCoincidenceToleranceMm)
            {
                AddY(chains, start, DimensionChainPositionSupportKind.AxisAlignedEdge, extents);
                AddY(chains, end, DimensionChainPositionSupportKind.AxisAlignedEdge, extents);
            }
            else if (shape.Shape.Kind == PlanarShapeKind.Segment)
            {
                AddCorner(chains, start, DimensionChainPositionSupportKind.SegmentEndpoint, extents);
                AddCorner(chains, end, DimensionChainPositionSupportKind.SegmentEndpoint, extents);
            }
            else
            {
                AddCorner(chains, start, DimensionChainPositionSupportKind.TiltedEdgeCorner, extents);
                AddCorner(chains, end, DimensionChainPositionSupportKind.TiltedEdgeCorner, extents);
            }
        }
    }

    private static void AddCorner(
        IReadOnlyList<DimensionChain> chains,
        SourcePoint source,
        DimensionChainPositionSupportKind kind,
        GroupExtents extents)
    {
        AddX(chains, source, kind, extents);
        AddY(chains, source, kind, extents);
    }

    private static void AddX(
        IReadOnlyList<DimensionChain> chains,
        SourcePoint source,
        DimensionChainPositionSupportKind kind,
        GroupExtents extents)
    {
        var toTop = Math.Abs(source.Point.Y - extents.MaxY);
        var toBottom = Math.Abs(source.Point.Y - extents.MinY);

        // An exactly central source is deliberately offered to both preliminary chains.
        // Later policy decides which side, if either, should retain it.
        if (toTop <= toBottom)
            Chain(chains, DimensionChainSide.Top).Add(source.Point.X, Support(source, kind));
        if (toBottom <= toTop)
            Chain(chains, DimensionChainSide.Bottom).Add(source.Point.X, Support(source, kind));
    }

    private static void AddY(
        IReadOnlyList<DimensionChain> chains,
        SourcePoint source,
        DimensionChainPositionSupportKind kind,
        GroupExtents extents)
    {
        var toLeft = Math.Abs(source.Point.X - extents.MinX);
        var toRight = Math.Abs(source.Point.X - extents.MaxX);

        // See AddX: equality is retained as an explicit two-sided preliminary proposal.
        if (toLeft <= toRight)
            Chain(chains, DimensionChainSide.Left).Add(source.Point.Y, Support(source, kind));
        if (toRight <= toLeft)
            Chain(chains, DimensionChainSide.Right).Add(source.Point.Y, Support(source, kind));
    }

    private static DimensionChainPositionSupport Support(SourcePoint source, DimensionChainPositionSupportKind kind) =>
        new(source.Shape, kind, source.Point, source.Index);

    private static DimensionChain Chain(IReadOnlyList<DimensionChain> chains, DimensionChainSide side) =>
        chains.Single(chain => chain.Side == side);

    private static IEnumerable<SourcePoint> Points(IEnumerable<GeometryGroupShape> shapes)
    {
        foreach (var shape in shapes)
        {
            for (var index = 0; index < shape.Shape.Points.Count; index++)
                yield return new SourcePoint(shape, shape.Shape.Points[index], index);
        }
    }

    private static bool Same(double first, double second) =>
        Math.Abs(first - second) <= PositionCoincidenceToleranceMm;

    private readonly struct GroupExtents
    {
        private GroupExtents(double minX, double maxX, double minY, double maxY)
        {
            MinX = minX;
            MaxX = maxX;
            MinY = minY;
            MaxY = maxY;
        }

        public double MinX { get; }
        public double MaxX { get; }
        public double MinY { get; }
        public double MaxY { get; }

        public static GroupExtents From(IReadOnlyList<SourcePoint> points) =>
            new(
                points.Min(point => point.Point.X),
                points.Max(point => point.Point.X),
                points.Min(point => point.Point.Y),
                points.Max(point => point.Point.Y));
    }

    private readonly struct SourcePoint(GeometryGroupShape shape, Vec3 point, int index)
    {
        public GeometryGroupShape Shape { get; } = shape;
        public Vec3 Point { get; } = point;
        public int Index { get; } = index;
    }
}
