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

    public SteelPartLocationInput(DimensionPointCatalog catalog, IReadOnlyCollection<int> mainPartIds,
        IReadOnlyCollection<int>? inclinedPartIds = null)
    {
        InclinedPartIds = inclinedPartIds ?? Array.Empty<int>();
        Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        MainPartIds = mainPartIds ?? throw new ArgumentNullException(nameof(mainPartIds));
    }
}
