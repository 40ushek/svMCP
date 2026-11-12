using System;
using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>Current side catalog and main-part identity for a steel location proposal.</summary>
internal sealed class SteelPartLocationInput
{
    public DimensionPointCatalog Catalog { get; }
    public IReadOnlyCollection<int> MainPartIds { get; }

    /// <summary>Parts located by a chain along themselves; an axis-aligned chain across the main part leaves them out.</summary>
    public IReadOnlyCollection<int> InclinedPartIds { get; }

    /// <summary>
    /// Parts whose plane is parallel to the view plane: a plate seen face-on, with its whole
    /// contour drawn. Along the main part such a part is located by both of its edges.
    /// </summary>
    public IReadOnlyCollection<int> FaceOnPartIds { get; }

    /// <summary>
    /// The thickness of each part: its smallest size along its own axes. A part whose size
    /// along the chain is more than its thickness lies along the main part (a cover plate, a
    /// gusset seen edge-on) and is located by both ends; a rib standing across shows only its
    /// thickness there and keeps one face.
    /// </summary>
    public IReadOnlyDictionary<int, double> PartThickness { get; }

    public SteelPartLocationInput(DimensionPointCatalog catalog, IReadOnlyCollection<int> mainPartIds,
        IReadOnlyCollection<int>? inclinedPartIds = null, IReadOnlyCollection<int>? faceOnPartIds = null,
        IReadOnlyDictionary<int, double>? partThickness = null)
    {
        PartThickness = partThickness ?? new Dictionary<int, double>();
        FaceOnPartIds = faceOnPartIds ?? Array.Empty<int>();
        InclinedPartIds = inclinedPartIds ?? Array.Empty<int>();
        Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        MainPartIds = mainPartIds ?? throw new ArgumentNullException(nameof(mainPartIds));
    }
}
