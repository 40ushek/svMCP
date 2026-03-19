using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class DimensionReductionDebugResult
{
    public List<DimensionGroup> ReducedGroups { get; } = [];
    public List<DimensionGroupReductionDebugInfo> Groups { get; } = [];
}

internal sealed class DimensionGroupReductionDebugInfo
{
    public DimensionGroup RawGroup { get; set; } = new();
    public DimensionGroup ReducedGroup { get; set; } = new();
    public List<DimensionReductionItemDebugInfo> Items { get; } = [];
}

internal sealed class DimensionReductionItemDebugInfo
{
    public DimensionItem Item { get; set; } = new();
    public string Status { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public int? PacketIndex { get; set; }
    public int? RepresentativeDimensionId { get; set; }
}
