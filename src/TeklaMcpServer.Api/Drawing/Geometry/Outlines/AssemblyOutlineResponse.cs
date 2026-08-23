using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// The exact JSON shape the bridge returns for get_assembly_outline. Pulled out of the
/// bridge's handler so a test can build one from a <see cref="ViewAssemblyOutlineResult"/>
/// and assert on the real serialized values, instead of scanning the handler's source text.
/// </summary>
public sealed class AssemblyOutlineResponse
{
    [JsonPropertyName("success")] public bool Success { get; set; }
    [JsonPropertyName("viewId")] public int ViewId { get; set; }
    [JsonPropertyName("isComplete")] public bool IsComplete { get; set; }
    [JsonPropertyName("restricted")] public bool Restricted { get; set; }
    [JsonPropertyName("selectionComplete")] public bool SelectionComplete { get; set; }
    [JsonPropertyName("visibleCount")] public int VisibleCount { get; set; }
    [JsonPropertyName("requestedIds")] public IReadOnlyList<int> RequestedIds { get; set; } = [];
    [JsonPropertyName("notVisibleRequestedIds")] public IReadOnlyList<int> NotVisibleRequestedIds { get; set; } = [];
    [JsonPropertyName("outsideDepthModelIds")] public IReadOnlyList<int> OutsideDepthModelIds { get; set; } = [];
    [JsonPropertyName("unresolvedDepthModelIds")] public IReadOnlyList<int> UnresolvedDepthModelIds { get; set; } = [];
    [JsonPropertyName("error")] public string? Error { get; set; }
    [JsonPropertyName("assemblyOutline")] public IReadOnlyList<OutlineTreeNodeResult> AssemblyOutline { get; set; } = [];
    [JsonPropertyName("partOutlines")] public IReadOnlyList<AssemblyOutlinePartEntry> PartOutlines { get; set; } = [];
    [JsonPropertyName("unread")] public IReadOnlyList<AssemblyOutlineUnreadEntry> Unread { get; set; } = [];

    public static AssemblyOutlineResponse From(ViewAssemblyOutlineResult result) => new()
    {
        Success = result.Error == null,
        ViewId = result.ViewId,
        IsComplete = result.IsComplete,
        Restricted = result.Restricted,
        SelectionComplete = result.SelectionComplete,
        VisibleCount = result.VisibleCount,
        RequestedIds = result.RequestedIds,
        NotVisibleRequestedIds = result.NotVisibleRequestedIds,
        OutsideDepthModelIds = result.OutsideDepthModelIds,
        UnresolvedDepthModelIds = result.UnresolvedDepthModelIds,
        Error = result.Error,
        AssemblyOutline = result.AssemblyNodes,
        PartOutlines = result.PartNodes
            .Select(part => new AssemblyOutlinePartEntry { ModelId = part.Key, Outline = part.Value })
            .ToList(),
        Unread = result.Unread
            .Select(part => new AssemblyOutlineUnreadEntry { ModelId = part.ModelId, Reason = part.Reason })
            .ToList()
    };
}

public sealed class AssemblyOutlinePartEntry
{
    [JsonPropertyName("modelId")] public int ModelId { get; set; }
    [JsonPropertyName("outline")] public IReadOnlyList<OutlineTreeNodeResult> Outline { get; set; } = [];
}

public sealed class AssemblyOutlineUnreadEntry
{
    [JsonPropertyName("modelId")] public int ModelId { get; set; }
    [JsonPropertyName("reason")] public string Reason { get; set; } = "";
}
