using System.Linq;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DimensionGroupArrangementPlannerTests
{
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
