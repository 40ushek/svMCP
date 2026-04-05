using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing;

internal enum DimensionOrchestrationAction
{
    Combine = 0,
    Suppress,
    Keep,
    Review
}

internal sealed class DimensionOrchestrationEvidence
{
    public string LayoutPolicyStatus { get; set; } = string.Empty;
    public string LayoutRecommendedAction { get; set; } = string.Empty;
    public string LayoutCombineClassification { get; set; } = string.Empty;
    public string ReductionStatus { get; set; } = string.Empty;
    public string ReductionReason { get; set; } = string.Empty;
    public string CombineConnectivityMode { get; set; } = string.Empty;
    public int? PreferredDimensionId { get; set; }
    public int? RepresentativeDimensionId { get; set; }
    public bool HasPartsBounds { get; set; }
    public string PartsBoundsSide { get; set; } = string.Empty;
    public bool IsOutsidePartsBounds { get; set; }
    public bool IntersectsPartsBounds { get; set; }
    public double? OffsetFromPartsBounds { get; set; }
    public double? ReferenceLineLength { get; set; }
    public double Distance { get; set; }
    public int TopDirection { get; set; }
    public double ViewScale { get; set; }
    public bool CanEvaluatePartsBoundsGap { get; set; }
    public double CurrentPartsBoundsGapDrawing { get; set; }
    public double TargetPartsBoundsGapPaper { get; set; }
    public double TargetPartsBoundsGapDrawing { get; set; }
    public bool RequiresPartsBoundsGapCorrection { get; set; }
    public double SuggestedOutwardDeltaFromPartsBounds { get; set; }
}

internal sealed class DimensionOrchestrationActionPacket
{
    public DimensionOrchestrationAction Action { get; set; }
    public List<int> DimensionIds { get; } = [];
    public int PrimaryDimensionId { get; set; }
    public List<int> RelatedDimensionIds { get; } = [];
    public int? ViewId { get; set; }
    public string DimensionType { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public DimensionOrchestrationEvidence Evidence { get; set; } = new();
}

internal sealed class DimensionOrchestrationDebugResult
{
    public int? ViewId { get; set; }
    public List<DimensionOrchestrationActionPacket> Packets { get; } = [];
    public List<string> Warnings { get; } = [];
}
