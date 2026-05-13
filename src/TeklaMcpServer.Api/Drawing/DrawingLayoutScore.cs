using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class DrawingLayoutScore
{
    public double TotalScore { get; set; }

    public DrawingLayoutScoreBreakdown Breakdown { get; set; } = new();

    public List<string> Diagnostics { get; set; } = new();
}

internal sealed class DrawingLayoutScoreBreakdown
{
    public int ScoredViewCount { get; set; }

    public int NonDetailViewCount { get; set; }

    public int BBoxRectCount { get; set; }

    public int FallbackRectCount { get; set; }

    public double SheetArea { get; set; }

    public double ReservedAreaUnion { get; set; }

    public double AvailableSheetArea { get; set; }

    public double TotalViewArea { get; set; }

    public double FillRatioRaw { get; set; }

    public double FillRatioScore { get; set; }

    public double UniformScaleScore { get; set; }

    public int ViewOverlapCount { get; set; }

    public double ViewOverlapArea { get; set; }

    public double ViewOverlapPenalty { get; set; }

    public int ReservedOverlapCount { get; set; }

    public double ReservedOverlapArea { get; set; }

    public double ReservedOverlapPenalty { get; set; }

    public double EdgeMarginPenalty { get; set; }

    public double PreferredSidePenalty { get; set; }

    public double CompactnessPenalty { get; set; }

    public double StackOrderPenalty { get; set; }

    public double ProjectedAxisPenalty { get; set; }

    public double FillRatioWeight { get; set; }

    public double UniformScaleWeight { get; set; }

    public double ViewOverlapPenaltyWeight { get; set; }

    public double ReservedOverlapPenaltyWeight { get; set; }

    public double EdgeMarginPenaltyWeight { get; set; }

    public double PreferredSidePenaltyWeight { get; set; }

    public double CompactnessPenaltyWeight { get; set; }

    public double StackOrderPenaltyWeight { get; set; }

    public double ProjectedAxisPenaltyWeight { get; set; }
}

internal sealed class DrawingLayoutScoreWeights
{
    public double FillRatioWeight { get; set; } = 1.0;

    public double UniformScaleWeight { get; set; } = 1.0;

    public double ViewOverlapPenaltyWeight { get; set; } = 1.0;

    public double ReservedOverlapPenaltyWeight { get; set; } = 1.0;

    public double EdgeMarginPenaltyWeight { get; set; } = 1.0;

    public double PreferredSidePenaltyWeight { get; set; } = 0.5;

    public double CompactnessPenaltyWeight { get; set; } = 0.25;

    public double StackOrderPenaltyWeight { get; set; } = 0.15;

    public double ProjectedAxisPenaltyWeight { get; set; } = 0.35;
}
