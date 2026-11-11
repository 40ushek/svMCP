using TeklaMcpServer.Api.Drawing.ViewLayout.Constraints;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class ConstraintScaleSearchTests
{
    [Fact]
    public void LayoutThatGivesNothingUp_IsTakenAtOnce()
    {
        var search = new ConstraintScaleSearch();

        Assert.Equal(ConstraintScaleDecision.Select, search.Offer(0, isLast: false, cost: 0));
    }

    [Fact]
    public void BrokenLinkAtTheLargerScale_LosesToACleanLayoutOneStepSmaller()
    {
        var search = new ConstraintScaleSearch();

        // 1:5 fits only with a view off the base view's axis (1.5); 1:10 fits cleanly.
        Assert.Equal(ConstraintScaleDecision.LookAhead, search.Offer(0, isLast: false, cost: 1.5));
        Assert.Equal(ConstraintScaleDecision.Select, search.Offer(1, isLast: false, cost: 0));
        Assert.Equal(1, search.BestIndex);
    }

    [Fact]
    public void BrokenLinkAtTheLargerScale_WinsWhenTheSmallerScaleIsNoBetter_AndTheSearchGoesBack()
    {
        var search = new ConstraintScaleSearch();

        Assert.Equal(ConstraintScaleDecision.LookAhead, search.Offer(0, isLast: false, cost: 1.5));
        // One step down still needs a view on the wrong side: 1 + 0.5 is not better than 1.5.
        Assert.Equal(ConstraintScaleDecision.GoBack, search.Offer(1, isLast: false, cost: 0.5));
        Assert.Equal(0, search.BestIndex);
        Assert.True(search.IsReturning);
        Assert.Equal(ConstraintScaleDecision.Select, search.Offer(2, isLast: false, cost: 1.5));
    }

    [Fact]
    public void WrongSideAtTheLargerScale_IsKept_WithoutTryingASmallerScale()
    {
        var search = new ConstraintScaleSearch();

        // A view on the opposite side costs half a step; a whole step down cannot beat that.
        Assert.Equal(ConstraintScaleDecision.Select, search.Offer(0, isLast: false, cost: 0.5));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void UnderBalanced_AViewOrTwoOffTheAxis_KeepTheScale_WithoutTryingASmallerOne(int viewsOffAxis)
    {
        var policy = ConstraintLayoutPolicy.Balanced;

        Assert.Equal(
            ConstraintScaleDecision.Select,
            new ConstraintScaleSearch(policy.ScaleStepCost).Offer(0, isLast: false, cost: viewsOffAxis * policy.LinkBreakCost));
    }

    [Fact]
    public void OneViewOffTheAxis_OnAHalfEmptySheet_IsTakenAtOnce()
    {
        // The figures of drawing M.179: fill 0.40 at 1:5 with one view off the axis,
        // 0.22 at 1:10 with nothing given up. The user took 1:5.
        var policy = ConstraintLayoutPolicy.Balanced;

        Assert.Equal(
            ConstraintScaleDecision.Select,
            new ConstraintScaleSearch(policy.ScaleStepCost)
                .Offer(0, isLast: false, cost: policy.LinkBreakCost, emptiness: policy.GetEmptinessCost(0.403)));
    }

    [Fact]
    public void ThreeViewsOffTheAxis_BeatACleanLayoutThatLeavesTheSheetEmpty_AndTheSearchGoesBack()
    {
        // The figures of drawing M.123: at 1:10 the two end views and the section stand
        // in a row off the base view's axis, fill 0.59; at 1:15 every view is on its
        // axis, fill 0.26. The user took 1:10.
        var policy = ConstraintLayoutPolicy.Balanced;
        var search = new ConstraintScaleSearch(policy.ScaleStepCost);

        Assert.Equal(
            ConstraintScaleDecision.LookAhead,
            search.Offer(0, isLast: false, cost: 3 * policy.LinkBreakCost, emptiness: policy.GetEmptinessCost(0.59)));
        Assert.Equal(
            ConstraintScaleDecision.GoBack,
            search.Offer(1, isLast: false, cost: 0, emptiness: policy.GetEmptinessCost(0.26)));
        Assert.Equal(0, search.BestIndex);
    }

    [Fact]
    public void ThreeViewsOffTheAxis_LoseToACleanLayoutOnASheetThatStaysFilled()
    {
        var policy = ConstraintLayoutPolicy.Balanced;
        var search = new ConstraintScaleSearch(policy.ScaleStepCost);

        search.Offer(0, isLast: false, cost: 3 * policy.LinkBreakCost, emptiness: policy.GetEmptinessCost(0.80));

        Assert.Equal(
            ConstraintScaleDecision.Select,
            search.Offer(1, isLast: false, cost: 0, emptiness: policy.GetEmptinessCost(0.55)));
        Assert.Equal(1, search.BestIndex);
    }

    [Fact]
    public void UnderPreferScale_ThreeViewsOffTheAxis_KeepTheScale_WithoutTryingASmallerOne()
    {
        var policy = ConstraintLayoutPolicy.PreferScale;

        Assert.Equal(
            ConstraintScaleDecision.Select,
            new ConstraintScaleSearch(policy.ScaleStepCost).Offer(0, isLast: false, cost: 3 * policy.LinkBreakCost));
    }

    [Fact]
    public void EmptinessAlone_NeverSendsTheSearchToASmallerScale()
    {
        // Nothing given up and a nearly empty sheet: a smaller scale would only be emptier.
        Assert.Equal(
            ConstraintScaleDecision.Select,
            new ConstraintScaleSearch().Offer(0, isLast: false, cost: 0, emptiness: 1.8));
    }

    [Theory]
    [InlineData(0.50, 0.0)]
    [InlineData(0.80, 0.0)]
    [InlineData(0.25, 2.0)]
    [InlineData(0.00, 4.0)]
    public void EmptinessCost_FallsLinearlyToNothingAtTheThreshold(double fill, double expected)
        => Assert.Equal(expected, ConstraintLayoutPolicy.Balanced.GetEmptinessCost(fill), 9);

    [Fact]
    public void NoLayoutAtTheSmallerScale_SendsTheSearchBack()
    {
        var search = new ConstraintScaleSearch();

        Assert.Equal(ConstraintScaleDecision.LookAhead, search.Offer(0, isLast: false, cost: 1.5));
        Assert.Equal(ConstraintScaleDecision.GoBack, search.Offer(1, isLast: false, cost: null));
    }

    [Fact]
    public void NoSmallerScaleLeft_TheBestSoFarIsTaken()
    {
        var search = new ConstraintScaleSearch();

        Assert.Equal(ConstraintScaleDecision.Select, search.Offer(0, isLast: true, cost: 1.5));
    }

    [Fact]
    public void UntilThePlannerHasALayout_TheCurrentPlannerDecides()
    {
        var search = new ConstraintScaleSearch();

        Assert.Equal(ConstraintScaleDecision.NoLayout, search.Offer(0, isLast: false, cost: null));
        Assert.Equal(ConstraintScaleDecision.NoLayout, search.Offer(1, isLast: false, cost: null));
        Assert.Equal(ConstraintScaleDecision.Select, search.Offer(2, isLast: false, cost: 0));
    }
}
