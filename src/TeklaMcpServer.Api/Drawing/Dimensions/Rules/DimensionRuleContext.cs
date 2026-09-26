using System;
using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>Existing snapshot geometry only; rules never read or write Tekla objects.</summary>
internal sealed class DimensionRuleContext
{
    private readonly IReadOnlyDictionary<string, DimensionRulePoint> _byId;
    private readonly IReadOnlyDictionary<Type, object> _data;
    public IReadOnlyList<DimensionRulePoint> Points { get; }
    public AxisAlignedDimensionGeometry? AxisAlignedGeometry { get; }

    public DimensionRuleContext(IEnumerable<DimensionRulePoint> points,
        AxisAlignedDimensionGeometry? axisAlignedGeometry = null, params object[] data)
    {
        Points = Array.AsReadOnly(points.ToArray());
        _byId = Points.ToDictionary(point => point.Id, StringComparer.Ordinal);
        AxisAlignedGeometry = axisAlignedGeometry;
        _data = data.ToDictionary(item => item.GetType(), item => item);
    }

    public DimensionRulePoint GetPoint(string id) => _byId[id];
    public T Require<T>() where T : class => _data.TryGetValue(typeof(T), out var value)
        ? (T)value
        : throw new InvalidOperationException($"Dimension rule data '{typeof(T).Name}' is unavailable.");

    // This adapter translates today's contour catalog. Future providers can supply
    // their own points and source kinds without constructing four side chains.
    public static DimensionRuleContext FromCatalog(DimensionPointCatalog catalog, GeometryGroupExtent extent,
        params object[] data) =>
        new(catalog.AllPoints.Select(point => new DimensionRulePoint(point.Id, point.X, point.Y,
            point.Parents.Select(parent => new DimensionPointSource(
                parent.ModelId.HasValue ? "part" : "geometry", parent.ModelId, parent.SourceId,
                parent.PointIndex, parent.Kind.ToString(), parent.IsHole, parent.PartExtentAlongChain)))),
            new AxisAlignedDimensionGeometry(catalog, extent), data);
}
