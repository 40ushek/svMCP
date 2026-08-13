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
        GeometryGroupShape? boundary,
        IReadOnlyList<GeometryGroupShape>? shapes = null)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("A geometry group needs an id.", nameof(id));

        Id = id;
        Boundary = boundary;
        Shapes = shapes?.ToArray() ?? Array.Empty<GeometryGroupShape>();
    }

    public string Id { get; }

    /// <summary>
    /// The real outer contour of this group, where one exists. It is the source of the
    /// group's overall X/Y extremes and of later skew-direction evidence.
    /// </summary>
    public GeometryGroupShape? Boundary { get; }

    /// <summary>Unchanged projected geometry belonging to this semantic group.</summary>
    public IReadOnlyList<GeometryGroupShape> Shapes { get; }

    /// <summary>
    /// Working chain state. Null until <see cref="CalcDimensionChains.Apply"/> has run.
    /// </summary>
    public DimensionChainSet? DimensionChains { get; private set; }

    internal void SetDimensionChains(DimensionChainSet chains) =>
        DimensionChains = chains ?? throw new ArgumentNullException(nameof(chains));
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
    public GeometryGroupShape(string id, PlanarShape shape, bool isHole = false)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("A group shape needs an id.", nameof(id));

        Id = id;
        Shape = shape ?? throw new ArgumentNullException(nameof(shape));
        IsHole = isHole;
    }

    /// <summary>Caller-owned stable identity, for example a part, contact, or bolt id.</summary>
    public string Id { get; }

    public PlanarShape Shape { get; }

    /// <summary>True only when this shape came from a contour hole ring.</summary>
    public bool IsHole { get; }
}
