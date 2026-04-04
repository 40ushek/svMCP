using System.Collections.Generic;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DimensionLayoutPolicyEvaluatorTests
{
    [Fact]
    public void Evaluate_MarksPoorerOverlappingChainAsLessPreferred()
    {
        var richer = CreateItem(1001, [0, 50, 100], [101]);
        var poorer = CreateItem(1002, [0, 100], [101]);
        var contexts = new Dictionary<DimensionItem, DimensionContext>
        {
            [richer] = CreateContext(richer, 101),
            [poorer] = CreateContext(poorer, 101)
        };

        var decisions = DimensionLayoutPolicyEvaluator.Evaluate([richer, poorer], contexts);

        Assert.Equal(DimensionLayoutPolicyStatus.Preferred, decisions[richer].Status);
        Assert.Equal("covers_poorer_chain", decisions[richer].Reason);
        Assert.Equal(DimensionLayoutPolicyStatus.LessPreferred, decisions[poorer].Status);
        Assert.Equal("subchain_of_richer_dimension", decisions[poorer].Reason);
        Assert.Equal(1001, decisions[poorer].PreferredDimensionId);
    }

    [Fact]
    public void Evaluate_DoesNotMarkWhenSourceIdentityDoesNotOverlap()
    {
        var richer = CreateItem(1001, [0, 50, 100], [101]);
        var poorer = CreateItem(1002, [0, 100], [202]);
        var contexts = new Dictionary<DimensionItem, DimensionContext>
        {
            [richer] = CreateContext(richer, 101),
            [poorer] = CreateContext(poorer, 202)
        };

        var decisions = DimensionLayoutPolicyEvaluator.Evaluate([richer, poorer], contexts);

        Assert.Equal(DimensionLayoutPolicyStatus.Neutral, decisions[richer].Status);
        Assert.Equal(DimensionLayoutPolicyStatus.Neutral, decisions[poorer].Status);
    }

    [Fact]
    public void Evaluate_MarksEquivalentControlGeometryAsLessPreferredUsingSmallerDistance()
    {
        var preferred = CreateFreeItem(2001, 60, [(0, 0), (100, 100)]);
        var duplicate = CreateFreeItem(2002, 117.312, [(0, 0), (100, 100)]);
        var contexts = new Dictionary<DimensionItem, DimensionContext>
        {
            [preferred] = CreateControlContext(preferred),
            [duplicate] = CreateControlContext(duplicate)
        };

        var decisions = DimensionLayoutPolicyEvaluator.Evaluate([preferred, duplicate], contexts);

        Assert.Equal(DimensionLayoutPolicyStatus.Preferred, decisions[preferred].Status);
        Assert.Equal("keeps_compact_equivalent_geometry", decisions[preferred].Reason);
        Assert.Equal(DimensionLayoutPolicyStatus.LessPreferred, decisions[duplicate].Status);
        Assert.Equal("equivalent_measured_geometry", decisions[duplicate].Reason);
        Assert.Equal(2001, decisions[duplicate].PreferredDimensionId);
    }

    [Fact]
    public void AttachRecommendedActions_MarksEquivalentDuplicateAsSuppressCandidate()
    {
        var preferred = CreateFreeItem(2001, 60, [(0, 0), (100, 100)]);
        var duplicate = CreateFreeItem(2002, 117.312, [(0, 0), (100, 100)]);
        var contexts = new Dictionary<DimensionItem, DimensionContext>
        {
            [preferred] = CreateControlContext(preferred),
            [duplicate] = CreateControlContext(duplicate)
        };

        var decisions = DimensionLayoutPolicyEvaluator.Evaluate([preferred, duplicate], contexts);

        DimensionLayoutPolicyEvaluator.AttachRecommendedActions(decisions);

        Assert.Equal(DimensionRecommendedAction.Keep, decisions[preferred].RecommendedAction);
        Assert.Equal(DimensionRecommendedAction.SuppressCandidate, decisions[duplicate].RecommendedAction);
    }

    [Fact]
    public void AttachCombineCandidates_MarksMergeableItemsAndKeepsStatus()
    {
        var richer = CreateItem(1001, [0, 50, 100], [101]);
        var poorer = CreateItem(1002, [0, 100], [101]);
        var contexts = new Dictionary<DimensionItem, DimensionContext>
        {
            [richer] = CreateContext(richer, 101),
            [poorer] = CreateContext(poorer, 101)
        };

        var decisions = DimensionLayoutPolicyEvaluator.Evaluate([richer, poorer], contexts);
        var itemsById = new Dictionary<int, DimensionItem>
        {
            [richer.DimensionId] = richer,
            [poorer.DimensionId] = poorer
        };
        var combineCandidates = new[]
        {
            new DimensionCombineCandidateDebugInfo
            {
                IsCombineCandidate = true,
                CombineConnectivityMode = "shared_point_neighbor_set",
                DimensionIds = { richer.DimensionId, poorer.DimensionId }
            }
        };

        DimensionLayoutPolicyEvaluator.AttachCombineCandidates(itemsById, decisions, combineCandidates);
        DimensionLayoutPolicyEvaluator.AttachRecommendedActions(decisions);

        Assert.True(decisions[richer].CombineCandidate);
        Assert.Equal("shared_point_neighbor_set", decisions[richer].CombineReason);
        Assert.Equal([poorer.DimensionId], decisions[richer].CombineWithDimensionIds);
        Assert.Equal(DimensionLayoutPolicyStatus.Preferred, decisions[richer].Status);
        Assert.Equal(DimensionRecommendedAction.PreferCombine, decisions[richer].RecommendedAction);

        Assert.True(decisions[poorer].CombineCandidate);
        Assert.Equal("shared_point_neighbor_set", decisions[poorer].CombineReason);
        Assert.Equal([richer.DimensionId], decisions[poorer].CombineWithDimensionIds);
        Assert.Equal(DimensionLayoutPolicyStatus.LessPreferred, decisions[poorer].Status);
        Assert.Equal(DimensionRecommendedAction.PreferCombine, decisions[poorer].RecommendedAction);
    }

    [Fact]
    public void AttachCombineCandidates_IgnoresBlockedCandidates()
    {
        var left = CreateItem(1001, [0, 100], [101]);
        var right = CreateItem(1002, [100, 200], [101]);
        var contexts = new Dictionary<DimensionItem, DimensionContext>
        {
            [left] = CreateContext(left, 101),
            [right] = CreateContext(right, 101)
        };

        var decisions = DimensionLayoutPolicyEvaluator.Evaluate([left, right], contexts);
        var itemsById = new Dictionary<int, DimensionItem>
        {
            [left.DimensionId] = left,
            [right.DimensionId] = right
        };
        var combineCandidates = new[]
        {
            new DimensionCombineCandidateDebugInfo
            {
                IsCombineCandidate = false,
                CombineConnectivityMode = "blocked_case",
                DimensionIds = { left.DimensionId, right.DimensionId }
            }
        };

        DimensionLayoutPolicyEvaluator.AttachCombineCandidates(itemsById, decisions, combineCandidates);
        DimensionLayoutPolicyEvaluator.AttachRecommendedActions(decisions);

        Assert.False(decisions[left].CombineCandidate);
        Assert.False(decisions[right].CombineCandidate);
        Assert.Empty(decisions[left].CombineWithDimensionIds);
        Assert.Empty(decisions[right].CombineWithDimensionIds);
        Assert.Equal(DimensionRecommendedAction.Keep, decisions[left].RecommendedAction);
        Assert.Equal(DimensionRecommendedAction.Keep, decisions[right].RecommendedAction);
    }

    [Fact]
    public void AttachRecommendedActions_MarksPoorerSubchainAsOperatorReview()
    {
        var richer = CreateItem(1001, [0, 50, 100], [101]);
        var poorer = CreateItem(1002, [0, 100], [101]);
        var contexts = new Dictionary<DimensionItem, DimensionContext>
        {
            [richer] = CreateContext(richer, 101),
            [poorer] = CreateContext(poorer, 101)
        };

        var decisions = DimensionLayoutPolicyEvaluator.Evaluate([richer, poorer], contexts);

        DimensionLayoutPolicyEvaluator.AttachRecommendedActions(decisions);

        Assert.Equal(DimensionRecommendedAction.Keep, decisions[richer].RecommendedAction);
        Assert.Equal(DimensionRecommendedAction.OperatorReview, decisions[poorer].RecommendedAction);
    }

    private static DimensionItem CreateItem(int dimensionId, double[] positions, int[] sourceIds)
    {
        var dimension = new DrawingDimensionInfo
        {
            Id = dimensionId,
            ViewId = 10,
            ViewType = "FrontView",
            ViewScale = 15,
            SourceKind = DimensionSourceKind.Part,
            GeometryKind = DimensionGeometryKind.Horizontal,
            ClassifiedDimensionType = DimensionType.Horizontal
        };

        foreach (var sourceId in sourceIds)
            dimension.SourceObjectIds.Add(sourceId);

        var item = new DimensionItem
        {
            DimensionId = dimensionId,
            ViewId = 10,
            ViewType = "FrontView",
            ViewScale = 15,
            DomainDimensionType = DimensionType.Horizontal,
            SourceKind = DimensionSourceKind.Part,
            GeometryKind = DimensionGeometryKind.Horizontal,
            TeklaDimensionType = "Horizontal",
            Distance = 20,
            DirectionX = 1,
            DirectionY = 0,
            TopDirection = -1,
            ReferenceLine = new DrawingLineInfo
            {
                StartX = positions[0],
                StartY = -20,
                EndX = positions[^1],
                EndY = -20
            },
            Dimension = dimension
        };

        for (var i = 0; i < positions.Length; i++)
        {
            item.PointList.Add(new DrawingPointInfo
            {
                X = positions[i],
                Y = 0,
                Order = i
            });
        }

        item.StartX = positions[0];
        item.StartY = 0;
        item.EndX = positions[^1];
        item.EndY = 0;
        item.StartPointOrder = 0;
        item.EndPointOrder = positions.Length - 1;
        item.CenterX = (positions[0] + positions[^1]) / 2.0;
        item.CenterY = 0;
        item.SegmentIds.Add(dimensionId);
        return item;
    }

    private static DimensionItem CreateFreeItem(int dimensionId, double distance, (double X, double Y)[] points)
    {
        var dimension = new DrawingDimensionInfo
        {
            Id = dimensionId,
            ViewId = 10,
            ViewType = "FrontView",
            ViewScale = 15,
            SourceKind = DimensionSourceKind.Unknown,
            GeometryKind = DimensionGeometryKind.Free,
            ClassifiedDimensionType = DimensionType.Free
        };

        var item = new DimensionItem
        {
            DimensionId = dimensionId,
            ViewId = 10,
            ViewType = "FrontView",
            ViewScale = 15,
            DomainDimensionType = DimensionType.Free,
            SourceKind = DimensionSourceKind.Unknown,
            GeometryKind = DimensionGeometryKind.Free,
            TeklaDimensionType = "Free",
            Distance = distance,
            DirectionX = 0.70710678,
            DirectionY = 0.70710678,
            TopDirection = 1,
            Dimension = dimension
        };

        for (var i = 0; i < points.Length; i++)
        {
            item.PointList.Add(new DrawingPointInfo
            {
                X = points[i].X,
                Y = points[i].Y,
                Order = i
            });
        }

        item.StartX = points[0].X;
        item.StartY = points[0].Y;
        item.EndX = points[^1].X;
        item.EndY = points[^1].Y;
        item.StartPointOrder = 0;
        item.EndPointOrder = points.Length - 1;
        item.CenterX = (points[0].X + points[^1].X) / 2.0;
        item.CenterY = (points[0].Y + points[^1].Y) / 2.0;
        item.SegmentIds.Add(dimensionId);
        return item;
    }

    private static DimensionContext CreateContext(DimensionItem item, int sourceId)
    {
        var context = new DimensionContext
        {
            DimensionId = item.DimensionId,
            Item = item,
            SourceKind = DimensionSourceKind.Part,
            Role = DimensionContextRole.External
        };

        context.Source.SourceKind = DimensionSourceKind.Part;
        context.Source.SourceObjectIds.Add(sourceId);
        foreach (var point in item.PointList)
        {
            context.Association.PointAssociations.Add(new DimensionContextPointAssociation
            {
                Order = point.Order,
                Status = DimensionPointObjectMappingStatus.Matched,
                MatchedModelId = sourceId,
                MatchedSourceKind = DimensionSourceKind.Part.ToString()
            });
        }

        return context;
    }

    private static DimensionContext CreateControlContext(DimensionItem item)
    {
        return new DimensionContext
        {
            DimensionId = item.DimensionId,
            Item = item,
            SourceKind = DimensionSourceKind.Unknown,
            Role = DimensionContextRole.Control
        };
    }
}
