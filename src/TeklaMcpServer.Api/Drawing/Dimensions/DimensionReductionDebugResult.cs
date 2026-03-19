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
    public string CombineConnectivityMode { get; set; } = string.Empty;
    public List<string> BlockingReasons { get; } = [];
    public DimensionCombinePreviewDebugInfo? CombinePreview { get; set; }
}

internal sealed class DimensionCombinePreviewDebugInfo
{
    public int BaseDimensionId { get; set; }
    public List<int> DimensionIds { get; } = [];
    public DrawingPointInfo StartPoint { get; set; } = new();
    public DrawingPointInfo EndPoint { get; set; } = new();
    public List<DrawingPointInfo> PointList { get; } = [];
    public List<double> LengthList { get; } = [];
    public double Distance { get; set; }
}
