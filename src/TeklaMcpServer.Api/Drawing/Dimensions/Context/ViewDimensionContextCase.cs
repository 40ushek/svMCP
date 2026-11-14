using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Clipper2Lib;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>One outline of a case: which stored solids it was built from and what its read reported.</summary>
internal sealed class ViewDimensionContextCaseOutline
{
    /// <summary>In the order the solids were read; the union is built in this order.</summary>
    public List<int> SolidIds { get; set; } = new();

    public List<ViewDimensionContextCaseUnread> Unread { get; set; } = new();

    public string? Error { get; set; }

    public bool Restricted { get; set; }

    public int VisibleCount { get; set; }

    public List<int> RequestedIds { get; set; } = new();

    public List<int> NotVisibleRequestedIds { get; set; } = new();

    public List<int> OutsideDepthModelIds { get; set; } = new();

    public List<int> UnresolvedDepthModelIds { get; set; } = new();

    /// <summary>The view depth the outlines were limited to; both null when it was not applied.</summary>
    public double? DepthMin { get; set; }

    public double? DepthMax { get; set; }
}

internal sealed class ViewDimensionContextCaseUnread
{
    public int ModelId { get; set; }

    public string Reason { get; set; } = string.Empty;
}

internal sealed class ViewDimensionContextCaseRule
{
    public string Id { get; set; } = string.Empty;

    public PartExclusionKind Kind { get; set; }

    public string Value { get; set; } = string.Empty;
}

internal sealed class ViewDimensionContextCasePart
{
    public int ModelId { get; set; }

    public string? PartPos { get; set; }

    public string? PartPrefix { get; set; }

    public string? Profile { get; set; }

    public string? Material { get; set; }

    public string? Name { get; set; }

    public string? Zone { get; set; }

    public bool ZoneKnown { get; set; }

    public bool PartPrefixKnown { get; set; }

    public bool MaterialKnown { get; set; }

    public bool IsMainPart { get; set; }

    public bool IsMainPartKnown { get; set; }

    public PartRole Role { get; set; }

    public bool IsClassified { get; set; }

    public string RuleId { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;
}

internal sealed class ViewDimensionContextCaseReference
{
    public List<string> Zones { get; set; } = new();

    public List<int> ModelIds { get; set; } = new();

    public ViewDimensionContextCaseOutline? Outline { get; set; }

    public string? Error { get; set; }
}

/// <summary>
/// Everything one view dimension context is built from, with no Tekla objects, so the
/// chain rules can be run again later without Tekla. Outlines are not stored: they are
/// rebuilt from the stored part solids by the same projection code.
///
/// Not stored: solids that the contact search reads later for parts outside the
/// dimension selection. A replayed context reports those parts as unread contacts.
/// </summary>
internal sealed class ViewDimensionContextCase
{
    public int ViewId { get; set; }

    public double Scale { get; set; }

    public JsonElement Source { get; set; }

    public JsonElement Metadata { get; set; }

    public JsonElement? Bolts { get; set; }

    /// <summary>The bolt-chain settings the context was built with; null in cases saved before them.</summary>
    public BoltChainSettingsData? BoltSettings { get; set; }

    public List<ViewDimensionContextCaseRule> Exclusions { get; set; } = new();

    public List<ViewDimensionContextCasePart> Included { get; set; } = new();

    public List<ViewDimensionContextCasePart> Excluded { get; set; } = new();

    public List<ViewDimensionContextCasePart> Unclassified { get; set; } = new();

    public List<ViewDimensionContextCaseUnread> UnreadRoles { get; set; } = new();

    public List<int> OutsideDepthModelIds { get; set; } = new();

    public List<int> ContactModelIds { get; set; } = new();

    public List<ViewDimensionContextCaseUnread> ContactSelectionUnread { get; set; } = new();

    /// <summary>Part solids in view coordinates, shared by the outline and the reference outline.</summary>
    public List<PartSolidGeometryInViewResult> Solids { get; set; } = new();

    public ViewDimensionContextCaseOutline Outline { get; set; } = new();

    public ViewDimensionContextCaseReference? ReferenceZone { get; set; }

