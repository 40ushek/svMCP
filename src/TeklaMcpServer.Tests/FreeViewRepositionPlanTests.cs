using System.Collections.Generic;
using Tekla.Structures.Drawing;
using TeklaMcpServer.Api.Drawing;
using TeklaMcpServer.Api.Drawing.ViewLayout;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class FreeViewRepositionPlanTests
{
    [Fact]
    public void ApplyFreeViewRepositionPlan_UpdatesVirtualOriginAndPreservesMetadata()
    {
        var arranged = new List<ArrangedView>
        {
            new()
            {
                Id = 7,
                ViewType = "_3DView",
                OriginX = 10,
                OriginY = 20,
                PreferredPlacementSide = "Top",
                ActualPlacementSide = "Top",
                PlacementFallbackUsed = true,
                LayoutMargin = 12,
                LayoutGap = 5
            }
        };
        var plan = new FreeViewRepositionPlan
        {
            Decisions = new[]
            {
                new FreeViewRepositionDecision
                {
                    ViewId = 7,
                    ViewKind = "Model3D",
                    PlannedOriginX = 100,
                    PlannedOriginY = 200,
                    Reason = "ok"
                }
            }
        };

        TeklaDrawingViewApi.ApplyFreeViewRepositionPlan(
            arranged,
            new List<View>(),
            plan,
            layoutMargin: 10,
            layoutGap: 4);

        var updated = Assert.Single(arranged);
        Assert.Equal(100, updated.OriginX);
        Assert.Equal(200, updated.OriginY);
        Assert.Equal("Top", updated.PreferredPlacementSide);
        Assert.Equal("Top", updated.ActualPlacementSide);
        Assert.True(updated.PlacementFallbackUsed);
        Assert.Equal(12, updated.LayoutMargin);
        Assert.Equal(5, updated.LayoutGap);
    }

    [Fact]
    public void ResolveOriginFromFrameCenter_RoundTripsNonZeroOffset()
    {
        var origin = ViewPlacementGeometryService.ResolveOriginFromFrameCenter(
            frameCenterX: 300,
            frameCenterY: 180,
            frameOffsetX: -25,
            frameOffsetY: 15);

        Assert.Equal(325, origin.X);
        Assert.Equal(165, origin.Y);
        Assert.Equal(300, origin.X - 25);
        Assert.Equal(180, origin.Y + 15);
    }
}
