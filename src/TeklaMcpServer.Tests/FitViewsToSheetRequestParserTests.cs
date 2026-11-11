using TeklaMcpServer.Api.Drawing;
using TeklaMcpServer.Api.Drawing.ViewLayout;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class FitViewsToSheetRequestParserTests
{
    [Fact]
    public void WithoutAPlannerToken_TheCurrentPlannerIsUsed()
    {
        var request = DrawingCommandParsers.ParseFitViewsToSheetRequest(["fit_views_to_sheet", "auto", "4", "0"]);

        Assert.Equal(DrawingLayoutPlannerMode.Current, request.PlannerMode);
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
            ["fit_views_to_sheet", "auto", "4", "0"]);

        Assert.Equal(DrawingScalePolicy.PreserveExistingScales, kept.ScalePolicy);
        Assert.Equal(DrawingScalePolicy.UniformAllNonDetail, rescaled.ScalePolicy);
        Assert.Equal(DrawingScalePolicy.UniformAllNonDetail, noPlanner.ScalePolicy);
    }
}
