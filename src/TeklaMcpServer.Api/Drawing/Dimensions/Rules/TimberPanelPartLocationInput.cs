using System;
using System.Collections.Generic;
using SolidContacts;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>Captured panel data consumed by the part-location rule.</summary>
internal sealed class TimberPanelPartLocationInput
{
    public GeometryGroup Group { get; }
    public GeometryGroup? ReferenceGroup { get; }
    public DimensionPointCatalog? PanelCatalog { get; }
    public IReadOnlyCollection<int> IncludedIds { get; }
    public Func<ViewContactCandidatePointsResult?> GetContacts { get; }

    public TimberPanelPartLocationInput(GeometryGroup group, IReadOnlyCollection<int> includedIds,
        Func<ViewContactCandidatePointsResult?> getContacts, GeometryGroup? referenceGroup = null,
        DimensionPointCatalog? panelCatalog = null)
    {
        ReferenceGroup = referenceGroup;
        PanelCatalog = panelCatalog;
        Group = group ?? throw new ArgumentNullException(nameof(group));
        IncludedIds = includedIds ?? throw new ArgumentNullException(nameof(includedIds));
        GetContacts = getContacts ?? throw new ArgumentNullException(nameof(getContacts));
    }
}
