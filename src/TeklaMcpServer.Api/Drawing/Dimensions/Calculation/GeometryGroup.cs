using System;
using System.Collections.Generic;
using System.Linq;
using SolidContacts;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// An immutable snapshot of one semantic group of drawing geometry, plus the working
/// chains calculated from it.
///
/// The snapshot is deliberately independent of whether its caller is an assembly or a
/// single-part drawing. Policies and skills may change <see cref="DimensionChains"/>,
/// but never the boundary or shapes they are explaining.
/// </summary>
public sealed class GeometryGroup
{
    public GeometryGroup(
        string id,
        IReadOnlyList<GeometryGroupShape>? boundaryShapes,
        IReadOnlyList<GeometryGroupShape>? shapes = null,
        GeometryGroupCompleteness? completeness = null)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("A geometry group needs an id.", nameof(id));

        Id = id;
        BoundaryShapes = CopyShapes(boundaryShapes, nameof(boundaryShapes));
        Shapes = CopyShapes(shapes, nameof(shapes));
        Completeness = completeness ?? GeometryGroupCompleteness.Complete;
        Extent = GeometryGroupExtent.TryCreate(BoundaryShapes.Count > 0 ? BoundaryShapes : Shapes);
    }

    public string Id { get; }

    /// <summary>
    /// Every contour ring belonging to the group's real boundary, including holes and
    /// disconnected components. Together they supply the group's overall X/Y extremes and
    /// later skew-direction evidence.
    /// </summary>
    public IReadOnlyList<GeometryGroupShape> BoundaryShapes { get; }

    /// <summary>Unchanged projected geometry belonging to this semantic group.</summary>
    public IReadOnlyList<GeometryGroupShape> Shapes { get; }

    /// <summary>
    /// Whether every source required to form this snapshot was read. Its issues are neutral
    /// source ids and reasons, so this object stays valid for both assembly and single-part
    /// adapters without importing Tekla identifiers.
    /// </summary>
    public GeometryGroupCompleteness Completeness { get; }

    /// <summary>
    /// Overall X/Y extent across all boundary components, including the space between
    /// disconnected components. Null when the selected extent source contains no point.
    /// </summary>
    public GeometryGroupExtent? Extent { get; }

    /// <summary>
    /// Working chain state. Null until <see cref="CalcDimensionChains.Apply"/> has run.
    /// </summary>
    public DimensionChainSet? DimensionChains { get; private set; }

    internal void SetDimensionChains(DimensionChainSet chains) =>
        DimensionChains = chains ?? throw new ArgumentNullException(nameof(chains));

    private static IReadOnlyList<GeometryGroupShape> CopyShapes(
        IReadOnlyList<GeometryGroupShape>? shapes,
        string parameterName)
    {
        if (shapes == null)
            return Array.Empty<GeometryGroupShape>();

        var copy = shapes.ToArray();
        if (copy.Any(shape => shape == null))
            throw new ArgumentException("A geometry group cannot contain a null shape.", parameterName);

        return copy;
    }
}

/// <summary>One source that prevented a geometry group's snapshot from being complete.</summary>
public sealed class GeometryGroupSourceIssue
{
    public GeometryGroupSourceIssue(string id, string reason)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("A source issue needs an id.", nameof(id));
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A source issue needs a reason.", nameof(reason));

        Id = id;
        Reason = reason;
    }

    public string Id { get; }
    public string Reason { get; }
}

/// <summary>Completeness information carried with, rather than beside, a geometry snapshot.</summary>
public sealed class GeometryGroupCompleteness
{
    public static GeometryGroupCompleteness Complete { get; } = new(Array.Empty<GeometryGroupSourceIssue>());

    public GeometryGroupCompleteness(IReadOnlyList<GeometryGroupSourceIssue>? issues)
    {
        Issues = issues?.ToArray() ?? Array.Empty<GeometryGroupSourceIssue>();
        if (Issues.Any(issue => issue == null))
            throw new ArgumentException("Completeness cannot contain a null issue.", nameof(issues));
    }

    public IReadOnlyList<GeometryGroupSourceIssue> Issues { get; }
    public bool IsComplete => Issues.Count == 0;
}

/// <summary>The factual extent of a group across all of its selected components.</summary>
public sealed class GeometryGroupExtent
{
    private GeometryGroupExtent(double minX, double maxX, double minY, double maxY)
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

    internal static GeometryGroupExtent? TryCreate(IReadOnlyList<GeometryGroupShape> shapes)
    {
        var points = shapes.SelectMany(shape => shape.Shape.Points).ToList();
        return points.Count == 0
            ? null
            : new GeometryGroupExtent(
                points.Min(point => point.X), points.Max(point => point.X),
                points.Min(point => point.Y), points.Max(point => point.Y));
    }
}

/// <summary>
/// One shape of a <see cref="GeometryGroup"/>, with group-level meaning retained beside
/// the shared planar geometry type.
///
/// <see cref="IsHole"/> stays here rather than on <see cref="PlanarShape"/>: a hole is a
/// fact about a contour ring, not about every contact segment or point in SolidContacts.
/// </summary>
public sealed class GeometryGroupShape
{
    public GeometryGroupShape(string id, PlanarShape shape, bool isHole = false, int? modelId = null)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("A group shape needs an id.", nameof(id));

        Id = id;
        Shape = shape ?? throw new ArgumentNullException(nameof(shape));
        IsHole = isHole;
        ModelId = modelId;
    }

    /// <summary>Caller-owned stable identity, for example a part, contact, or bolt id.</summary>
    public string Id { get; }

    public PlanarShape Shape { get; }

    /// <summary>True only when this shape came from a contour hole ring.</summary>
    public bool IsHole { get; }

    /// <summary>
    /// Model part that owns this shape, when the caller has one. Assembly-boundary and
    /// derived geometry deliberately have no owner; a missing value is not an invitation
    /// to recover one by nearest-coordinate matching.
    /// </summary>
    public int? ModelId { get; }
}
