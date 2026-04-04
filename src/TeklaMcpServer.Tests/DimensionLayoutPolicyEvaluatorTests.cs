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
}
