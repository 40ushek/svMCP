using TeklaMcpServer.Api.Drawing;
using TeklaMcpServer.Api.Drawing.ViewLayout;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class FitViewsToSheetRequestParserTests
{
    [Fact]
    public void WithoutAPlannerToken_TheConstraintPlannerIsUsed_AndScalesAreKept()
    {
        var request = DrawingCommandParsers.ParseFitViewsToSheetRequest(["fit_views_to_sheet", "auto", "4", "0"]);

        Assert.Equal(DrawingLayoutPlannerMode.ConstraintBalanced, request.PlannerMode);
        Assert.False(request.PlannerGiven);
        Assert.Equal(DrawingScalePolicy.PreserveExistingScales, request.ScalePolicy);
    }

    [Theory]
    [InlineData("planner:legacy")]
    [InlineData("planner:current")]
    public void LegacyToken_SelectsTheDeprecatedPlanner_WithItsOwnScaleDefault(string token)
    {
        var request = DrawingCommandParsers.ParseFitViewsToSheetRequest(["fit_views_to_sheet", "auto", "4", "0", token]);

        Assert.Equal(DrawingLayoutPlannerMode.Legacy, request.PlannerMode);
        Assert.True(request.PlannerGiven);
        Assert.Equal(DrawingScalePolicy.UniformAllNonDetail, request.ScalePolicy);
    }

    [Fact]
    public void OnADrawingTheConstraintPlannerIsNotTheDefaultOn_LegacyDefaultsReplaceOnlyWhatWasNotNamed()
    {
        var nothingNamed = DrawingCommandParsers.ParseFitViewsToSheetRequest(["fit_views_to_sheet", "auto", "4", "0"]);
        var scaleNamed = DrawingCommandParsers.ParseFitViewsToSheetRequest(["fit_views_to_sheet", "auto", "4", "0", "keepscale"]);
        var plannerNamed = DrawingCommandParsers.ParseFitViewsToSheetRequest(["fit_views_to_sheet", "auto", "4", "0", "planner:preferscale"]);

        nothingNamed.UseLegacyDefaults();
        scaleNamed.UseLegacyDefaults();
        plannerNamed.UseLegacyDefaults();

        Assert.Equal(DrawingLayoutPlannerMode.Legacy, nothingNamed.PlannerMode);
        Assert.Equal(DrawingScalePolicy.UniformAllNonDetail, nothingNamed.ScalePolicy);
        Assert.Equal(DrawingLayoutPlannerMode.Legacy, scaleNamed.PlannerMode);
        Assert.Equal(DrawingScalePolicy.PreserveExistingScales, scaleNamed.ScalePolicy);
        Assert.Equal(DrawingLayoutPlannerMode.ConstraintPreferScale, plannerNamed.PlannerMode);
        Assert.Equal(DrawingScalePolicy.PreserveExistingScales, plannerNamed.ScalePolicy);
    }

    [Theory]
    [InlineData("planner:keepprojection", DrawingLayoutPlannerMode.ConstraintKeepProjection)]
    [InlineData("planner:balanced", DrawingLayoutPlannerMode.ConstraintBalanced)]
    [InlineData("planner:preferscale", DrawingLayoutPlannerMode.ConstraintPreferScale)]
    [InlineData("PLANNER:PreferScale", DrawingLayoutPlannerMode.ConstraintPreferScale)]
    public void PlannerToken_SelectsTheConstraintPlanner_AndLeavesTheOtherOptionsAlone(
        string token,
        DrawingLayoutPlannerMode expected)
    {
        var request = DrawingCommandParsers.ParseFitViewsToSheetRequest(
            ["fit_views_to_sheet", "auto", "6", "0", "debugpreview", token]);

        Assert.Equal(expected, request.PlannerMode);
        Assert.Equal(DrawingScalePolicy.PreserveExistingScales, request.ScalePolicy);
        Assert.Equal(6, request.Gap);
        Assert.Equal(DrawingLayoutApplyMode.DebugPreview, request.ApplyMode);
        Assert.Equal(SecondaryScalePolicy.SameAsMain, request.SecondaryScalePolicy);
    }

    [Fact]
    public void OppositeSideIsAllowed_UnlessTheTokenForbidsIt()
    {
        var byDefault = DrawingCommandParsers.ParseFitViewsToSheetRequest(["fit_views_to_sheet", "auto", "4", "0"]);
        var forbidden = DrawingCommandParsers.ParseFitViewsToSheetRequest(["fit_views_to_sheet", "auto", "4", "0", "no-opposite-side"]);

        Assert.True(byDefault.PlannerAllowOppositeSide);
        Assert.False(forbidden.PlannerAllowOppositeSide);
    }

    [Fact]
    public void MainViewsStayOnTheAxis_UnlessTheTokenLetsThemSlide()
    {
        var byDefault = DrawingCommandParsers.ParseFitViewsToSheetRequest(["fit_views_to_sheet", "auto", "4", "0"]);
        var allowed = DrawingCommandParsers.ParseFitViewsToSheetRequest(["fit_views_to_sheet", "auto", "4", "0", "allow-main-view-shift"]);

        Assert.False(byDefault.PlannerAllowMainViewShift);
        Assert.True(allowed.PlannerAllowMainViewShift);
    }

    [Fact]
    public void VariantToken_PicksALaterLayout_AndGarbageIsIgnored()
    {
        var chosen = DrawingCommandParsers.ParseFitViewsToSheetRequest(
            ["fit_views_to_sheet", "auto", "4", "0", "planner:balanced", "planner-variant:3"]);
        var garbage = DrawingCommandParsers.ParseFitViewsToSheetRequest(
            ["fit_views_to_sheet", "auto", "4", "0", "planner:balanced", "planner-variant:x"]);

        Assert.Equal(3, chosen.PlannerVariant);
        Assert.Equal(DrawingLayoutPlannerMode.ConstraintBalanced, chosen.PlannerMode);
        Assert.Equal(0, garbage.PlannerVariant);
    }

    [Fact]
    public void WithAPlanner_ScalesAreKept_UnlessAScalePolicyIsNamed()
    {
        var kept = DrawingCommandParsers.ParseFitViewsToSheetRequest(
            ["fit_views_to_sheet", "auto", "4", "0", "planner:balanced"]);
        var rescaled = DrawingCommandParsers.ParseFitViewsToSheetRequest(
            ["fit_views_to_sheet", "auto", "4", "0", "uniformallnondetail", "planner:balanced"]);
        var noPlanner = DrawingCommandParsers.ParseFitViewsToSheetRequest(
            ["fit_views_to_sheet", "auto", "4", "0", "planner:legacy"]);

        Assert.Equal(DrawingScalePolicy.PreserveExistingScales, kept.ScalePolicy);
        Assert.Equal(DrawingScalePolicy.UniformAllNonDetail, rescaled.ScalePolicy);
        Assert.Equal(DrawingScalePolicy.UniformAllNonDetail, noPlanner.ScalePolicy);
    }
}
