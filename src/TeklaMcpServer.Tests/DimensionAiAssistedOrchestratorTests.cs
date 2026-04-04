using System.Linq;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DimensionAiAssistedOrchestratorTests
{
    [Fact]
    public void Build_EmitsCombineThenArrangeStepsForInformationPreservingMerge()
    {
        var debug = new DimensionReductionDebugResult();
        var group = CreateGroup(10, DimensionType.Horizontal);
        group.Items.Add(CreateItem(1001, "kept", "kept", DimensionLayoutPolicyStatus.Preferred, "covers_poorer_chain", DimensionRecommendedAction.PreferCombine, DimensionCombineClassification.InformationPreservingMerge));
        group.Items.Add(CreateItem(1002, "kept", "kept", DimensionLayoutPolicyStatus.LessPreferred, "subchain_of_richer_dimension", DimensionRecommendedAction.PreferCombine, DimensionCombineClassification.InformationPreservingMerge));
        group.CombineCandidates.Add(CreateCombineCandidate(new[] { 1001, 1002 }, "shared_point_neighbor_set", 1001));
        debug.Groups.Add(group);

        var result = new DimensionAiAssistedOrchestrator().Build(debug, 10);

        Assert.Equal(2, result.Steps.Count);

        var combine = result.Steps[0];
        Assert.Equal(DimensionAiAssistedAction.Combine, combine.Action);
        Assert.Equal(1, combine.StepOrder);
        Assert.Equal("combine_dimensions", combine.ToolName);
        Assert.True(combine.PreviewOnly);
        Assert.NotNull(combine.ToolArguments);
        Assert.True(combine.ToolArguments!.PreviewOnly);
        Assert.Equal(new[] { 1001, 1002 }, combine.ToolArguments.DimensionIds.ToArray());
        Assert.NotNull(combine.ApplyToolArguments);
        Assert.False(combine.ApplyToolArguments!.PreviewOnly);

        var arrange = result.Steps[1];
        Assert.Equal(DimensionAiAssistedAction.Arrange, arrange.Action);
        Assert.Equal(2, arrange.StepOrder);
        Assert.Equal("arrange_dimensions", arrange.ToolName);
        Assert.False(arrange.PreviewOnly);
        Assert.NotNull(arrange.ToolArguments);
        Assert.Equal(10, arrange.ToolArguments!.ViewId);
        Assert.Equal(TeklaDrawingDimensionsApi.DefaultArrangeTargetGapPaper, arrange.ToolArguments.TargetGap);
    }

    [Fact]
    public void Build_EmitsReviewOnlyForDuplicateChain()
    {
        var debug = new DimensionReductionDebugResult();
        var group = CreateGroup(10, DimensionType.Horizontal);
        group.Items.Add(CreateItem(1001, "kept", "kept", DimensionLayoutPolicyStatus.Preferred, "keeps_compact_equivalent_geometry", DimensionRecommendedAction.Keep, DimensionCombineClassification.DuplicateChain));
        group.Items.Add(CreateItem(1002, "kept", "kept", DimensionLayoutPolicyStatus.LessPreferred, "equivalent_measured_geometry", DimensionRecommendedAction.OperatorReview, DimensionCombineClassification.DuplicateChain));
        group.CombineCandidates.Add(CreateCombineCandidate(new[] { 1001, 1002 }, "shared_point_neighbor_set", 1001));
        debug.Groups.Add(group);

        var result = new DimensionAiAssistedOrchestrator().Build(debug, 10);

        var review = Assert.Single(result.Steps);
        Assert.Equal(DimensionAiAssistedAction.ReviewOnly, review.Action);
        Assert.Equal("equivalent_measured_geometry", review.Reason);
        Assert.Equal(string.Empty, review.ToolName);
    }

    [Fact]
    public void Build_EmitsReviewOnlyForSuppressPacket()
    {
        var debug = new DimensionReductionDebugResult();
        var group = CreateGroup(10, DimensionType.Horizontal);
        group.Items.Add(CreateItem(1001, "rejected", "covered", DimensionLayoutPolicyStatus.Neutral, "neutral", DimensionRecommendedAction.Keep, DimensionCombineClassification.None, representativeDimensionId: 1002));
        group.Items.Add(CreateItem(1002, "kept", "kept", DimensionLayoutPolicyStatus.Neutral, "neutral", DimensionRecommendedAction.Keep, DimensionCombineClassification.None));
        debug.Groups.Add(group);

        var result = new DimensionAiAssistedOrchestrator().Build(debug, 10);

        var review = Assert.Single(result.Steps);
        Assert.Equal(DimensionAiAssistedAction.ReviewOnly, review.Action);
        Assert.Equal("covered", review.Reason);
        Assert.Equal("reduction", review.Source);
    }

    [Fact]
    public void Build_IncludesGeometryEvidenceFromContext()
    {
        var debug = new DimensionReductionDebugResult();
        var group = CreateGroup(10, DimensionType.Vertical);
        group.Items.Add(CreateItem(2001, "kept", "kept", DimensionLayoutPolicyStatus.Preferred, "covers_poorer_chain", DimensionRecommendedAction.PreferCombine, DimensionCombineClassification.InformationPreservingMerge));
        group.Items.Add(CreateItem(2002, "kept", "kept", DimensionLayoutPolicyStatus.LessPreferred, "subchain_of_richer_dimension", DimensionRecommendedAction.PreferCombine, DimensionCombineClassification.InformationPreservingMerge));
        group.CombineCandidates.Add(CreateCombineCandidate(new[] { 2001, 2002 }, "shared_point_neighbor_set", 2001));
        debug.Groups.Add(group);

        var result = new DimensionAiAssistedOrchestrator().Build(debug, 10);

        var evidence = result.Steps[0].Evidence;
        Assert.NotNull(evidence.LineDirection);
        Assert.NotNull(evidence.NormalDirection);
        Assert.Equal(0, evidence.StartAlong);
        Assert.Equal(100, evidence.EndAlong);
        Assert.NotNull(evidence.GeometryBand);
        Assert.Equal(1, evidence.SegmentGeometryCount);
        Assert.True(evidence.HasTextBounds);
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
            Context = CreateContext(item),
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

    private static DimensionContext CreateContext(DimensionItem item)
    {
        var context = new DimensionContext
        {
            DimensionId = item.DimensionId,
            Item = item
        };

        context.AnnotationGeometry.LineDirection = new DrawingVectorInfo { X = 1, Y = 0 };
        context.AnnotationGeometry.NormalDirection = new DrawingVectorInfo { X = 0, Y = -1 };
        context.AnnotationGeometry.StartAlong = 0;
        context.AnnotationGeometry.EndAlong = 100;
        context.AnnotationGeometry.TextBounds = new DrawingBoundsInfo
        {
            MinX = 40,
            MinY = -20,
            MaxX = 60,
            MaxY = -10
        };
        context.AnnotationGeometry.LocalBand = new DimensionGeometryBand
        {
            StartAlong = 0,
            EndAlong = 100,
            MinOffset = -20,
            MaxOffset = 0
        };
        context.AnnotationGeometry.SegmentGeometries.Add(new DimensionSegmentGeometry
        {
            SegmentId = item.DimensionId
        });
        return context;
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
}
