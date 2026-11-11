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

    [Fact]
    public void UnderPreferScale_OneViewOffTheAxis_BeatsAStepDown_ButTwoDoNot()
    {
        var price = ConstraintLayoutPolicy.PreferScale.LinkBreakCost;

        // One view off the axis: cheaper than a scale step, taken without looking further.
        Assert.Equal(ConstraintScaleDecision.Select, new ConstraintScaleSearch().Offer(0, isLast: false, cost: price));

        // Two views off the axis: a clean layout one step smaller is better.
        var search = new ConstraintScaleSearch();
        Assert.Equal(ConstraintScaleDecision.LookAhead, search.Offer(0, isLast: false, cost: 2 * price));
        Assert.Equal(ConstraintScaleDecision.Select, search.Offer(1, isLast: false, cost: 0));
    }

    [Fact]
    public void UnderBalanced_OneViewOffTheAxis_LosesToAStepDown()
    {
        var search = new ConstraintScaleSearch();

        Assert.Equal(
            ConstraintScaleDecision.LookAhead,
            search.Offer(0, isLast: false, cost: ConstraintLayoutPolicy.Balanced.LinkBreakCost));
        Assert.Equal(ConstraintScaleDecision.Select, search.Offer(1, isLast: false, cost: 0));
    }

    [Fact]
    public void OnAHalfEmptySheet_ABrokenLinkAtTheLargerScale_BeatsACleanLayoutOnAnEmptierOne()
    {
        // The figures of drawing M.179 under the balanced policy: fill 0.40 at 1:5
        // with one view off the axis, 0.22 at 1:10 with nothing given up.
        var policy = ConstraintLayoutPolicy.Balanced;
        var search = new ConstraintScaleSearch();

        Assert.Equal(
            ConstraintScaleDecision.LookAhead,
            search.Offer(0, isLast: false, cost: policy.LinkBreakCost, emptiness: policy.GetEmptinessCost(0.403)));
        Assert.Equal(
            ConstraintScaleDecision.GoBack,
            search.Offer(1, isLast: false, cost: 0, emptiness: policy.GetEmptinessCost(0.221)));
        Assert.Equal(0, search.BestIndex);
    }

    [Fact]
    public void OnAWellFilledSheet_TheStepDownStillWins()
    {
        var policy = ConstraintLayoutPolicy.Balanced;
        var search = new ConstraintScaleSearch();

        search.Offer(0, isLast: false, cost: policy.LinkBreakCost, emptiness: policy.GetEmptinessCost(0.80));

        Assert.Equal(
            ConstraintScaleDecision.Select,
            search.Offer(1, isLast: false, cost: 0, emptiness: policy.GetEmptinessCost(0.55)));
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
    [InlineData(0.25, 1.0)]
    [InlineData(0.00, 2.0)]
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