    /// <summary>Captures the arguments of the <see cref="ViewDimensionContext"/> constructor.</summary>
    public static ViewDimensionContextCase From(int viewId, double scale, StructuralOutline outline,
        IReadOnlyList<PartExclusionRule> exclusions, object source, object metadata,
        ViewBoltGeometrySnapshot? bolts = null, BoltChainSettings? boltSettings = null)
    {
        var reference = outline.ReferenceZone;
        return new ViewDimensionContextCase
        {
            ViewId = viewId,
            Scale = scale,
            Source = ViewDimensionContext.Freeze(source),
            Metadata = ViewDimensionContext.Freeze(metadata),
            Bolts = bolts?.Answer,
            BoltSettings = boltSettings == null ? null : BoltChainSettingsData.From(boltSettings),
            Exclusions = exclusions.Select(rule => new ViewDimensionContextCaseRule {
                Id = rule.Id, Kind = rule.Kind, Value = rule.Value }).ToList(),
            Included = outline.Included.Select(Part).ToList(),
            Excluded = outline.Excluded.Select(Part).ToList(),
            Unclassified = outline.Unclassified.Select(Part).ToList(),
            UnreadRoles = outline.UnreadRoles.Select(Unread).ToList(),
            OutsideDepthModelIds = outline.OutsideDepthModelIds.ToList(),
            ContactModelIds = outline.ContactModelIds.ToList(),
            ContactSelectionUnread = outline.ContactSelectionUnread.Select(Unread).ToList(),
            Solids = outline.Outline.PartSolidGeometries
                .Concat(reference?.Outline?.PartSolidGeometries ?? new Dictionary<int, PartSolidGeometryInViewResult>())
                .GroupBy(pair => pair.Key).Select(group => group.First().Value).ToList(),
            Outline = OutlineOf(outline.Outline),
            ReferenceZone = reference == null ? null : new ViewDimensionContextCaseReference {
                Zones = reference.Zones.ToList(), ModelIds = reference.ModelIds.ToList(),
                Outline = reference.Outline == null ? null : OutlineOf(reference.Outline), Error = reference.Error }
        };
    }

    /// <summary>Rebuilds the context; its contacts see only the stored solids.</summary>
    public ViewDimensionContext ToContext()
    {
        var solids = Solids.GroupBy(solid => solid.ModelId).ToDictionary(group => group.Key, group => group.First());
        var reference = ReferenceZone == null ? null : new ReferenceZoneOutline(ReferenceZone.Zones, ReferenceZone.ModelIds,
            ReferenceZone.Outline == null ? null : Rebuild(ReferenceZone.Outline, solids), ReferenceZone.Error);
        var structural = new StructuralOutline(Rebuild(Outline, solids),
            Included.Select(Part).ToArray(), Excluded.Select(Part).ToArray(), Unclassified.Select(Part).ToArray(),
            UnreadRoles.Select(Unread).ToArray(), OutsideDepthModelIds.ToArray(), ContactModelIds.ToArray(),
            ContactSelectionUnread.Select(Unread).ToArray(), reference);
        var context = new ViewDimensionContext(ViewId, Scale, structural,
            Exclusions.Select(rule => new PartExclusionRule(rule.Id, rule.Kind, rule.Value)).ToArray(),
            Source, Metadata, Bolts is { } bolts ? ViewBoltGeometrySnapshot.Restore(bolts) : null,
            BoltSettings?.ToSettings("Saved case"));
        context.AttachContacts(new ViewContactsSnapshot(ViewId, context.ContactModelIds, context.ContactSelectionUnread,
            modelId => new PartSolidGeometryInViewResult { ViewId = ViewId, ModelId = modelId,
                Error = "solid was not captured in this case" }));
        return context;
    }

    private ViewAssemblyOutlineResult Rebuild(ViewDimensionContextCaseOutline outline,
        IReadOnlyDictionary<int, PartSolidGeometryInViewResult> solids)
    {
        var unread = outline.Unread.Select(Unread).ToList();
        if (outline.Error != null)
            return new ViewAssemblyOutlineResult(ViewId, new PolyTreeD(), new Dictionary<int, PolyTreeD>(), unread,
                outline.Error, outline.Restricted, outline.VisibleCount, outline.RequestedIds, outline.NotVisibleRequestedIds,
                outline.OutsideDepthModelIds, outline.UnresolvedDepthModelIds);

        ViewAssemblyOutlineResult Build(IReadOnlyList<UnreadPart> selectionUnread) =>
            TeklaDrawingAssemblyOutlineApi.Build(ViewId, outline.SolidIds, new StoredSolids(solids), null,
                outline.Restricted, outline.VisibleCount, outline.RequestedIds, outline.NotVisibleRequestedIds,
                outline.OutsideDepthModelIds, outline.UnresolvedDepthModelIds, selectionUnread,
                MetadataDepthRange() ?? (outline.DepthMin is { } min && outline.DepthMax is { } max ? (min, max, null) : null));

        // The projection reports again whatever it could not use. The rest of the stored
        // list came from the selection and has to be handed back in.
        foreach (var repeated in Build(Array.Empty<UnreadPart>()).Unread)
        {
            var index = unread.FindIndex(part => part.ModelId == repeated.ModelId && part.Reason == repeated.Reason);
            if (index >= 0) unread.RemoveAt(index);
        }
        return Build(unread);
    }

    // A case saved before the depth was recorded: the same range the reader would take, from
    // the view metadata - the depth window, moved to the view origin along the depth.
    private (double Min, double Max, double? Cut)? MetadataDepthRange()
    {
        if (Metadata.ValueKind != JsonValueKind.Object
            || !Metadata.TryGetProperty("depthWindow", out var window) || window.ValueKind != JsonValueKind.Object
            || !Metadata.TryGetProperty("viewCoordinates", out var view)
            || !Metadata.TryGetProperty("displayCoordinates", out var display)) return null;
        return window.TryGetProperty("MinZ", out var min) && window.TryGetProperty("MaxZ", out var max)
            ? ViewDepthWindow.DepthRangeInView(min.GetDouble(), max.GetDouble(),
                view.EnumerateArray().Select(number => number.GetDouble()).ToArray(),
                display.EnumerateArray().Select(number => number.GetDouble()).ToArray())
            : null;
    }

