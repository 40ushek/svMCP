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
    public List<DimensionRepresentativePacketDebugInfo> Packets { get; } = [];
}

internal sealed class DimensionReductionItemDebugInfo
{
    public DimensionItem Item { get; set; } = new();
    public string Status { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public int? PacketIndex { get; set; }
    public int? RepresentativeDimensionId { get; set; }
}

internal sealed class DimensionRepresentativePacketDebugInfo
{
    public int PacketIndex { get; set; }
    public int StartDimensionId { get; set; }
    public int EndDimensionId { get; set; }
    public List<int> DimensionIds { get; } = [];
    public int ItemCount { get; set; }
    public string SelectionMode { get; set; } = string.Empty;
    public int RepresentativeDimensionId { get; set; }
    public double? SplitGapFromPreviousEndToCurrentStart { get; set; }
    public double? SplitGapFromPreviousStartToCurrentEnd { get; set; }
    public double SplitThreshold { get; set; }
    public bool IsCombineCandidate { get; set; }
    public List<string> BlockingReasons { get; } = [];
}
