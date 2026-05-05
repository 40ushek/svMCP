using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DrawingLayoutCandidateFactoryTests
{
    [Fact]
    public void FromPlannedViews_BuildsCandidateWithoutRuntimeView()
    {
        var drawing = new DrawingInfo
        {
            Name = "A-001"
        };
        var sheet = new DrawingSheetContext
        {
            Width = 420,
            Height = 297
        };
        var reservedLayout = new DrawingReservedLayoutContext
        {
            Areas =
            [
                new ReservedRect(300, 0, 420, 40)
            ]
        };
        var layoutRect = new ReservedRect(10, 20, 110, 70);

        var candidate = DrawingLayoutCandidateFactory.FromPlannedViews(
            "planned",
            drawing,
            sheet,
            reservedLayout,
            [
                new DrawingLayoutPlannedView
                {
                    Id = 42,
                    ViewType = "FrontView",
                    SemanticKind = "BaseProjected",
                    Name = "front",
                    OriginX = 60,
                    OriginY = 45,
                    Scale = 20,
                    Width = 100,
                    Height = 50,
                    LayoutRect = layoutRect,
                    PreferredPlacementSide = "Top",
                    ActualPlacementSide = "Bottom",
                    PlacementFallbackUsed = true
                }
            ]);

        var view = Assert.Single(candidate.Views);
        Assert.Equal("planned", candidate.Name);
        Assert.Equal(drawing, candidate.Drawing);
        Assert.Equal(sheet, candidate.Sheet);
        Assert.Equal(reservedLayout, candidate.ReservedLayout);
        Assert.Equal(42, view.Id);
        Assert.Equal("FrontView", view.ViewType);
        Assert.Equal("BaseProjected", view.SemanticKind);
        Assert.Equal("front", view.Name);
        Assert.Equal(60, view.OriginX);
        Assert.Equal(45, view.OriginY);
        Assert.Equal(20, view.Scale);
        Assert.Equal(100, view.Width);
        Assert.Equal(50, view.Height);
        Assert.Equal(layoutRect, view.LayoutRect);
        Assert.Equal(10, view.BBoxMinX);
        Assert.Equal(20, view.BBoxMinY);
        Assert.Equal(110, view.BBoxMaxX);
        Assert.Equal(70, view.BBoxMaxY);
        Assert.Equal("Top", view.PreferredPlacementSide);
        Assert.Equal("Bottom", view.ActualPlacementSide);
        Assert.True(view.PlacementFallbackUsed);
    }
}