    private static ViewDimensionContextCaseOutline OutlineOf(ViewAssemblyOutlineResult outline) => new()
    {
        SolidIds = outline.PartSolidGeometries.Keys.ToList(),
        Unread = outline.Unread.Select(Unread).ToList(),
        Error = outline.Error,
        Restricted = outline.Restricted,
        VisibleCount = outline.VisibleCount,
        RequestedIds = outline.RequestedIds.ToList(),
        NotVisibleRequestedIds = outline.NotVisibleRequestedIds.ToList(),
        OutsideDepthModelIds = outline.OutsideDepthModelIds.ToList(),
        UnresolvedDepthModelIds = outline.UnresolvedDepthModelIds.ToList(),
        DepthMin = outline.DepthRange?.Min, DepthMax = outline.DepthRange?.Max
    };

    private static ViewDimensionContextCasePart Part(PartRoleInView part) => new()
    {
        ModelId = part.ModelId, PartPos = part.PartPos, PartPrefix = part.PartPrefix, Profile = part.Profile,
        Material = part.Material, Name = part.Name, Zone = part.Zone, ZoneKnown = part.ZoneKnown,
        PartPrefixKnown = part.PartPrefixKnown, MaterialKnown = part.MaterialKnown,
        IsMainPart = part.IsMainPart, IsMainPartKnown = part.IsMainPartKnown,
        Role = part.Role.Role, IsClassified = part.Role.IsClassified, RuleId = part.Role.RuleId, Reason = part.Role.Reason
    };

    private static PartRoleInView Part(ViewDimensionContextCasePart part) => new(
        part.ModelId, part.PartPos, part.PartPrefix,
        part.IsClassified ? new PartRoleResult(part.Role, part.RuleId, part.Reason) : PartRoleResult.Unclassified,
        part.IsMainPart, part.IsMainPartKnown, part.Profile, part.Material, part.Name, part.Zone, part.ZoneKnown,
        part.PartPrefixKnown, part.MaterialKnown);

    private static ViewDimensionContextCaseUnread Unread(UnreadPart part) => new() { ModelId = part.ModelId, Reason = part.Reason };

    private static UnreadPart Unread(ViewDimensionContextCaseUnread part) => new(part.ModelId, part.Reason);

    private sealed class StoredSolids(IReadOnlyDictionary<int, PartSolidGeometryInViewResult> solids) : IDrawingPartSolidGeometryApi
    {
        public PartSolidGeometryInViewResult GetPartSolidGeometryInView(int viewId, int modelId) =>
            solids.TryGetValue(modelId, out var solid) ? solid
                : throw new InvalidOperationException("solid was not captured in this case");
    }
}

/// <summary>Stores view dimension context cases as one JSON file per drawing, view and part selection.</summary>
internal static class ViewDimensionContextCaseStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Where every context read from Tekla drops its case, next to the logs.</summary>
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "svMCP", "DimensionContextCases");

    /// <summary>Writes the case, replacing an earlier one of the same drawing, view and selection. Returns the file path.</summary>
    public static string Save(string directory, ViewDimensionContextCase contextCase)
    {
        if (contextCase == null)
            throw new ArgumentNullException(nameof(contextCase));

        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, GetFileName(contextCase));
        File.WriteAllText(path, JsonSerializer.Serialize(contextCase, JsonOptions));
        return path;
    }

    public static ViewDimensionContextCase Load(string path)
        => JsonSerializer.Deserialize<ViewDimensionContextCase>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidOperationException($"Dimension context case '{path}' could not be read.");

    // Different exclusions or reference zones give a different context of the same view,
    // so they get their own file.
    internal static string GetFileName(ViewDimensionContextCase contextCase)
    {
        var guid = contextCase.Source.ValueKind == JsonValueKind.Object
            && contextCase.Source.TryGetProperty("drawingGuid", out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty : string.Empty;
        var invalid = Path.GetInvalidFileNameChars();
        var name = new string(guid.Trim().Where(c => !invalid.Contains(c)).ToArray());
        var selection = string.Join("|", contextCase.Exclusions.Select(rule => rule.Id))
            + "#" + string.Join("|", contextCase.ReferenceZone?.Zones ?? new List<string>())
            + "#" + string.Join("|", contextCase.ReferenceZone?.ModelIds ?? new List<int>());
        using var hash = SHA256.Create();
        var suffix = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(selection)), 0, 4)
            .Replace("-", string.Empty).ToLowerInvariant();
        return $"{(name.Length == 0 ? "unknown" : name)}_{contextCase.ViewId}_{suffix}.json";
    }
}
