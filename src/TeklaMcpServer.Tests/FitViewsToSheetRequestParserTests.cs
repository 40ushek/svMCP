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
    [InlineData("planner:preferscale", DrawingLayoutPlannerMode.ConstraintPreferScale)]
    [InlineData("PLANNER:PreferScale", DrawingLayoutPlannerMode.ConstraintPreferScale)]
    public void PlannerToken_SelectsTheConstraintPlanner_AndLeavesTheOtherOptionsAlone(
        string token,
        DrawingLayoutPlannerMode expected)
    {
        var request = DrawingCommandParsers.ParseFitViewsToSheetRequest(
            ["fit_views_to_sheet", "auto", "6", "0", "debugpreview", token]);

        Assert.Equal(expected, request.PlannerMode);
        Assert.Equal(6, request.Gap);
        Assert.Equal(DrawingLayoutApplyMode.DebugPreview, request.ApplyMode);
        Assert.Equal(SecondaryScalePolicy.SameAsMain, request.SecondaryScalePolicy);
    }
}
