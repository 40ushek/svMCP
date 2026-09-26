using System;
using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>Current side catalog and main-part identity for a steel location proposal.</summary>
internal sealed class SteelPartLocationInput
{
    public DimensionPointCatalog Catalog { get; }
    public IReadOnlyCollection<int> MainPartIds { get; }

    public SteelPartLocationInput(DimensionPointCatalog catalog, IReadOnlyCollection<int> mainPartIds)
    {
        Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        MainPartIds = mainPartIds ?? throw new ArgumentNullException(nameof(mainPartIds));
    }
}
