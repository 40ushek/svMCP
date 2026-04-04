using System.Linq;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DimensionArrangementDedupTests
{
    [Fact]
    public void ReduceWithDebug_RemovesEquivalentSimpleDuplicateWithSameSourceKind()
    {
        var group = CreateGroup(
            CreateSimpleItem(1, DimensionSourceKind.Part, yOffset: 40, distance: 8),
            CreateSimpleItem(2, DimensionSourceKind.Part, yOffset: 40, distance: 8));

        var result = DimensionArrangementDedup.ReduceWithDebug([group]);

        var reducedGroup = Assert.Single(result.ReducedGroups);
        var kept = Assert.Single(reducedGroup.DimensionList);
        Assert.Equal(1, kept.DimensionId);

        var debugGroup = Assert.Single(result.Groups);
        var rejected = Assert.Single(debugGroup.Items.Where(static item => item.Item.DimensionId == 2));
        Assert.Equal("rejected", rejected.Status);
        Assert.Equal("equivalent_simple", rejected.Reason);
        Assert.Equal(1, rejected.RepresentativeDimensionId);
    }

    [Fact]
    public void ReduceWithDebug_DoesNotRemoveEquivalentSimpleDuplicateWithDifferentSourceKind()
    {
        var group = CreateGroup(
            CreateSimpleItem(1, DimensionSourceKind.Part, yOffset: 40, distance: 8),
            CreateSimpleItem(2, DimensionSourceKind.Grid, yOffset: 40, distance: 8));

        var result = DimensionArrangementDedup.ReduceWithDebug([group]);

        var reducedGroup = Assert.Single(result.ReducedGroups);
        Assert.Equal(2, reducedGroup.DimensionList.Count);
        Assert.All(result.Groups.SelectMany(static groupInfo => groupInfo.Items), item => Assert.Equal("kept", item.Status));
    }

    [Fact]
    public void ReduceWithDebug_PrefersMoreInformativeCoveringItem()
    {
        var group = CreateGroup(
            CreateChainItem(1, DimensionSourceKind.Part, yOffset: 40, distance: 8),
            CreateSimpleItem(2, DimensionSourceKind.Part, yOffset: 40, distance: 8));

        var result = DimensionArrangementDedup.ReduceWithDebug([group]);

        var reducedGroup = Assert.Single(result.ReducedGroups);
        var kept = Assert.Single(reducedGroup.DimensionList);
        Assert.Equal(1, kept.DimensionId);

        var debugGroup = Assert.Single(result.Groups);
        var rejected = Assert.Single(debugGroup.Items.Where(static item => item.Item.DimensionId == 2));
        Assert.Equal("covered", rejected.Reason);
        Assert.Equal(1, rejected.RepresentativeDimensionId);
    }

    [Fact]
    public void Reduce_RemovesDuplicateBeforePlanningUnitsAreBuilt()
    {
        var groups = DimensionArrangementDedup.Reduce(
        [
            CreateGroup(CreateSimpleItem(1, DimensionSourceKind.Part, yOffset: 40, distance: 8)),
            CreateGroup(CreateSimpleItem(2, DimensionSourceKind.Part, yOffset: 40, distance: 8)),
            CreateGroup(CreateSimpleItem(3, DimensionSourceKind.Part, yOffset: 60, distance: 8))
        ]);

        var stack = Assert.Single(DimensionGroupSpacingAnalyzer.BuildStacks(groups));
        var planningUnits = DimensionGroupSpacingAnalyzer.BuildPlanningUnits(stack);

        Assert.Equal(2, planningUnits.Count);
        Assert.Equal(new[] { 1, 3 }, planningUnits.Select(static unit => unit.AnchorDimensionId).OrderBy(static id => id).ToArray());
    }

    private static DimensionGroup CreateGroup(params DimensionGroupMember[] members)
    {
        var group = new DimensionGroup
        {
            ViewId = 10,
            ViewType = "FrontView",
            DomainDimensionType = DimensionType.Horizontal,
            SourceKind = members[0].SourceKind,
            GeometryKind = members[0].GeometryKind,
            Orientation = "horizontal",
            Direction = (1, 0),
            TopDirection = -1
        };

        group.Members.AddRange(members);
        group.SortMembers();
        group.RefreshMetrics();
        return group;
    }

    private static DimensionGroupMember CreateSimpleItem(
        int dimensionId,
        DimensionSourceKind sourceKind,
        double yOffset,
        double distance)
    {
        return CreateItem(
            dimensionId,
            sourceKind,
            distance,
            points:
            [
                new DrawingPointInfo { X = 0, Y = 0, Order = 0 },
                new DrawingPointInfo { X = 100, Y = 0, Order = 1 }
            ],
            referenceLine: new DrawingLineInfo { StartX = 0, StartY = yOffset, EndX = 100, EndY = yOffset },
            leadLineMain: new DrawingLineInfo { StartX = 0, StartY = 0, EndX = 0, EndY = yOffset },
            leadLineSecond: new DrawingLineInfo { StartX = 100, StartY = 0, EndX = 100, EndY = yOffset });
    }

    private static DimensionGroupMember CreateChainItem(
        int dimensionId,
        DimensionSourceKind sourceKind,
        double yOffset,
        double distance)
    {
        return CreateItem(
            dimensionId,
            sourceKind,
            distance,
            points:
            [
                new DrawingPointInfo { X = 0, Y = 0, Order = 0 },
                new DrawingPointInfo { X = 100, Y = 0, Order = 1 },
                new DrawingPointInfo { X = 220, Y = 0, Order = 2 }
            ],
            referenceLine: new DrawingLineInfo { StartX = 0, StartY = yOffset, EndX = 220, EndY = yOffset },
            leadLineMain: new DrawingLineInfo { StartX = 0, StartY = 0, EndX = 0, EndY = yOffset },
            leadLineSecond: new DrawingLineInfo { StartX = 220, StartY = 0, EndX = 220, EndY = yOffset });
    }

    private static DimensionGroupMember CreateItem(
        int dimensionId,
        DimensionSourceKind sourceKind,
        double distance,
        DrawingPointInfo[] points,
        DrawingLineInfo referenceLine,
        DrawingLineInfo leadLineMain,
        DrawingLineInfo leadLineSecond)
    {
        var item = new DimensionGroupMember
        {
            DimensionId = dimensionId,
            ViewId = 10,
            ViewType = "FrontView",
            ViewScale = 1,
            DomainDimensionType = DimensionType.Horizontal,
            SourceKind = sourceKind,
            GeometryKind = DimensionGeometryKind.Horizontal,
            Orientation = "horizontal",
            Distance = distance,
            SortKey = referenceLine.StartY,
            DirectionX = 1,
            DirectionY = 0,
            TopDirection = -1,
            ReferenceLine = referenceLine,
            LeadLineMain = leadLineMain,
            LeadLineSecond = leadLineSecond,
            Bounds = TeklaDrawingDimensionsApi.CreateBoundsFromLine(referenceLine)
        };

        item.ReplacePointList(points);
        return item;
    }
}
