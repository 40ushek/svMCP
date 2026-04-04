using System.Linq;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DimensionOrchestrationEngineTests
{
    [Fact]
    public void BuildPlan_DelegatesToOrchestrationBuilders()
    {
        var debug = new DimensionReductionDebugResult();
        var group = new DimensionGroupReductionDebugInfo
        {
            RawGroup = new DimensionGroup
            {
                ViewId = 10,
                ViewType = "FrontView",
                DomainDimensionType = DimensionType.Horizontal
            },
            ReducedGroup = new DimensionGroup
            {
                ViewId = 10,
                ViewType = "FrontView",
                DomainDimensionType = DimensionType.Horizontal
            }
        };

        group.Items.Add(CreateItem(1001, DimensionLayoutPolicyStatus.Preferred));
        group.Items.Add(CreateItem(1002, DimensionLayoutPolicyStatus.LessPreferred));
        group.CombineCandidates.Add(CreateCombineCandidate([1001, 1002], 1001));
        debug.Groups.Add(group);

        var engine = new DimensionOrchestrationEngine();
        var debugResult = engine.BuildDebug(debug, 10);
        var planResult = engine.BuildPlan(debug, 10);

        Assert.Contains(debugResult.Packets, static packet => packet.Action == DimensionOrchestrationAction.Combine);
        Assert.Equal(2, planResult.Steps.Count);
        Assert.Equal(DimensionAiAssistedAction.Combine, planResult.Steps[0].Action);
        Assert.Equal(DimensionAiAssistedAction.Arrange, planResult.Steps[1].Action);
    }

    private static DimensionReductionItemDebugInfo CreateItem(int dimensionId, DimensionLayoutPolicyStatus status)
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
            Status = "kept",
            Reason = "kept",
            LayoutPolicy = new DimensionLayoutPolicyDecision
            {
                Status = status,
                Reason = status == DimensionLayoutPolicyStatus.Preferred ? "covers_poorer_chain" : "subchain_of_richer_dimension",
                RecommendedAction = DimensionRecommendedAction.PreferCombine,
                CombineCandidate = true,
                CombineReason = "shared_point_neighbor_set",
                CombineClassification = DimensionCombineClassification.InformationPreservingMerge
            }
        };
    }

    private static DimensionCombineCandidateDebugInfo CreateCombineCandidate(int[] dimensionIds, int baseDimensionId)
    {
        var candidate = new DimensionCombineCandidateDebugInfo
        {
            IsCombineCandidate = true,
            CombineConnectivityMode = "shared_point_neighbor_set",
            CombinePreview = new DimensionCombinePreviewDebugInfo
            {
                BaseDimensionId = baseDimensionId,
                Distance = 40
            }
        };

        foreach (var id in dimensionIds.OrderBy(static id => id))
            candidate.DimensionIds.Add(id);

        candidate.CombinePreview.PointList.Add(new DrawingPointInfo { X = 0, Y = 0, Order = 0 });
        candidate.CombinePreview.PointList.Add(new DrawingPointInfo { X = 100, Y = 0, Order = 1 });
        return candidate;
    }
}
