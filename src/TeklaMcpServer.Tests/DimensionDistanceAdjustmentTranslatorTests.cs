using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DimensionDistanceAdjustmentTranslatorTests
{
    [Fact]
    public void BuildPlan_MapsHorizontalAxisShiftToDistanceDelta()
    {
        var group = new DimensionGroup
        {
            ViewId = 10,
            ViewType = "FrontView",
            Orientation = "horizontal"
        };
        group.Members.Add(new DimensionGroupMember { DimensionId = 42, Distance = 8 });
        var axisPlan = new DimensionGroupArrangementPlan
        {
            ViewId = 10,
            ViewType = "FrontView",
            Orientation = "horizontal",
            TargetGap = 5
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
            Orientation = "horizontal"
        };
        group.Members.Add(new DimensionGroupMember { DimensionId = 42, Distance = -8 });
        var axisPlan = new DimensionGroupArrangementPlan
        {
            ViewId = 10,
            ViewType = "FrontView",
            Orientation = "horizontal",
            TargetGap = 5
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
            Orientation = "horizontal"
        };
        group.Members.Add(new DimensionGroupMember { DimensionId = 42, Distance = 0 });
        var axisPlan = new DimensionGroupArrangementPlan
        {
            ViewId = 10,
            ViewType = "FrontView",
            Orientation = "horizontal",
            TargetGap = 5
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
            Orientation = "angled"
        };
        var axisPlan = new DimensionGroupArrangementPlan
        {
            ViewId = 10,
            ViewType = "FrontView",
            Orientation = "angled",
            TargetGap = 5
        };
        axisPlan.Proposals.Add(new DimensionMoveProposal { DimensionId = 42, AxisShift = 12.5 });

        var plan = DimensionDistanceAdjustmentTranslator.BuildPlan(group, axisPlan);

        var proposal = Assert.Single(plan.Proposals);
        Assert.False(proposal.CanApply);
        Assert.Equal(0, proposal.DistanceDelta, 3);
        Assert.Contains("angled", proposal.Reason);
    }
}
