using System;
using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>Detached point in view coordinates with snapshot-local identity and source evidence.</summary>
internal sealed class DimensionRulePoint
{
    public string Id { get; }
    public double X { get; }
    public double Y { get; }
    public IReadOnlyList<DimensionPointSource> Sources { get; }

    public DimensionRulePoint(string id, double x, double y, IEnumerable<DimensionPointSource> sources)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A point ID is required.", nameof(id));
        if (double.IsNaN(x) || double.IsInfinity(x) || double.IsNaN(y) || double.IsInfinity(y))
            throw new ArgumentException("Point coordinates must be finite.");
        Id = id;
        X = x;
        Y = y;
        Sources = Array.AsReadOnly(sources.ToArray());
    }
}

/// <summary>
/// Provider-defined object kind and geometric reference. New kinds such as bolt, bolt-group,
/// rebar or rebar-group do not require changing the executor. ModelId is optional for geometry
/// anchors. The remaining fields preserve the evidence of the current point catalog.
/// </summary>
internal sealed class DimensionPointSource
{
    public string ObjectKind { get; }
    public int? ModelId { get; }
    public string GeometryId { get; }
    public int? PointIndex { get; }
    public string? FeatureKind { get; }
    public bool IsHole { get; }
    public double? ExtentAlongChain { get; }

    public DimensionPointSource(string objectKind, int? modelId, string geometryId,
        int? pointIndex = null, string? featureKind = null, bool isHole = false, double? extentAlongChain = null)
    {
        if (string.IsNullOrWhiteSpace(objectKind)) throw new ArgumentException("An object kind is required.", nameof(objectKind));
        ObjectKind = objectKind;
        ModelId = modelId;
        GeometryId = geometryId;
        PointIndex = pointIndex;
        FeatureKind = featureKind;
        IsHole = isHole;
        ExtentAlongChain = extentAlongChain;
    }
}
