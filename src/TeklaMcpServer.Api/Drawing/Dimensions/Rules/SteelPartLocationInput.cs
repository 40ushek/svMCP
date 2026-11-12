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

    public SteelPartLocationInput(DimensionPointCatalog catalog, IReadOnlyCollection<int> mainPartIds,
        IReadOnlyCollection<int>? inclinedPartIds = null, IReadOnlyCollection<int>? faceOnPartIds = null)
    {
        FaceOnPartIds = faceOnPartIds ?? Array.Empty<int>();
        InclinedPartIds = inclinedPartIds ?? Array.Empty<int>();
        Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        MainPartIds = mainPartIds ?? throw new ArgumentNullException(nameof(mainPartIds));
    }
}
