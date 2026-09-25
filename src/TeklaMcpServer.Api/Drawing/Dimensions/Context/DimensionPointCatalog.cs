using System;
using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

[Flags]
internal enum DimensionPointKind
{
    None = 0,
    GroupExtent = 1,
    AxisAlignedEdge = 2,
    TiltedEdgeCorner = 4,
    SegmentEndpoint = 8,
    PointShape = 16,
    Hole = 32
}

/// <summary>One immutable, context-local point catalog made only from existing chains.</summary>
internal sealed class DimensionPointCatalog
{
    private readonly IReadOnlyDictionary<string, DimensionPoint> _points;
    private readonly IReadOnlyDictionary<DimensionChainSide, DimensionPointLine> _lines;

    private DimensionPointCatalog(Dictionary<string, DimensionPoint> points,
        Dictionary<DimensionChainSide, DimensionPointLine> lines)
    {
        _points = points;
        _lines = lines;
    }

    internal IEnumerable<DimensionPoint> AllPoints => _points.Values;

    internal IReadOnlyList<DimensionPoint> LinePoints(DimensionChainSide side) =>
        _lines[side].Entries.Select(entry => _points[entry.PointId]).ToArray();

    public object Project(IEnumerable<DimensionChainSide> sides) => new {
        sides = sides.Select(side => new {
            side = side.ToString(),
            points = _lines[side].Entries.Select(entry => {
                var point = _points[entry.PointId];
                return new {
                    pointId = point.Id,
                    kinds = KindNames(point.Kinds),
                    parents = point.Parents.Select(parent => new {
                        modelId = parent.ModelId,
                        sourceId = parent.SourceId,
                        pointIndex = parent.PointIndex,
                        kind = parent.Kind.ToString(),
                        isHole = parent.IsHole,
                        partExtentAlongChain = parent.PartExtentAlongChain
                    }).ToArray(),
                    previousDistance = entry.PreviousDistance,
                    nextDistance = entry.NextDistance
                };
            }).ToArray()
        }).ToArray()
    };

    public double[] Resolve(IEnumerable<string> pointIds, DimensionChainSide side)
    {
        var ids = pointIds.ToArray();
        if (ids.Length < 2)
            throw new ArgumentException("pointIds must contain at least two points");
        if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
            throw new ArgumentException("pointIds must not contain duplicates");

        var allowed = new HashSet<string>(_lines[side].Entries.Select(entry => entry.PointId), StringComparer.Ordinal);
        var coordinates = new List<double>(ids.Length * 3);
        foreach (var id in ids)
        {
            if (!_points.TryGetValue(id, out var point))
                throw new ArgumentException($"Unknown pointId '{id}' for this context");
            if (!allowed.Contains(id))
                throw new ArgumentException($"pointId '{id}' is not on the {side} dimension line");
            coordinates.Add(point.X);
            coordinates.Add(point.Y);
            coordinates.Add(0);
        }
        return coordinates.ToArray();
    }

    public static DimensionPointCatalog Build(DimensionChainSet chains)
    {
        var points = new Dictionary<string, DimensionPoint>(StringComparer.Ordinal);
        var byCoordinates = new Dictionary<(double X, double Y), DimensionPoint>();
        var lines = new Dictionary<DimensionChainSide, DimensionPointLine>();
        var nextId = 1;

        foreach (DimensionChain chain in chains.Chains)
        {
            var entries = chain.Positions
                .SelectMany(position => position.Supports)
                .GroupBy(support => (support.Point.X, support.Point.Y))
                .OrderBy(group => chain.Side is DimensionChainSide.Top or DimensionChainSide.Bottom
                    ? group.Key.X : group.Key.Y)
                .ThenBy(group => chain.Side is DimensionChainSide.Top or DimensionChainSide.Bottom
                    ? group.Key.Y : group.Key.X)
                .Select(group => {
                    if (!byCoordinates.TryGetValue(group.Key, out var point))
                    {
                        point = new DimensionPoint($"p{nextId++:D4}", group.Key.X, group.Key.Y);
                        byCoordinates.Add(group.Key, point);
                        points.Add(point.Id, point);
                    }
                    foreach (var support in group) point.Add(support, chain.Side);
                    return point;
                }).ToArray();

            var lineEntries = entries.Select((point, index) => new DimensionPointLineEntry(
                point.Id,
                index == 0 ? null : AxisDistance(chain.Side, entries[index - 1], point),
                index + 1 == entries.Length ? null : AxisDistance(chain.Side, point, entries[index + 1]))).ToArray();
            lines.Add(chain.Side, new DimensionPointLine(lineEntries));
        }

        foreach (var point in points.Values) point.Finish();
        return new DimensionPointCatalog(points, lines);
    }

