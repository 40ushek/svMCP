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
        Assert.Equal(1, proposal.DimensionId);
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

        stack.Groups.Add(CreateReferenceLineGroup(1, "horizontal", -1, (1, 0), 10, 0, 100, 8, leadLineLength: 2));
        stack.Groups.Add(CreateReferenceLineGroup(2, "horizontal", -1, (1, 0), 12, 0, 100, 8, leadLineLength: 6));
        stack.Groups.Add(CreateReferenceLineGroup(3, "horizontal", -1, (1, 0), 14, 0, 100, 8, leadLineLength: 4));

        var plan = DimensionGroupArrangementPlanner.BuildPlan(stack, 5);

        Assert.False(plan.HasChanges);
    }

    [Fact]
    public void BuildPlan_ForStack_PrefersDecisionContextViewScale()
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
        stack.Groups.Add(CreateReferenceLineGroup(2, "horizontal", -1, (1, 0), 20, 0, 100, 0));

        var decisionContext = new DimensionDecisionContext
        {
            View = new DimensionViewContext
            {
                ViewId = 10,
                ViewScale = 20
            }
        };

        var plan = DimensionGroupArrangementPlanner.BuildPlan(stack, 5, decisionContext);

        Assert.Equal(100, plan.TargetGapDrawing, 3);
    }

    [Fact]
    public void BuildPlan_ForStack_ShiftsAllUnitsOutward_WhenFirstChainGapFromPartsBoundsIsTooSmall()
    {
        var stack = new DimensionGroupLineStack
        {
            ViewId = 10,
            ViewType = "FrontView",
            Orientation = "horizontal",
            TopDirection = 1,
            Direction = (1, 0)
        };

        stack.Groups.Add(CreateReferenceLineGroup(1, "horizontal", 1, (1, 0), 105, 0, 100, 0));
        stack.Groups.Add(CreateReferenceLineGroup(2, "horizontal", 1, (1, 0), 115, 0, 100, 0));

        var decisionContext = new DimensionDecisionContext
        {
            View = new DimensionViewContext
            {
                ViewId = 10,
                ViewScale = 1,
                PartsBounds = new DrawingBoundsInfo
                {
                    MinX = 0,
                    MinY = 0,
                    MaxX = 100,
                    MaxY = 100
                }
            }
        };
        decisionContext.Dimensions.Add(CreateContext(1, 105, 10));
        decisionContext.Dimensions.Add(CreateContext(2, 115, 10));

        var plan = DimensionGroupArrangementPlanner.BuildPlan(stack, 10, decisionContext);

        Assert.Equal(2, plan.Proposals.Count);
        var sorted3 = plan.Proposals.OrderBy(static p => p.DimensionId).ToList();
        Assert.Equal(1, sorted3[0].DimensionId);
        Assert.Equal(5, sorted3[0].AxisShift, 3);
        Assert.Equal(2, sorted3[1].DimensionId);
        Assert.Equal(5, sorted3[1].AxisShift, 3);
    }

    [Fact]
    public void BuildPlan_ReturnsNoChanges_WhenGapMatchesTarget()
    {
        var group = CreateGroup(
        [
            CreateMember(1, 10, 10, 100, 20),
            CreateMember(2, 10, 25, 100, 35)
        ], "horizontal");

        var plan = DimensionGroupArrangementPlanner.BuildPlan(group, 5);

        Assert.False(plan.HasChanges);
        Assert.Empty(plan.Proposals);
    }

    [Fact]
    public void BuildPlan_ForGroup_FallsBackToMemberScaleWhenDecisionContextViewDoesNotMatch()
    {
        var group = CreateGroup(
        [
            CreateMember(1, 10, 10, 100, 20),
            CreateMember(2, 10, 25, 100, 35)
        ], "horizontal");
        group.Members[0].ViewScale = 3;
        group.Members[1].ViewScale = 3;

        var decisionContext = new DimensionDecisionContext
        {
            View = new DimensionViewContext
            {
                ViewId = 20,
                ViewScale = 50
            }
        };

        var plan = DimensionGroupArrangementPlanner.BuildPlan(group, 5, decisionContext);

        Assert.Equal(15, plan.TargetGapDrawing, 3);
    }

    [Fact]
    public void BuildPlan_ForGroup_CombinesPartsBoundsAnchorShift_WithRegularSpacingShift()
    {
        var group = CreateGroup(
        [
            CreateMember(1, 0, 105, 100, 115, referenceY: 110),
            CreateMember(2, 0, 120, 100, 130, referenceY: 125)
        ], "horizontal");

        var decisionContext = new DimensionDecisionContext
        {
            View = new DimensionViewContext
            {
                ViewId = 10,
                ViewScale = 1,
                PartsBounds = new DrawingBoundsInfo
                {
                    MinX = 0,
                    MinY = 0,
                    MaxX = 100,
                    MaxY = 100
                }
            }
        };
        decisionContext.Dimensions.Add(CreateContext(1, 110, 10));
        decisionContext.Dimensions.Add(CreateContext(2, 125, 10));

        var plan = DimensionGroupArrangementPlanner.BuildPlan(group, 10, decisionContext);

        var proposal = Assert.Single(plan.Proposals);
        Assert.Equal(2, proposal.DimensionId);
        Assert.Equal(5, proposal.AxisShift, 3);
    }

    [Fact]
    public void BuildPlan_ForStack_AnchorsFirstUnitToPartsBounds_ThenRepositionsLaterUnitsFromIt_WhenOptionEnabled()
    {
        var stack = new DimensionGroupLineStack
        {
            ViewId = 10,
            ViewType = "FrontView",
            Orientation = "horizontal",
            TopDirection = 1,
            Direction = (1, 0)
        };

        stack.Groups.Add(CreateReferenceLineGroup(1, "horizontal", 1, (1, 0), 120, 0, 100, 0));
        stack.Groups.Add(CreateReferenceLineGroup(2, "horizontal", 1, (1, 0), 135, 0, 100, 0));

        var decisionContext = new DimensionDecisionContext
        {
            View = new DimensionViewContext
            {
                ViewId = 10,
                ViewScale = 1,
                PartsBounds = new DrawingBoundsInfo
                {
                    MinX = 0,
                    MinY = 0,
                    MaxX = 100,
                    MaxY = 100
                }
            }
        };
        decisionContext.Dimensions.Add(CreateContext(1, 120, 1));
        decisionContext.Dimensions.Add(CreateContext(2, 135, 1));

        var plan = DimensionGroupArrangementPlanner.BuildPlan(stack, 10, decisionContext, allowInwardCorrectionFromPartsBounds: true);

        Assert.Equal(2, plan.Proposals.Count);
        var sorted = plan.Proposals.OrderBy(static p => p.DimensionId).ToList();
        Assert.Equal(1, sorted[0].DimensionId);
        Assert.Equal(-10, sorted[0].AxisShift, 3);
        Assert.Equal(2, sorted[1].DimensionId);
        Assert.Equal(-15, sorted[1].AxisShift, 3);
    }

    [Fact]
    public void BuildPlan_ForStack_DoesNotPullTowardPartsBounds_WhenNearestChainIsTooFar_AndOptionDisabled()
    {
        var stack = new DimensionGroupLineStack
        {
            ViewId = 10,
            ViewType = "FrontView",
            Orientation = "horizontal",
            TopDirection = 1,
            Direction = (1, 0)
        };

        stack.Groups.Add(CreateReferenceLineGroup(1, "horizontal", 1, (1, 0), 120, 0, 100, 0));

        var decisionContext = new DimensionDecisionContext
        {
            View = new DimensionViewContext
            {
                ViewId = 10,
                ViewScale = 1,
                PartsBounds = new DrawingBoundsInfo
                {
                    MinX = 0,
                    MinY = 0,
                    MaxX = 100,
                    MaxY = 100
                }
            }
        };
        decisionContext.Dimensions.Add(CreateContext(1, 120, 1));

        var plan = DimensionGroupArrangementPlanner.BuildPlan(stack, 10, decisionContext);

        Assert.False(plan.HasChanges);
    }

    [Fact]
    public void BuildPlan_ForStack_UsesPositiveSignedOffsetForVerticalLeftPartsBoundsCorrection()
    {
        var stack = new DimensionGroupLineStack
        {
            ViewId = 10,
            ViewType = "FrontView",
            Orientation = "vertical",
            TopDirection = 1,
            Direction = (0, 1)
        };

        stack.Groups.Add(CreateReferenceLineGroup(1, "vertical", 1, (0, 1), -120, 0, 100, 120));

        var decisionContext = new DimensionDecisionContext
        {
            View = new DimensionViewContext
            {
                ViewId = 10,
                ViewScale = 1,
                PartsBounds = new DrawingBoundsInfo
                {
                    MinX = -5,
                    MinY = 0,
                    MaxX = 100,
                    MaxY = 100
                }
            }
        };
        decisionContext.Dimensions.Add(CreateVerticalContext(1, -120, 1));

        var plan = DimensionGroupArrangementPlanner.BuildPlan(stack, 10, decisionContext);

        var proposal = Assert.Single(plan.Proposals);
        Assert.Equal(1, proposal.DimensionId);
        Assert.Equal(25, proposal.AxisShift, 3);
    }

    [Fact]
    public void BuildPlan_ForGroup_DoesNotApplyPartsBoundsShift_WhenDecisionContextIsIncomplete()
    {
        var group = CreateGroup(
        [
            CreateMember(1, 0, 105, 100, 115, referenceY: 110),
            CreateMember(2, 0, 120, 100, 130, referenceY: 125)
        ], "horizontal");

        var decisionContext = new DimensionDecisionContext
        {
            View = new DimensionViewContext
            {
                ViewId = 10,
                ViewScale = 1,
                PartsBounds = new DrawingBoundsInfo
                {
                    MinX = 0,
                    MinY = 0,
                    MaxX = 100,
                    MaxY = 100
                }
            }
        };
        decisionContext.Dimensions.Add(CreateContext(1, 110, 10));

        var plan = DimensionGroupArrangementPlanner.BuildPlan(group, 10, decisionContext);

        var proposal = Assert.Single(plan.Proposals);
        Assert.Equal(2, proposal.DimensionId);
        Assert.Equal(5, proposal.AxisShift, 3);
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
    public void BuildPlan_PullsLaterMemberCloser_WhenGapIsTooLarge()
    {
        var group = CreateGroup(
        [
            CreateMember(1, 10, 10, 100, 20),
            CreateMember(2, 10, 35, 100, 45)
        ], "horizontal");

        var plan = DimensionGroupArrangementPlanner.BuildPlan(group, 5);

        var proposal = Assert.Single(plan.Proposals);
        Assert.Equal(2, proposal.DimensionId);
        Assert.Equal(-10, proposal.AxisShift, 3);
    }

    [Fact]
    public void BuildPlan_DoesNotPropagateEarlierExpansion_IntoAlreadyCorrectLaterGap()
    {
        var group = CreateGroup(
        [
            CreateMember(1, 10, 10, 100, 25),
            CreateMember(2, 10, 20, 100, 35),
            CreateMember(3, 10, 50, 100, 60),
            CreateMember(4, 10, 65, 100, 75)
        ], "horizontal");

        var plan = DimensionGroupArrangementPlanner.BuildPlan(group, 5);

        var proposal = Assert.Single(plan.Proposals);
        Assert.Equal(2, proposal.DimensionId);
        Assert.Equal(10, proposal.AxisShift, 3);
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
            ViewScale = 1,
            Orientation = orientation,
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
            Bounds = TeklaDrawingDimensionsApi.CreateBoundsFromLine(line)
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

    private static DimensionGroupMember CreateMember(
        int dimensionId,
        double minX,
        double minY,
        double maxX,
        double maxY,
        double? referenceY = null)
    {
        return new DimensionGroupMember
        {
            DimensionId = dimensionId,
            ViewScale = 1,
            Orientation = "horizontal",
            SortKey = minX + minY,
            ReferenceLine = referenceY.HasValue
                ? new DrawingLineInfo
                {
                    StartX = minX,
                    StartY = referenceY.Value,
                    EndX = maxX,
                    EndY = referenceY.Value
                }
                : null,
            Bounds = new DrawingBoundsInfo
            {
                MinX = minX,
                MinY = minY,
                MaxX = maxX,
                MaxY = maxY
            }
        };
    }

    private static DimensionContext CreateContext(int dimensionId, double referenceY, double viewScale)
    {
        var item = new DimensionItem
        {
            DimensionId = dimensionId,
            ViewId = 10
        };

        var context = new DimensionContext
        {
            DimensionId = dimensionId,
            ViewId = 10,
            ViewScale = viewScale,
            Item = item
        };
        context.Geometry.ReferenceLine = new DrawingLineInfo
        {
            StartX = 0,
            StartY = referenceY,
            EndX = 100,
            EndY = referenceY
        };
        return context;
    }

    private static DimensionContext CreateVerticalContext(int dimensionId, double referenceX, double viewScale)
    {
        var item = new DimensionItem
        {
            DimensionId = dimensionId,
            ViewId = 10,
            TopDirection = 1
        };

        var context = new DimensionContext
        {
            DimensionId = dimensionId,
            ViewId = 10,
            ViewScale = viewScale,
            Item = item
        };
        context.Geometry.ReferenceLine = new DrawingLineInfo
        {
            StartX = referenceX,
            StartY = 0,
            EndX = referenceX,
            EndY = 100
        };
        return context;
    }
}
