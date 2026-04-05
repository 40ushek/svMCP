using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing;

internal enum DimensionAiAssistedAction
{
    Combine = 0,
    Arrange,
    ReviewOnly,
    Keep
}

internal sealed class DimensionAiOrchestrationToolArguments
{
    public int? ViewId { get; set; }
    public List<int> DimensionIds { get; } = [];
    public double? TargetGap { get; set; }
    public bool? PreviewOnly { get; set; }
}

internal sealed class DimensionAiOrchestrationEvidence
{
    public string LayoutPolicyStatus { get; set; } = string.Empty;
    public string LayoutRecommendedAction { get; set; } = string.Empty;
    public string LayoutCombineClassification { get; set; } = string.Empty;
    public string ReductionStatus { get; set; } = string.Empty;
    public string ReductionReason { get; set; } = string.Empty;
    public string CombineConnectivityMode { get; set; } = string.Empty;
    public int? PreferredDimensionId { get; set; }
    public int? RepresentativeDimensionId { get; set; }
    public DrawingVectorInfo? LineDirection { get; set; }
    public DrawingVectorInfo? NormalDirection { get; set; }
    public double? StartAlong { get; set; }
    public double? EndAlong { get; set; }
    public DimensionGeometryBand? GeometryBand { get; set; }
    public int SegmentGeometryCount { get; set; }
    public bool HasTextBounds { get; set; }
    public bool HasPartsBounds { get; set; }
    public string PartsBoundsSide { get; set; } = string.Empty;
    public bool IsOutsidePartsBounds { get; set; }
    public bool IntersectsPartsBounds { get; set; }
    public double? OffsetFromPartsBounds { get; set; }
    public double? ReferenceLineLength { get; set; }
    public double Distance { get; set; }
    public int TopDirection { get; set; }
    public double ViewScale { get; set; }
}

internal sealed class DimensionAiOrchestrationPlanStep
{
    public int StepOrder { get; set; }
    public DimensionAiAssistedAction Action { get; set; }
    public List<int> DimensionIds { get; } = [];
    public int PrimaryDimensionId { get; set; }
    public List<int> RelatedDimensionIds { get; } = [];
    public int? ViewId { get; set; }
    public string DimensionType { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public DimensionAiOrchestrationEvidence Evidence { get; set; } = new();
    public string ToolName { get; set; } = string.Empty;
    public DimensionAiOrchestrationToolArguments? ToolArguments { get; set; }
    public DimensionAiOrchestrationToolArguments? ApplyToolArguments { get; set; }
    public bool PreviewOnly { get; set; }
}

internal sealed class DimensionAiOrchestrationPlanResult
{
    public int? ViewId { get; set; }
    public List<DimensionAiOrchestrationPlanStep> Steps { get; } = [];
    public List<string> Warnings { get; } = [];
}
