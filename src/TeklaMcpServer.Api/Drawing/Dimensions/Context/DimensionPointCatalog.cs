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

    // A witness line must not run along a part outline: on the same coordinate along the chain,
    // the point farthest toward the dimension line is used, whichever part it belongs to.
    private DimensionPoint Outermost(DimensionPoint point, DimensionChainSide side)
    {
        var alongX = side is DimensionChainSide.Top or DimensionChainSide.Bottom;
        var outer = side is DimensionChainSide.Top or DimensionChainSide.Right ? 1 : -1;
        double Along(DimensionPoint p) => alongX ? p.X : p.Y;
        double Across(DimensionPoint p) => alongX ? p.Y : p.X;
        return LinePoints(side)
            .Where(p => Math.Abs(Along(p) - Along(point)) <= 0.01)
            // Prefer the requested coordinate before choosing an outward witness support.
            .OrderBy(p => Math.Abs(Along(p) - Along(point)))
            .ThenByDescending(p => outer * Across(p))
            .ThenBy(p => p.Id == point.Id ? 0 : 1).ThenBy(p => p.Id, StringComparer.Ordinal)
            .FirstOrDefault() ?? point;
    }

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
            point = Outermost(point, side);
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

    // Keep the original panel IDs while adding detached reference supports. Exact coincident
    // points merge their geometric parents; candidate kinds are kept separately by the rule.
    internal DimensionPointCatalog WithReference(DimensionPointCatalog reference, GeometryGroupExtent panel,
        IReadOnlyCollection<(double X, double Y)> vertices)
    {
        var points = _points.Values.ToDictionary(point => point.Id, point => point.Copy(point.Id), StringComparer.Ordinal);
        var byCoordinates = points.Values.ToDictionary(point => (point.X, point.Y));
        var mapped = new Dictionary<string, DimensionPoint>(StringComparer.Ordinal);
        var referencePoints = reference.AllPoints.ToDictionary(point => (point.X, point.Y));
        var nextVertex = 1;
        foreach (var vertex in vertices.Distinct().OrderBy(point => point.X).ThenBy(point => point.Y))
        {
            // A chain built over several reference components can omit an inner component's
            // extreme. The selected polygon vertex itself is still a real captured support.
            var source = referencePoints.TryGetValue(vertex, out var captured) ? captured
                : new DimensionPoint($"v{nextVertex++:D4}", vertex.X, vertex.Y);
            if (!byCoordinates.TryGetValue((source.X, source.Y), out var point))
            {
                point = source.Copy("r" + source.Id);
                points.Add(point.Id, point);
                byCoordinates.Add((point.X, point.Y), point);
            }
            else point.Merge(source);
            mapped.Add(source.Id, point);
        }
        var lines = new Dictionary<DimensionChainSide, DimensionPointLine>();
        foreach (var pair in _lines)
        {
            var side = pair.Key;
            var entries = pair.Value.Entries.Select(entry => points[entry.PointId])
                .Concat(mapped.Values.Where(point => {
                    var alongX = side is DimensionChainSide.Top or DimensionChainSide.Bottom;
                    var outer = side is DimensionChainSide.Top or DimensionChainSide.Right ? 1 : -1;
                    var middle = alongX ? (panel.MinY + panel.MaxY) / 2 : (panel.MinX + panel.MaxX) / 2;
                    return outer * ((alongX ? point.Y : point.X) - middle) >= -.5;
                }))
                .Distinct().OrderBy(point => side is DimensionChainSide.Top or DimensionChainSide.Bottom ? point.X : point.Y)
                .ThenBy(point => side is DimensionChainSide.Top or DimensionChainSide.Bottom ? point.Y : point.X).ToArray();
            lines.Add(side, new DimensionPointLine(entries.Select((point, index) => new DimensionPointLineEntry(
                point.Id, index == 0 ? null : AxisDistance(side, entries[index - 1], point),
                index + 1 == entries.Length ? null : AxisDistance(side, point, entries[index + 1]))).ToArray()));
        }
        foreach (var point in points.Values) point.Finish();
        return new DimensionPointCatalog(points, lines);
    }

    // Global overall bounds may be supported only on the opposite contour side. Retain
    // those real points on each requested axis so their IDs resolve during creation.
    internal DimensionPointCatalog WithExtentSupports(GeometryGroupExtent extent)
    {
        var lines = new Dictionary<DimensionChainSide, DimensionPointLine>();
        foreach (var side in _lines.Keys)
        {
            var alongX = side is DimensionChainSide.Top or DimensionChainSide.Bottom;
            double Along(DimensionPoint point) => alongX ? point.X : point.Y;
            var min = alongX ? extent.MinX : extent.MinY;
            var max = alongX ? extent.MaxX : extent.MaxY;
            var entries = LinePoints(side).Concat(AllPoints.Where(point =>
                    Math.Abs(Along(point) - min) <= .5 || Math.Abs(Along(point) - max) <= .5))
                .Distinct().OrderBy(Along).ThenBy(point => alongX ? point.Y : point.X).ToArray();
            lines.Add(side, new DimensionPointLine(entries.Select((point, index) => new DimensionPointLineEntry(
                point.Id, index == 0 ? null : AxisDistance(side, entries[index - 1], point),
                index + 1 == entries.Length ? null : AxisDistance(side, point, entries[index + 1]))).ToArray()));
        }
        return new DimensionPointCatalog(_points.ToDictionary(pair => pair.Key, pair => pair.Value), lines);
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

    internal DimensionPoint Copy(string id)
    {
        var copy = new DimensionPoint(id, X, Y);
        copy.Merge(this);
        return copy;
    }

    internal void Merge(DimensionPoint source)
    {
        _kinds |= source._kinds;
        foreach (var parent in source.Parents)
        {
            var key = (parent.ModelId, parent.SourceId, parent.PointIndex, parent.Kind, parent.IsHole, parent.PartExtentAlongChain);
            if (_parentKeys.Add(key)) _parents.Add(parent);
        }
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
