using System;
using System.Collections.Generic;
using System.Linq;
using SolidContacts;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>Optional reference geometry captured alongside the panel in one solid read.</summary>
public sealed class ReferenceZoneOutline
{
    public IReadOnlyCollection<string> Zones { get; }
    public IReadOnlyCollection<int> ModelIds { get; }
    public ViewAssemblyOutlineResult? Outline { get; }
    public string? Error { get; }
    public bool IsComplete => Error == null && Outline is { IsComplete: true, SelectionComplete: true }
        && ModelIds.Count > 0 && ModelIds.All(Outline.PartOutlines.ContainsKey);

    internal ReferenceZoneOutline(IReadOnlyCollection<string> zones, IReadOnlyCollection<int> modelIds,
        ViewAssemblyOutlineResult? outline = null, string? error = null)
    {
        Zones = Array.AsReadOnly(zones.ToArray());
        ModelIds = Array.AsReadOnly(modelIds.ToArray());
        Outline = outline;
        Error = error;
    }

    internal static string[] Normalize(IReadOnlyCollection<string>? zones)
    {
        if (zones == null) return Array.Empty<string>();
        if (zones.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("referenceZones must contain non-empty zone values", nameof(zones));
        return zones.Select(zone => zone.Trim()).Distinct(StringComparer.Ordinal)
            .OrderBy(zone => zone, StringComparer.Ordinal).ToArray();
    }

    internal static ReferenceZoneOutline Select(IEnumerable<PartRoleInView> attributes,
        IReadOnlyCollection<string> zones, IReadOnlyList<PartExclusionRule> exclusions)
    {
        var wanted = Normalize(zones);
        var eligible = attributes.GroupBy(part => part.ModelId).Select(group => group.First())
            .Where(part => !exclusions.Any(rule => rule.Matches(part.PartPrefix, part.Material))).ToArray();
        // An unread zone cannot safely be ruled out of the caller's selection.
        var unknown = eligible.Where(part => !part.ZoneKnown).Select(part => part.ModelId).ToArray();
        if (unknown.Length > 0)
            return new(wanted, Array.Empty<int>(), error: "ZONE could not be read for model IDs: " + string.Join(",", unknown));
        var selected = eligible.Where(part => part.Zone != null && wanted.Contains(part.Zone, StringComparer.Ordinal)).ToArray();
        var unclassified = selected.Where(part => !part.Role.IsClassified).Select(part => part.ModelId).ToArray();
        if (unclassified.Length > 0)
            return new(wanted, Array.Empty<int>(), error: "Reference exclusions could not be verified for model IDs: " + string.Join(",", unclassified));
        var ids = selected.Select(part => part.ModelId).Distinct().OrderBy(id => id).ToArray();
        return new(wanted, ids, error: ids.Length == 0 ? "No parts survived the reference-zone selection" : null);
    }

    /// <summary>Rebuilds a subset union from detached per-part trees; no Tekla call.</summary>
    internal static ViewAssemblyOutlineResult Subset(ViewAssemblyOutlineResult captured,
        IReadOnlyCollection<int> ids, OutlineOptions? options = null)
    {
        var parts = captured.PartOutlines.Where(pair => ids.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value);
        return new(captured.ViewId, ProjectedOutlineBuilder.BuildAssembly(parts.Values, options), parts,
            captured.Unread.Where(part => ids.Contains(part.ModelId)).ToArray(), captured.Error,
            restricted: true, visibleCount: captured.VisibleCount, requestedIds: ids.ToArray(),
            notVisibleRequestedIds: captured.NotVisibleRequestedIds.Where(ids.Contains).ToArray(),
            outsideDepthModelIds: captured.OutsideDepthModelIds.Where(ids.Contains).ToArray(),
            unresolvedDepthModelIds: captured.UnresolvedDepthModelIds.Where(ids.Contains).ToArray(),
            partSolidGeometries: captured.PartSolidGeometries.Where(pair => ids.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value));
    }
}
