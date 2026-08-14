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

    /// <summary>
    /// Comparison tolerance for a policy check that recognizes a span as a whole, proven
    /// axis-aligned part. This is deliberately not the coordinate-coincidence tolerance:
    /// it answers whether a printed span represents the same manufactured length.
    /// </summary>
    public const double PartSpanMatchToleranceMm = 0.5;

    /// <summary>
    /// Geometric tolerance for deciding whether a part contour is sufficiently axis-aligned
    /// to expose a safe whole-part span. This is separate from coordinate coincidence: it
    /// tolerates projection/Clipper noise, but must never turn a genuinely short diagonal
    /// into a rectangular part.
    /// </summary>
    public const double PartExtentAxisAlignmentOffsetToleranceMm = 0.01;

    /// <summary>
    /// Maximum deviation from a view axis for whole-part span evidence. Together with the
    /// 0.01 mm offset cap this matters only below about 5.7 mm edge length; on ordinary
    /// members the absolute cap is the limiting condition.
    /// </summary>
    public const double PartExtentAxisAlignmentAngleToleranceDegrees = 0.1;

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
        var extentShapes = group.BoundaryShapes.Count == 0 ? group.Shapes : group.BoundaryShapes;
        var modelExtents = AxisAlignedModelExtents(group.Shapes);
        var extentPoints = Points(extentShapes, modelExtents).ToList();

        if (group.Extent == null)
        {
            throw new InvalidOperationException(
                $"Geometry group '{group.Id}' has no planar points from which to calculate dimension chains.");
        }

        var extents = group.Extent;
        AddExtremes(chains, extentPoints, extents);

        foreach (var shape in group.Shapes)
            AddShape(chains, shape, extents, modelExtents);

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
        GeometryGroupExtent extents)
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
        GeometryGroupExtent extents,
        IReadOnlyDictionary<int, GeometryGroupExtent> modelExtents)
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
            AddCorner(chains, CreateSourcePoint(shape, points[0], 0, modelExtents), DimensionChainPositionSupportKind.PointShape, extents);
            return;
        }

        var edgeCount = shape.Shape.Kind == PlanarShapeKind.Polygon ? points.Count : points.Count - 1;
        for (var startIndex = 0; startIndex < edgeCount; startIndex++)
        {
            var endIndex = (startIndex + 1) % points.Count;
            var start = CreateSourcePoint(shape, points[startIndex], startIndex, modelExtents);
            var end = CreateSourcePoint(shape, points[endIndex], endIndex, modelExtents);
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
        GeometryGroupExtent extents)
    {
        AddX(chains, source, kind, extents);
        AddY(chains, source, kind, extents);
    }

    private static void AddX(
        IReadOnlyList<DimensionChain> chains,
        SourcePoint source,
        DimensionChainPositionSupportKind kind,
        GeometryGroupExtent extents)
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
        GeometryGroupExtent extents)
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
        new(source.Shape, kind, source.Point, source.Index, source.ModelExtent);

    private static DimensionChain Chain(IReadOnlyList<DimensionChain> chains, DimensionChainSide side) =>
        chains.Single(chain => chain.Side == side);

    private static IEnumerable<SourcePoint> Points(
        IEnumerable<GeometryGroupShape> shapes,
        IReadOnlyDictionary<int, GeometryGroupExtent> modelExtents)
    {
        foreach (var shape in shapes)
        {
            for (var index = 0; index < shape.Shape.Points.Count; index++)
                yield return CreateSourcePoint(shape, shape.Shape.Points[index], index, modelExtents);
        }
    }

    /// <summary>
    /// Returns an extent only for a part whose projected rings contain no tilted edge.
    /// A box around a raked part invents a span through empty space, so callers must receive
    /// no automatic part-size evidence for it. Empty/point-only sources are ignored rather
    /// than allowing a missing extent to fail the whole group calculation.
    /// </summary>
    private static IReadOnlyDictionary<int, GeometryGroupExtent> AxisAlignedModelExtents(
        IEnumerable<GeometryGroupShape> shapes)
    {
        var results = new Dictionary<int, GeometryGroupExtent>();
        foreach (var part in shapes
                     .Where(shape => shape.ModelId.HasValue)
                     .GroupBy(shape => shape.ModelId!.Value))
        {
            var partShapes = part.ToList();
            if (!HasOnlyAxisAlignedEdges(partShapes))
                continue;

            var extent = GeometryGroupExtent.TryCreate(partShapes);
            if (extent != null)
                results.Add(part.Key, extent);
        }

        return results;
    }

    private static bool HasOnlyAxisAlignedEdges(IEnumerable<GeometryGroupShape> shapes)
    {
        foreach (var shape in shapes)
        {
            var points = shape.Shape.Points;
            var edgeCount = shape.Shape.Kind switch
            {
                PlanarShapeKind.Polygon => points.Count,
                PlanarShapeKind.Segment => Math.Max(points.Count - 1, 0),
                _ => 0
            };

            for (var startIndex = 0; startIndex < edgeCount; startIndex++)
            {
                var endIndex = (startIndex + 1) % points.Count;
                var deltaX = Math.Abs(points[endIndex].X - points[startIndex].X);
                var deltaY = Math.Abs(points[endIndex].Y - points[startIndex].Y);
                if (!IsAxisAlignedForPartExtent(deltaX, deltaY))
                    return false;
            }
        }

        return true;
    }

    private static bool IsAxisAlignedForPartExtent(double deltaX, double deltaY)
    {
        var length = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        if (length <= PositionCoincidenceToleranceMm)
            return true;

        var offset = Math.Min(deltaX, deltaY);
        var maximumOffsetForAngle = length * Math.Sin(
            PartExtentAxisAlignmentAngleToleranceDegrees * Math.PI / 180.0);
        return offset <= PartExtentAxisAlignmentOffsetToleranceMm && offset <= maximumOffsetForAngle;
    }

    private static SourcePoint CreateSourcePoint(
        GeometryGroupShape shape,
        Vec3 point,
        int index,
        IReadOnlyDictionary<int, GeometryGroupExtent> modelExtents) =>
        new(shape, point, index,
            shape.ModelId is int modelId && modelExtents.TryGetValue(modelId, out var extent) ? extent : null);

    private static bool Same(double first, double second) =>
        Math.Abs(first - second) <= PositionCoincidenceToleranceMm;

    private readonly struct SourcePoint(GeometryGroupShape shape, Vec3 point, int index, GeometryGroupExtent? modelExtent)
    {
        public GeometryGroupShape Shape { get; } = shape;
        public Vec3 Point { get; } = point;
        public int Index { get; } = index;
        public GeometryGroupExtent? ModelExtent { get; } = modelExtent;
    }
}