    private static double AxisDistance(DimensionChainSide side, DimensionPoint a, DimensionPoint b) =>
        Math.Abs(side is DimensionChainSide.Top or DimensionChainSide.Bottom ? b.X - a.X : b.Y - a.Y);

    private static string[] KindNames(DimensionPointKind kinds) =>
        Enum.GetValues(typeof(DimensionPointKind)).Cast<DimensionPointKind>()
            .Where(kind => kind != DimensionPointKind.None && kinds.HasFlag(kind))
            .Select(kind => kind.ToString()).ToArray();
}

internal sealed class DimensionPointLine(IReadOnlyList<DimensionPointLineEntry> entries)
{
    public IReadOnlyList<DimensionPointLineEntry> Entries { get; } = entries;
}

internal sealed class DimensionPointLineEntry(string pointId, double? previousDistance, double? nextDistance)
{
    public string PointId { get; } = pointId;
    public double? PreviousDistance { get; } = previousDistance;
    public double? NextDistance { get; } = nextDistance;
}

internal sealed class DimensionPoint(string id, double x, double y)
{
    private readonly List<DimensionPointParent> _parents = new();
    private readonly HashSet<(int? ModelId, string SourceId, int PointIndex,
        DimensionChainPositionSupportKind Kind, bool IsHole, double? Span)> _parentKeys = new();
    private DimensionPointKind _kinds;

    public string Id { get; } = id;
    public double X { get; } = x;
    public double Y { get; } = y;
    public DimensionPointKind Kinds => _kinds;
    public IReadOnlyList<DimensionPointParent> Parents => _parents;

    public void Add(DimensionChainPositionSupport support, DimensionChainSide side)
    {
        _kinds |= support.Kind switch {
            DimensionChainPositionSupportKind.GroupExtent => DimensionPointKind.GroupExtent,
            DimensionChainPositionSupportKind.AxisAlignedEdge => DimensionPointKind.AxisAlignedEdge,
            DimensionChainPositionSupportKind.TiltedEdgeCorner => DimensionPointKind.TiltedEdgeCorner,
            DimensionChainPositionSupportKind.SegmentEndpoint => DimensionPointKind.SegmentEndpoint,
            DimensionChainPositionSupportKind.PointShape => DimensionPointKind.PointShape,
            _ => DimensionPointKind.None
        };
        if (support.Source.IsHole) _kinds |= DimensionPointKind.Hole;

        var extent = support.AxisAlignedModelExtent;
        double? span = extent == null ? null : side is DimensionChainSide.Top or DimensionChainSide.Bottom
            ? extent.MaxX - extent.MinX : extent.MaxY - extent.MinY;
        var key = (support.ModelId, support.Source.Id, support.PointIndex, support.Kind, support.Source.IsHole, span);
        if (_parentKeys.Add(key))
            _parents.Add(new DimensionPointParent(support.ModelId, support.Source.Id,
                support.PointIndex, support.Kind, support.Source.IsHole, span));
    }

    public void Finish() => _parents.Sort((a, b) => {
        var model = Nullable.Compare(a.ModelId, b.ModelId);
        if (model != 0) return model;
        var source = StringComparer.Ordinal.Compare(a.SourceId, b.SourceId);
        return source != 0 ? source : a.PointIndex.CompareTo(b.PointIndex);
    });
}

internal sealed class DimensionPointParent(int? modelId, string sourceId, int pointIndex,
    DimensionChainPositionSupportKind kind, bool isHole, double? partExtentAlongChain)
{
    public int? ModelId { get; } = modelId;
    public string SourceId { get; } = sourceId;
    public int PointIndex { get; } = pointIndex;
    public DimensionChainPositionSupportKind Kind { get; } = kind;
    public bool IsHole { get; } = isHole;
    public double? PartExtentAlongChain { get; } = partExtentAlongChain;
}
