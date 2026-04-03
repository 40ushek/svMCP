using System.Linq;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DimensionGroupArrangementPlannerTests
{
    [Fact]
    public void BuildPlan_ForStack_MergesMultipleGroupsOfSameDimensionIdIntoSingleMoveUnit()
    {
        var stack = new DimensionGroupLineStack
        {
            ViewId = 10,
            ViewType = "FrontView",
            Orientation = "horizontal",
            TopDirection = -1,
            Direction = (1, 0)
        };

        stack.Groups.Add(CreateReferenceLineGroup(1, "horizontal", -1, (1, 0), 10, 0, 100, 0));
        stack.Groups.Add(CreateReferenceLineGroup(1, "horizontal", -1, (1, 0), 20, 0, 100, 0));
        stack.Groups.Add(CreateReferenceLineGroup(2, "horizontal", -1, (1, 0), 25, 0, 100, 0));

        var plan = DimensionGroupArrangementPlanner.BuildPlan(stack, 10);

        var proposal = Assert.Single(plan.Proposals);
        Assert.Equal(2, proposal.DimensionId);
        Assert.Equal(5, proposal.AxisShift, 3);
    }

    [Fact]
    public void BuildPlan_ForStack_UsesAlignedClusterAsSinglePlanningUnit()
    {
        var stack = new DimensionGroupLineStack
        {
            ViewId = 10,
            ViewType = "FrontView",
            Orientation = "horizontal",
            TopDirection = -1,
            Direction = (1, 0)
        };

        stack.Groups.Add(CreateReferenceLineGroup(1, "horizontal", -1, (1, 0), 10, 8, leadLineLength: 2));
        stack.Groups.Add(CreateReferenceLineGroup(2, "horizontal", -1, (1, 0), 12, 8, leadLineLength: 6));
        stack.Groups.Add(CreateReferenceLineGroup(3, "horizontal", -1, (1, 0), 14, 8, leadLineLength: 4));

        var plan = DimensionGroupArrangementPlanner.BuildPlan(stack, 5);

        var proposal = Assert.Single(plan.Proposals);
        Assert.Equal(3, proposal.DimensionId);
        Assert.Equal(1, proposal.AxisShift, 3);
    }

    [Fact]
    public void BuildPlan_ReturnsNoChanges_WhenGapIsAlreadyEnough()
    {
        var group = CreateGroup(
        [
            CreateMember(1, 10, 10, 100, 20),
            CreateMember(2, 10, 30, 100, 40)
        ], "horizontal");

        var plan = DimensionGroupArrangementPlanner.BuildPlan(group, 5);

        Assert.False(plan.HasChanges);
        Assert.Empty(plan.Proposals);
    }

    [Fact]
    public void BuildPlan_ShiftsLaterMember_WhenOverlapExists()
    {
        var group = CreateGroup(
        [
            CreateMember(1, 10, 10, 100, 25),
            CreateMember(2, 10, 20, 100, 35)
        ], "horizontal");

        var plan = DimensionGroupArrangementPlanner.BuildPlan(group, 5);

        var proposal = Assert.Single(plan.Proposals);
        Assert.Equal(2, proposal.DimensionId);
        Assert.Equal(10, proposal.AxisShift, 3);
    }

    [Fact]
    public void BuildPlan_AccumulatesShiftAcrossMultipleMembers()
    {
        var group = CreateGroup(
        [
            CreateMember(1, 10, 10, 100, 25),
            CreateMember(2, 10, 20, 100, 35),
            CreateMember(3, 10, 30, 100, 45)
        ], "horizontal");

        var plan = DimensionGroupArrangementPlanner.BuildPlan(group, 5);

        Assert.Equal(2, plan.Proposals.Count);
        Assert.Equal(2, plan.Proposals[0].DimensionId);
        Assert.Equal(10, plan.Proposals[0].AxisShift, 3);
        Assert.Equal(3, plan.Proposals[1].DimensionId);
        Assert.Equal(20, plan.Proposals[1].AxisShift, 3);
    }

    [Fact]
    public void BuildPlan_PropagatesExistingCumulativeShift_ToLaterNonOverlappingMembers()
    {
        var group = CreateGroup(
        [
            CreateMember(1, 10, 10, 100, 25),
            CreateMember(2, 10, 20, 100, 35),
            CreateMember(3, 10, 50, 100, 60),
            CreateMember(4, 10, 65, 100, 75)
        ], "horizontal");

        var plan = DimensionGroupArrangementPlanner.BuildPlan(group, 5);

        Assert.Equal(3, plan.Proposals.Count);
        Assert.Equal(2, plan.Proposals[0].DimensionId);
        Assert.Equal(10, plan.Proposals[0].AxisShift, 3);
        Assert.Equal(3, plan.Proposals[1].DimensionId);
        Assert.Equal(10, plan.Proposals[1].AxisShift, 3);
        Assert.Equal(4, plan.Proposals[2].DimensionId);
        Assert.Equal(10, plan.Proposals[2].AxisShift, 3);
    }

    private static DimensionGroup CreateGroup(DimensionGroupMember[] members, string orientation)
    {
        var group = new DimensionGroup
        {
            ViewId = 10,
            ViewType = "FrontView",
            Orientation = orientation
        };

        group.Members.AddRange(members);
        group.SortMembers();
        group.RefreshMetrics();
        return group;
    }

    private static DimensionGroup CreateReferenceLineGroup(
        int dimensionId,
        string orientation,
        int topDirection,
        (double X, double Y) direction,
        double lineOffset,
        double startAlong,
        double endAlong,
        double distance,
        double? leadLineLength = null)
    {
        DrawingLineInfo line;
        if (System.Math.Abs(direction.Y) <= System.Math.Abs(direction.X) * 0.01)
        {
            line = new DrawingLineInfo
            {
                StartX = startAlong,
                StartY = lineOffset,
                EndX = endAlong,
                EndY = lineOffset
            };
        }
        else
        {
            line = new DrawingLineInfo
            {
                StartX = lineOffset,
                StartY = startAlong,
                EndX = lineOffset,
                EndY = endAlong
            };
        }

        var member = new DimensionGroupMember
        {
            DimensionId = dimensionId,
            Distance = distance,
            DirectionX = direction.X,
            DirectionY = direction.Y,
            TopDirection = topDirection,
            ReferenceLine = line,
            LeadLineMain = leadLineLength.HasValue
                ? new DrawingLineInfo
                {
                    StartX = line.StartX,
                    StartY = line.StartY,
                    EndX = line.StartX,
                    EndY = line.StartY + leadLineLength.Value
                }
                : null,
            Bounds = TeklaDrawingDimensionsApi.CreateBoundsFromLine(line),
            Dimension = new DrawingDimensionInfo
            {
                Id = dimensionId
            }
        };

        var group = new DimensionGroup
        {
            ViewId = 10,
            ViewType = "FrontView",
            Orientation = orientation,
            TopDirection = topDirection,
            Direction = direction
        };
        group.Members.Add(member);
        group.RefreshMetrics();
        return group;
    }

    private static DimensionGroupMember CreateMember(int dimensionId, double minX, double minY, double maxX, double maxY)
    {
        return new DimensionGroupMember
        {
            DimensionId = dimensionId,
            SortKey = minX + minY,
            Bounds = new DrawingBoundsInfo
            {
                MinX = minX,
                MinY = minY,
                MaxX = maxX,
                MaxY = maxY
            },
            Dimension = new DrawingDimensionInfo
            {
                Id = dimensionId,
                Bounds = new DrawingBoundsInfo
                {
                    MinX = minX,
                    MinY = minY,
                    MaxX = maxX,
                    MaxY = maxY
                }
            }
        };
    }
}
