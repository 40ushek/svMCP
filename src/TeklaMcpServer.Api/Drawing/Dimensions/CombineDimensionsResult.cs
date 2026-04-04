using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing;

public sealed class CombineDimensionCandidateResult
{
    public int? ViewId { get; set; }
    public string ViewType { get; set; } = string.Empty;
    public string DimensionType { get; set; } = string.Empty;
    public int PacketIndex { get; set; }
    public int BaseDimensionId { get; set; }
    public string ConnectivityMode { get; set; } = string.Empty;
    public bool PreviewOnly { get; set; }
    public bool Combined { get; set; }
    public int? CreatedDimensionId { get; set; }
    public bool RollbackAttempted { get; set; }
    public bool RollbackSucceeded { get; set; }
    public string RollbackReason { get; set; } = string.Empty;
    public double Distance { get; set; }
    public string Reason { get; set; } = string.Empty;
    public List<int> DimensionIds { get; } = [];
    public List<int> DeletedDimensionIds { get; } = [];
    public List<string> BlockingReasons { get; } = [];
    public List<DrawingPointInfo> PointList { get; } = [];
}

public sealed class CombineDimensionsResult
{
    public bool PreviewOnly { get; set; }
    public int CandidateCount { get; set; }
    public int CombinedCount { get; set; }
    public int SkippedCount { get; set; }
    public List<CombineDimensionCandidateResult> Combined { get; } = [];
    public List<CombineDimensionCandidateResult> Skipped { get; } = [];
}
