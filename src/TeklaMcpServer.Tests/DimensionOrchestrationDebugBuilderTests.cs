using System.Collections.Generic;
using System.Linq;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DimensionOrchestrationDebugBuilderTests
{
    [Fact]
    public void Build_EmitsCombinePacketForInformationPreservingMerge()
    {
        var debug = new DimensionReductionDebugResult();
        var group = CreateGroup(10, DimensionType.Horizontal);
        group.Items.Add(CreateItem(1001, "kept", "kept", DimensionLayoutPolicyStatus.Preferred, "covers_poorer_chain", DimensionRecommendedAction.PreferCombine, DimensionCombineClassification.InformationPreservingMerge));
        group.Items.Add(CreateItem(1002, "kept", "kept", DimensionLayoutPolicyStatus.LessPreferred, "subchain_of_richer_dimension", DimensionRecommendedAction.PreferCombine, DimensionCombineClassification.InformationPreservingMerge));
        group.CombineCandidates.Add(CreateCombineCandidate([1001, 1002], "shared_point_neighbor_set", 1001));
        debug.Groups.Add(group);

        var packet = Assert.Single(DimensionOrchestrationDebugBuilder.Build(debug, 10).Packets);

        Assert.Equal(DimensionOrchestrationAction.Combine, packet.Action);
        Assert.Equal("information_preserving_merge", packet.Reason);
        Assert.Equal("fused", packet.Source);
        Assert.Equal(new[] { 1001, 1002 }, packet.DimensionIds.ToArray());
        Assert.Equal(1001, packet.PrimaryDimensionId);
        Assert.Equal("shared_point_neighbor_set", packet.Evidence.CombineConnectivityMode);
    }

    [Fact]
    public void Build_DoesNotEmitCombineForDuplicateChain()
    {
        var debug = new DimensionReductionDebugResult();
        var group = CreateGroup(10, DimensionType.Horizontal);
        group.Items.Add(CreateItem(1001, "kept", "kept", DimensionLayoutPolicyStatus.Preferred, "keeps_compact_equivalent_geometry", DimensionRecommendedAction.Keep, DimensionCombineClassification.DuplicateChain));
        group.Items.Add(CreateItem(1002, "kept", "kept", DimensionLayoutPolicyStatus.LessPreferred, "equivalent_measured_geometry", DimensionRecommendedAction.OperatorReview, DimensionCombineClassification.DuplicateChain));
        group.CombineCandidates.Add(CreateCombineCandidate([1001, 1002], "shared_point_neighbor_set", 1001));
        debug.Groups.Add(group);

        var packets = DimensionOrchestrationDebugBuilder.Build(debug, 10).Packets;

        Assert.DoesNotContain(packets, static packet => packet.Action == DimensionOrchestrationAction.Combine);
        Assert.Contains(packets, static packet => packet.Action == DimensionOrchestrationAction.Review);
        Assert.Contains(packets, static packet => packet.Action == DimensionOrchestrationAction.Keep);
    }

    [Fact]
    public void Build_EmitsSuppressForReductionCoveredItem()
    {
        var debug = new DimensionReductionDebugResult();
        var group = CreateGroup(10, DimensionType.Horizontal);
        group.Items.Add(CreateItem(1001, "rejected", "covered", DimensionLayoutPolicyStatus.Neutral, "neutral", DimensionRecommendedAction.Keep, DimensionCombineClassification.None, representativeDimensionId: 1002));
        group.Items.Add(CreateItem(1002, "kept", "kept", DimensionLayoutPolicyStatus.Neutral, "neutral", DimensionRecommendedAction.Keep, DimensionCombineClassification.None));
        debug.Groups.Add(group);

        var packets = DimensionOrchestrationDebugBuilder.Build(debug, 10).Packets;
        var suppress = Assert.Single(packets.Where(static packet => packet.Action == DimensionOrchestrationAction.Suppress));

        Assert.Equal(1001, suppress.PrimaryDimensionId);
        Assert.Equal("covered", suppress.Reason);
        Assert.Equal("reduction", suppress.Source);
        Assert.Equal(1002, suppress.Evidence.RepresentativeDimensionId);
    }

    [Fact]
    public void Build_EmitsReviewForOverlappingCombineCandidates()
    {
        var debug = new DimensionReductionDebugResult();
        var group = CreateGroup(10, DimensionType.Horizontal);
        group.Items.Add(CreateItem(1001, "kept", "kept", DimensionLayoutPolicyStatus.Neutral, "neutral", DimensionRecommendedAction.PreferCombine, DimensionCombineClassification.InformationPreservingMerge));
        group.Items.Add(CreateItem(1002, "kept", "kept", DimensionLayoutPolicyStatus.Neutral, "neutral", DimensionRecommendedAction.PreferCombine, DimensionCombineClassification.InformationPreservingMerge));
        group.Items.Add(CreateItem(1003, "kept", "kept", DimensionLayoutPolicyStatus.Neutral, "neutral", DimensionRecommendedAction.PreferCombine, DimensionCombineClassification.InformationPreservingMerge));
        group.CombineCandidates.Add(CreateCombineCandidate([1001, 1002], "shared_point_neighbor_set", 1001));
        group.CombineCandidates.Add(CreateCombineCandidate([1002, 1003], "shared_point_neighbor_set", 1002));
        debug.Groups.Add(group);

        var packets = DimensionOrchestrationDebugBuilder.Build(debug, 10).Packets;
        var review = Assert.Single(packets.Where(static packet => packet.Action == DimensionOrchestrationAction.Review));

        Assert.Equal("overlapping_combine_candidates", review.Reason);
        Assert.Equal("fused", review.Source);
        Assert.Equal(new[] { 1001, 1002, 1003 }, review.DimensionIds.ToArray());
        Assert.DoesNotContain(packets, static packet => packet.Action == DimensionOrchestrationAction.Combine);
    }

    [Fact]
    public void Build_EmitsKeepForRemainingReducedKeptItem()
    {
        var debug = new DimensionReductionDebugResult();
        var group = CreateGroup(10, DimensionType.Vertical);
        group.Items.Add(CreateItem(2001, "kept", "kept", DimensionLayoutPolicyStatus.Neutral, "neutral", DimensionRecommendedAction.Keep, DimensionCombineClassification.None));
        debug.Groups.Add(group);

        var packet = Assert.Single(DimensionOrchestrationDebugBuilder.Build(debug, 10).Packets);

        Assert.Equal(DimensionOrchestrationAction.Keep, packet.Action);
        Assert.Equal("keep", packet.Reason);
        Assert.Equal("fused", packet.Source);
        Assert.Equal(2001, packet.PrimaryDimensionId);
    }

    [Fact]
    public void Build_UsesDecisionContextViewAndWarningsWhenViewArgumentIsMissing()
    {
        var debug = new DimensionReductionDebugResult();
        var group = CreateGroup(10, DimensionType.Vertical);
        group.Items.Add(CreateItem(2001, "kept", "kept", DimensionLayoutPolicyStatus.Neutral, "neutral", DimensionRecommendedAction.Keep, DimensionCombineClassification.None));
        debug.Groups.Add(group);
        debug.DecisionContext.View.ViewId = 10;
        debug.DecisionContext.Warnings.Add("single_view_required");
        debug.DecisionContext.View.Warnings.Add("bolt-part:101:missing");
        debug.DecisionContext.Dimensions.Add(CreateContext(group.Items[0].Item));
        debug.DecisionContext.View.PartsBounds = new DrawingBoundsInfo
        {
            MinX = 0,
            MinY = 0,
            MaxX = 100,
            MaxY = 100
        };

        var result = DimensionOrchestrationDebugBuilder.Build(debug, viewId: null);

        Assert.Equal(10, result.ViewId);
        Assert.Contains("single_view_required", result.Warnings);
        Assert.Contains("bolt-part:101:missing", result.Warnings);
        Assert.True(result.Packets[0].Evidence.HasPartsBounds);
        Assert.Equal("top", result.Packets[0].Evidence.PartsBoundsSide);
        Assert.True(result.Packets[0].Evidence.IsOutsidePartsBounds);
        Assert.Equal(20, result.Packets[0].Evidence.OffsetFromPartsBounds, 3);
        Assert.Equal(100, result.Packets[0].Evidence.ReferenceLineLength, 3);
        Assert.Equal(0, result.Packets[0].Evidence.Distance, 3);
        Assert.Equal(0, result.Packets[0].Evidence.TopDirection);
        Assert.Equal(0, result.Packets[0].Evidence.ViewScale, 3);
        Assert.True(result.Packets[0].Evidence.CanEvaluatePartsBoundsGap);
        Assert.Equal(20, result.Packets[0].Evidence.CurrentPartsBoundsGapDrawing, 3);
        Assert.Equal(10, result.Packets[0].Evidence.TargetPartsBoundsGapPaper, 3);
        Assert.Equal(10, result.Packets[0].Evidence.TargetPartsBoundsGapDrawing, 3);
        Assert.False(result.Packets[0].Evidence.RequiresPartsBoundsGapCorrection);
        Assert.Equal(0, result.Packets[0].Evidence.SuggestedOutwardDeltaFromPartsBounds, 3);
    }

    private static DimensionGroupReductionDebugInfo CreateGroup(int? viewId, DimensionType dimensionType)
    {
        return new DimensionGroupReductionDebugInfo
        {
            RawGroup = new DimensionGroup
            {
                ViewId = viewId,
                ViewType = "FrontView",
                DomainDimensionType = dimensionType
            },
            ReducedGroup = new DimensionGroup
            {
                ViewId = viewId,
                ViewType = "FrontView",
                DomainDimensionType = dimensionType
            }
        };
    }

    private static DimensionReductionItemDebugInfo CreateItem(
        int dimensionId,
        string status,
        string reductionReason,
        DimensionLayoutPolicyStatus layoutStatus,
        string layoutReason,
        DimensionRecommendedAction recommendedAction,
        DimensionCombineClassification combineClassification,
        int? representativeDimensionId = null)
    {
        var item = new DimensionItem
        {
            DimensionId = dimensionId,
            ViewId = 10,
            ViewType = "FrontView",
            DomainDimensionType = DimensionType.Horizontal,
            GeometryKind = DimensionGeometryKind.Horizontal,
            SourceKind = DimensionSourceKind.Part,
            SortKey = dimensionId
        };

        return new DimensionReductionItemDebugInfo
        {
            Item = item,
            Status = status,
            Reason = reductionReason,
            RepresentativeDimensionId = representativeDimensionId,
            LayoutPolicy = new DimensionLayoutPolicyDecision
            {
                Status = layoutStatus,
                Reason = layoutReason,
                RecommendedAction = recommendedAction,
                CombineCandidate = combineClassification != DimensionCombineClassification.None,
                CombineReason = "shared_point_neighbor_set",
                CombineClassification = combineClassification
            }
        };
    }

    private static DimensionCombineCandidateDebugInfo CreateCombineCandidate(int[] dimensionIds, string mode, int baseDimensionId)
    {
        var candidate = new DimensionCombineCandidateDebugInfo
        {
            IsCombineCandidate = true,
            CombineConnectivityMode = mode,
            CombinePreview = new DimensionCombinePreviewDebugInfo
            {
                BaseDimensionId = baseDimensionId,
                Distance = 40
            }
        };

        foreach (var id in dimensionIds)
            candidate.DimensionIds.Add(id);

        candidate.CombinePreview.PointList.Add(new DrawingPointInfo { X = 0, Y = 0, Order = 0 });
        candidate.CombinePreview.PointList.Add(new DrawingPointInfo { X = 100, Y = 0, Order = 1 });
        return candidate;
    }

    private static DimensionContext CreateContext(DimensionItem item)
    {
        var context = new DimensionContext
        {
            DimensionId = item.DimensionId,
            Item = item
        };

        context.Geometry.ReferenceLine = new DrawingLineInfo
        {
            StartX = 0,
            StartY = 120,
            EndX = 100,
            EndY = 120
        };
        return context;
    }
}
