using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DimensionDistanceAdjustmentTranslatorTests
{
    [Fact]
    public void BuildPlan_ForStack_UsesMergedMoveUnitsByDimensionId()
    {
        var stack = new DimensionGroupLineStack
        {
            ViewId = 10,
            ViewType = "FrontView",
            Orientation = "horizontal",
            TopDirection = -1,
            Direction = (1, 0)
        };

        stack.Groups.Add(CreateReferenceLineGroup(1, -1, (1, 0), 10, 8));
        stack.Groups.Add(CreateReferenceLineGroup(1, -1, (1, 0), 20, 8));
        stack.Groups.Add(CreateReferenceLineGroup(2, -1, (1, 0), 30, 8));

        var axisPlan = new DimensionGroupArrangementPlan
        {
            ViewId = 10,
            ViewType = "FrontView",
            Orientation = "horizontal",
            TargetGapPaper = 5
        };
        axisPlan.Proposals.Add(new DimensionMoveProposal { DimensionId = 2, AxisShift = 12.5 });

        var plan = DimensionDistanceAdjustmentTranslator.BuildPlan(stack, axisPlan);

        var proposal = Assert.Single(plan.Proposals);
        Assert.True(proposal.CanApply);
        Assert.Equal(2, proposal.DimensionId);
        Assert.Equal(12.5, proposal.DistanceDelta, 3);
    }

    [Fact]
    public void BuildPlan_MapsHorizontalAxisShiftToDistanceDelta()
    {
        var group = new DimensionGroup
        {
            ViewId = 10,
            ViewType = "FrontView",
            Orientation = "horizontal",
            Direction = (1, 0),
            TopDirection = -1
        };
        group.Members.Add(new DimensionGroupMember
        {
            DimensionId = 42,
            Distance = 8,
            ReferenceLine = new DrawingLineInfo { StartX = 0, StartY = 10, EndX = 100, EndY = 10 }
        });
        var axisPlan = new DimensionGroupArrangementPlan
        {
            ViewId = 10,
            ViewType = "FrontView",
            Orientation = "horizontal",
            TargetGapPaper = 5
        };
        axisPlan.Proposals.Add(new DimensionMoveProposal { DimensionId = 42, AxisShift = 12.5 });

        var plan = DimensionDistanceAdjustmentTranslator.BuildPlan(group, axisPlan);

        var proposal = Assert.Single(plan.Proposals);
        Assert.True(proposal.CanApply);
        Assert.Equal(42, proposal.DimensionId);
        Assert.Equal(12.5, proposal.DistanceDelta, 3);
    }

    [Fact]
    public void BuildPlan_MapsNegativeDistanceUsingExistingDistanceSign()
    {
        var group = new DimensionGroup
        {
            ViewId = 10,
            ViewType = "FrontView",
            Orientation = "horizontal",
            Direction = (1, 0),
            TopDirection = -1
        };
        group.Members.Add(new DimensionGroupMember
        {
            DimensionId = 42,
            Distance = -8,
            ReferenceLine = new DrawingLineInfo { StartX = 0, StartY = 10, EndX = 100, EndY = 10 }
        });
        var axisPlan = new DimensionGroupArrangementPlan
        {
            ViewId = 10,
            ViewType = "FrontView",
            Orientation = "horizontal",
            TargetGapPaper = 5
        };
        axisPlan.Proposals.Add(new DimensionMoveProposal { DimensionId = 42, AxisShift = 12.5 });

        var plan = DimensionDistanceAdjustmentTranslator.BuildPlan(group, axisPlan);

        var proposal = Assert.Single(plan.Proposals);
        Assert.True(proposal.CanApply);
        Assert.Equal(-12.5, proposal.DistanceDelta, 3);
    }

    [Fact]
    public void BuildPlan_KeepsZeroDistanceUnsupported()
    {
        var group = new DimensionGroup
        {
            ViewId = 10,
            ViewType = "FrontView",
            Orientation = "horizontal",
            Direction = (1, 0),
            TopDirection = -1
        };
        group.Members.Add(new DimensionGroupMember
        {
            DimensionId = 42,
            Distance = 0,
            ReferenceLine = new DrawingLineInfo { StartX = 0, StartY = 10, EndX = 100, EndY = 10 }
        });
        var axisPlan = new DimensionGroupArrangementPlan
        {
            ViewId = 10,
            ViewType = "FrontView",
            Orientation = "horizontal",
            TargetGapPaper = 5
        };
        axisPlan.Proposals.Add(new DimensionMoveProposal { DimensionId = 42, AxisShift = 12.5 });

        var plan = DimensionDistanceAdjustmentTranslator.BuildPlan(group, axisPlan);

        var proposal = Assert.Single(plan.Proposals);
        Assert.False(proposal.CanApply);
        Assert.Contains("zero-distance", proposal.Reason);
    }

    [Fact]
    public void BuildPlan_KeepsAngledAdjustmentsUnsupported()
    {
        var group = new DimensionGroup
        {
            ViewId = 10,
            ViewType = "FrontView",
            Orientation = "angled",
            Direction = (0.707, 0.707),
            TopDirection = -1
        };
        var axisPlan = new DimensionGroupArrangementPlan
        {
            ViewId = 10,
            ViewType = "FrontView",
            Orientation = "angled",
            TargetGapPaper = 5
        };
        axisPlan.Proposals.Add(new DimensionMoveProposal { DimensionId = 42, AxisShift = 12.5 });

        var plan = DimensionDistanceAdjustmentTranslator.BuildPlan(group, axisPlan);

        var proposal = Assert.Single(plan.Proposals);
        Assert.False(proposal.CanApply);
        Assert.Equal(0, proposal.DistanceDelta, 3);
        Assert.Contains("axis-aligned", proposal.Reason);
    }

    [Fact]
    public void BuildPlan_KeepsGroupsWithoutReferenceLineUnsupported()
    {
        var group = new DimensionGroup
        {
            ViewId = 10,
            ViewType = "FrontView",
            Orientation = "horizontal",
            Direction = (1, 0),
            TopDirection = -1
        };
        group.Members.Add(new DimensionGroupMember { DimensionId = 42, Distance = 8 });
        var axisPlan = new DimensionGroupArrangementPlan
        {
            ViewId = 10,
            ViewType = "FrontView",
            Orientation = "horizontal",
            TargetGapPaper = 5
        };
        axisPlan.Proposals.Add(new DimensionMoveProposal { DimensionId = 42, AxisShift = 12.5 });

        var plan = DimensionDistanceAdjustmentTranslator.BuildPlan(group, axisPlan);

        var proposal = Assert.Single(plan.Proposals);
        Assert.False(proposal.CanApply);
        Assert.Contains("reference line", proposal.Reason);
    }

    private static DimensionGroup CreateReferenceLineGroup(
        int dimensionId,
        int topDirection,
        (double X, double Y) direction,
        double lineOffset,
        double distance)
    {
        var line = new DrawingLineInfo
        {
            StartX = 0,
            StartY = lineOffset,
            EndX = 100,
            EndY = lineOffset
        };

        var group = new DimensionGroup
        {
            ViewId = 10,
            ViewType = "FrontView",
            Orientation = "horizontal",
            TopDirection = topDirection,
            Direction = direction
        };
        group.Members.Add(new DimensionGroupMember
        {
            DimensionId = dimensionId,
            Distance = distance,
            DirectionX = direction.X,
            DirectionY = direction.Y,
            TopDirection = topDirection,
            ReferenceLine = line,
            Bounds = TeklaDrawingDimensionsApi.CreateBoundsFromLine(line),
            Dimension = new DrawingDimensionInfo
            {
                Id = dimensionId
            }
        });
        group.RefreshMetrics();
        return group;
    }
}
