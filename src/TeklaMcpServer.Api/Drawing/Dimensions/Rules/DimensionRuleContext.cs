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
    public IReadOnlyList<DimensionRuleResult> PriorResults { get; }

    public DimensionRuleContext(IEnumerable<DimensionRulePoint> points,
        AxisAlignedDimensionGeometry? axisAlignedGeometry = null, params object[] data)
    {
        Points = Array.AsReadOnly(points.ToArray());
        _byId = Points.ToDictionary(point => point.Id, StringComparer.Ordinal);
        AxisAlignedGeometry = axisAlignedGeometry;
        _data = data.ToDictionary(item => item.GetType(), item => item);
        PriorResults = Array.Empty<DimensionRuleResult>();
    }

    private DimensionRuleContext(IReadOnlyList<DimensionRulePoint> points,
        IReadOnlyDictionary<string, DimensionRulePoint> byId,
        AxisAlignedDimensionGeometry? axisAlignedGeometry,
        IReadOnlyDictionary<Type, object> data,
        IReadOnlyList<DimensionRuleResult> priorResults)
    {
        Points = points;
        _byId = byId;
        AxisAlignedGeometry = axisAlignedGeometry;
        _data = data;
        PriorResults = priorResults;
    }

    public DimensionRulePoint GetPoint(string id) => _byId[id];
    public T Require<T>() where T : class => _data.TryGetValue(typeof(T), out var value)
        ? (T)value
        : throw new InvalidOperationException($"Dimension rule data '{typeof(T).Name}' is unavailable.");

    public DimensionRuleContext WithPriorResults(IEnumerable<DimensionRuleResult> results) =>
        new(Points, _byId, AxisAlignedGeometry, _data, Array.AsReadOnly(results.ToArray()));

    // This adapter translates today's contour catalog. Future providers can supply
    // their own points and source kinds without constructing four side chains.
    public static DimensionRuleContext FromCatalog(DimensionPointCatalog catalog, params object[] data) =>
        new(ToRulePoints(catalog), null, data);

    public static DimensionRuleContext FromCatalog(DimensionPointCatalog catalog, GeometryGroupExtent extent,
        params object[] data) =>
        new(ToRulePoints(catalog), new AxisAlignedDimensionGeometry(catalog, extent), data);

    private static IEnumerable<DimensionRulePoint> ToRulePoints(DimensionPointCatalog catalog) =>
        catalog.AllPoints.Select(point => new DimensionRulePoint(point.Id, point.X, point.Y,
            point.Parents.Select(parent => new DimensionPointSource(
                parent.ModelId.HasValue ? "part" : "geometry", parent.ModelId, parent.SourceId,
                parent.PointIndex, parent.Kind.ToString(), parent.IsHole, parent.PartExtentAlongChain))));
}
