using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing;

public sealed class DimensionArrangementDebugGroupInfo
{
    public int? ViewId { get; set; }
    public string ViewType { get; set; } = string.Empty;
    public string DimensionType { get; set; } = string.Empty;
    public string Orientation { get; set; } = string.Empty;
    public double? DirectionX { get; set; }
    public double? DirectionY { get; set; }
    public int TopDirection { get; set; }
    public DrawingLineInfo? ReferenceLine { get; set; }
    public int MemberCount { get; set; }
    public double MaximumDistance { get; set; }
    public DrawingBoundsInfo? Bounds { get; set; }
    public List<string> GroupingBasis { get; } = [];
    public List<DimensionArrangementDebugMemberInfo> Members { get; } = [];
}

public sealed class DimensionArrangementDebugDedupItemInfo
{
    public int DimensionId { get; set; }
    public string SourceKind { get; set; } = string.Empty;
    public string GeometryKind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public int? RepresentativeDimensionId { get; set; }
}

public sealed class DimensionArrangementDebugDedupGroupInfo
{
    public int? ViewId { get; set; }
    public string ViewType { get; set; } = string.Empty;
    public string DimensionType { get; set; } = string.Empty;
    public int RawMemberCount { get; set; }
    public int ReducedMemberCount { get; set; }
    public int RejectedCount { get; set; }
    public List<DimensionArrangementDebugDedupItemInfo> Items { get; } = [];
}

public sealed class DimensionArrangementDebugMemberInfo
{
    public int DimensionId { get; set; }
    public double Distance { get; set; }
    public double SortKey { get; set; }
    public double DirectionX { get; set; }
    public double DirectionY { get; set; }
    public int TopDirection { get; set; }
    public DrawingBoundsInfo? Bounds { get; set; }
    public DrawingLineInfo? ReferenceLine { get; set; }
    public DrawingLineInfo? LeadLineMain { get; set; }
    public DrawingLineInfo? LeadLineSecond { get; set; }
}

public sealed class DimensionArrangementDebugSpacingPair
{
    public int FirstDimensionId { get; set; }
    public int SecondDimensionId { get; set; }
    public double Distance { get; set; }
    public bool IsOverlap { get; set; }
}

public sealed class DimensionArrangementDebugSpacingInfo
{
    public int? ViewId { get; set; }
    public string ViewType { get; set; } = string.Empty;
    public string DimensionType { get; set; } = string.Empty;
    public string Orientation { get; set; } = string.Empty;
    public double? DirectionX { get; set; }
    public double? DirectionY { get; set; }
    public int TopDirection { get; set; }
    public DrawingLineInfo? ReferenceLine { get; set; }
    public bool HasOverlaps { get; set; }
    public double? MinimumDistance { get; set; }
    public List<DimensionArrangementDebugSpacingPair> Pairs { get; } = [];
}

public sealed class DimensionArrangementDebugStackMemberInfo
{
    public int DimensionId { get; set; }
    public string DimensionType { get; set; } = string.Empty;
    public string Orientation { get; set; } = string.Empty;
    public double Distance { get; set; }
    public DrawingLineInfo? ReferenceLine { get; set; }
    public DrawingLineInfo? PlanningReferenceLine { get; set; }
    public int AlignmentClusterId { get; set; }
    public int? AlignmentAnchorDimensionId { get; set; }
    public string AlignmentStatus { get; set; } = string.Empty;
    public string AlignmentReason { get; set; } = string.Empty;
}

public sealed class DimensionArrangementDebugAlignmentClusterInfo
{
    public int ClusterId { get; set; }
    public int AnchorDimensionId { get; set; }
    public DrawingLineInfo? AnchorReferenceLine { get; set; }
    public bool Applied { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public List<int> DimensionIds { get; } = [];
}

public sealed class DimensionArrangementDebugStackInfo
{
    public int? ViewId { get; set; }
    public string ViewType { get; set; } = string.Empty;
    public string DimensionType { get; set; } = string.Empty;
    public string Orientation { get; set; } = string.Empty;
    public double? DirectionX { get; set; }
    public double? DirectionY { get; set; }
    public int TopDirection { get; set; }
    public DrawingLineInfo? ReferenceLine { get; set; }
    public bool AlignmentApplied { get; set; }
    public List<string> GroupingBasis { get; } = [];
    public List<DimensionArrangementDebugStackMemberInfo> Members { get; } = [];
    public List<DimensionArrangementDebugAlignmentClusterInfo> AlignmentClusters { get; } = [];
}

public sealed class DimensionArrangementDebugProposal
{
    public int DimensionId { get; set; }
    public double AxisShift { get; set; }
    public double DistanceDelta { get; set; }
    public bool CanApply { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class DimensionArrangementDebugPlanInfo
{
    public int? ViewId { get; set; }
    public string ViewType { get; set; } = string.Empty;
    public string DimensionType { get; set; } = string.Empty;
    public string Orientation { get; set; } = string.Empty;
    public double? DirectionX { get; set; }
    public double? DirectionY { get; set; }
    public int TopDirection { get; set; }
    public DrawingLineInfo? ReferenceLine { get; set; }
    public double TargetGapPaper { get; set; }
    public double TargetGapDrawing { get; set; }
    public int ProposalCount { get; set; }
    public bool HasApplicableChanges { get; set; }
    public List<DimensionArrangementDebugProposal> Proposals { get; } = [];
}

public sealed class DimensionArrangementDebugResult
{
    public int RawViewFilteredTotal { get; set; }
    public int ViewFilteredTotal { get; set; }
    public int RawGroupCount { get; set; }
    public int GroupCount { get; set; }
    public int DedupRejectedCount { get; set; }
    public double TargetGapPaper { get; set; }
    public List<DimensionArrangementDebugDedupGroupInfo> Dedup { get; } = [];
    public List<DimensionArrangementDebugGroupInfo> Groups { get; } = [];
    public List<DimensionArrangementDebugStackInfo> Stacks { get; } = [];
    public List<DimensionArrangementDebugSpacingInfo> Spacing { get; } = [];
    public List<DimensionArrangementDebugPlanInfo> Plans { get; } = [];
}
