using System.Collections.Generic;
using Tekla.Structures.Drawing;
using Tekla.Structures.Geometry3d;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class BaseProjectedDrawingArrangeStrategyTests
{
    [Theory]
    [InlineData(60, true)]
    [InlineData(50, true)]
    [InlineData(40, false)]
    public void ShouldPreferRelaxedLayout_UsesScaleCutoff(double scale, bool expected)
    {
        Assert.Equal(expected, BaseProjectedDrawingArrangeStrategy.ShouldPreferRelaxedLayout(scale));
    }

    [Theory]
    [InlineData(SectionPlacementSide.Top, SectionPlacementSide.Bottom)]
    [InlineData(SectionPlacementSide.Bottom, SectionPlacementSide.Top)]
    [InlineData(SectionPlacementSide.Left, SectionPlacementSide.Right)]
    [InlineData(SectionPlacementSide.Right, SectionPlacementSide.Left)]
    [InlineData(SectionPlacementSide.Unknown, SectionPlacementSide.Unknown)]
    public void GetFallbackPlacementSide_MirrorsPreferredSide(
        SectionPlacementSide preferred,
        SectionPlacementSide expected)
    {
        Assert.Equal(expected, BaseProjectedDrawingArrangeStrategy.GetFallbackPlacementSide(preferred));
    }

    [Fact]
    public void ComputeFreeArea_ShrinksForWideEdgeBands()
    {
        var free = BaseProjectedDrawingArrangeStrategy.ComputeFreeArea(
            sheetWidth: 420,
            sheetHeight: 297,
            margin: 10,
            gap: 8,
            reservedAreas: new List<ReservedRect>
            {
                new(5, 5, 410, 82),
                new(10, 280, 410, 297)
            });

        Assert.Equal(10, free.minX, 6);
        Assert.Equal(410, free.maxX, 6);
        Assert.Equal(90, free.minY, 6);
        Assert.Equal(272, free.maxY, 6);
    }

    [Fact]
    public void ComputeFreeArea_DoesNotShrinkForLocalCornerTables()
    {
        var free = BaseProjectedDrawingArrangeStrategy.ComputeFreeArea(
            sheetWidth: 420,
            sheetHeight: 297,
            margin: 10,
            gap: 8,
            reservedAreas: new List<ReservedRect>
            {
                new(5, 5, 220, 82),
                new(380, 280, 420, 297)
            });

        Assert.Equal(10, free.minX, 6);
        Assert.Equal(410, free.maxX, 6);
        Assert.Equal(10, free.minY, 6);
        Assert.Equal(287, free.maxY, 6);
    }

    [Fact]
    public void TryCreateCenteredRect_CentersInsideAvailableBand()
    {
        var ok = BaseProjectedDrawingArrangeStrategy.TryCreateCenteredRect(
            width: 120,
            height: 80,
            minX: 60,
            maxX: 360,
            minY: 90,
            maxY: 250,
            out var rect);

        Assert.True(ok);
        Assert.Equal(150, rect.MinX, 6);
        Assert.Equal(270, rect.MaxX, 6);
        Assert.Equal(130, rect.MinY, 6);
        Assert.Equal(210, rect.MaxY, 6);
    }

    [Fact]
    public void TryCreateCenteredRect_ReturnsFalseWhenBandIsTooSmall()
    {
        var ok = BaseProjectedDrawingArrangeStrategy.TryCreateCenteredRect(
            width: 180,
            height: 90,
            minX: 100,
            maxX: 220,
            minY: 80,
            maxY: 200,
            out _);

        Assert.False(ok);
    }

    [Fact]
    public void TryFindBaseViewWindow_AccountsForAllZoneBudgets()
    {
        var budgets = new BaseProjectedDrawingArrangeStrategy.ZoneBudgets(
            topHeight: 80,
            bottomHeight: 50,
            leftWidth: 60,
            rightWidth: 40);

        var ok = BaseProjectedDrawingArrangeStrategy.TryFindBaseViewWindow(
            freeMinX: 10,
            freeMaxX: 410,
            freeMinY: 20,
            freeMaxY: 320,
            baseWidth: 200,
            baseHeight: 120,
            budgets,
            out var window);

        Assert.True(ok);
        Assert.Equal(70, window.MinX, 6);
        Assert.Equal(370, window.MaxX, 6);
        Assert.Equal(70, window.MinY, 6);
        Assert.Equal(240, window.MaxY, 6);
    }

    [Fact]
    public void TryFindBaseViewWindow_FailsWhenTopBudgetLeavesNoHeight()
    {
        var budgets = new BaseProjectedDrawingArrangeStrategy.ZoneBudgets(
            topHeight: 120,
            bottomHeight: 40,
            leftWidth: 0,
            rightWidth: 0);

        var ok = BaseProjectedDrawingArrangeStrategy.TryFindBaseViewWindow(
            freeMinX: 10,
            freeMaxX: 410,
            freeMinY: 20,
            freeMaxY: 220,
            baseWidth: 200,
            baseHeight: 70,
            budgets,
            out _);

        Assert.False(ok);
    }

    [Fact]
    public void TryFindBaseViewWindow_IgnoresResidualAndDetailByContract()
    {
        var budgets = new BaseProjectedDrawingArrangeStrategy.ZoneBudgets(
            topHeight: 0,
            bottomHeight: 24,
            leftWidth: 36,
            rightWidth: 12);

        var ok = BaseProjectedDrawingArrangeStrategy.TryFindBaseViewWindow(
            freeMinX: 5,
            freeMaxX: 589,
            freeMinY: 5,
            freeMaxY: 415,
            baseWidth: 300,
            baseHeight: 150,
            budgets,
            out var window);

        Assert.True(ok);
        Assert.Equal(41, window.MinX, 6);
        Assert.Equal(577, window.MaxX, 6);
        Assert.Equal(29, window.MinY, 6);
        Assert.Equal(415, window.MaxY, 6);
    }

    [Fact]
    public void TryValidateMainSkeletonSpacing_PassesWhenTopAndRightRespectGap()
    {
        var ok = BaseProjectedDrawingArrangeStrategy.TryValidateMainSkeletonSpacing(
            baseRect: new ReservedRect(100, 100, 200, 200),
            topRect: new ReservedRect(110, 204, 190, 244),
            bottomRect: null,
            leftRect: null,
            rightRect: new ReservedRect(204, 120, 254, 180),
            sheetWidth: 420,
            sheetHeight: 297,
            margin: 10,
            gap: 4,
            reservedAreas: System.Array.Empty<ReservedRect>(),
            out var reason,
            out _,
            out _);

        Assert.True(ok);
        Assert.Equal(string.Empty, reason);
    }

    [Fact]
    public void TryValidateMainSkeletonSpacing_RejectsTopGapViolation()
    {
        var ok = BaseProjectedDrawingArrangeStrategy.TryValidateMainSkeletonSpacing(
            baseRect: new ReservedRect(100, 100, 200, 200),
            topRect: new ReservedRect(110, 202, 190, 242),
            bottomRect: null,
            leftRect: null,
            rightRect: null,
            sheetWidth: 420,
            sheetHeight: 297,
            margin: 10,
            gap: 4,
            reservedAreas: System.Array.Empty<ReservedRect>(),
            out var reason,
            out var role,
            out _);

        Assert.False(ok);
        Assert.Equal("main-skeleton-gap-top", reason);
        Assert.Equal("top", role);
    }

    [Fact]
    public void TryValidateMainSkeletonSpacing_RejectsRightGapViolation()
    {
        var ok = BaseProjectedDrawingArrangeStrategy.TryValidateMainSkeletonSpacing(
            baseRect: new ReservedRect(100, 100, 200, 200),
            topRect: null,
            bottomRect: null,
            leftRect: null,
            rightRect: new ReservedRect(203, 120, 253, 180),
            sheetWidth: 420,
            sheetHeight: 297,
            margin: 10,
            gap: 4,
            reservedAreas: System.Array.Empty<ReservedRect>(),
            out var reason,
            out var role,
            out _);

        Assert.False(ok);
        Assert.Equal("main-skeleton-gap-right", reason);
        Assert.Equal("right", role);
    }

    [Fact]
    public void TryValidateMainSkeletonSpacing_RejectsBottomGapViolation()
    {
        var ok = BaseProjectedDrawingArrangeStrategy.TryValidateMainSkeletonSpacing(
            baseRect: new ReservedRect(100, 100, 200, 200),
            topRect: null,
            bottomRect: new ReservedRect(110, 58, 190, 98),
            leftRect: null,
            rightRect: null,
            sheetWidth: 420,
            sheetHeight: 297,
            margin: 10,
            gap: 4,
            reservedAreas: System.Array.Empty<ReservedRect>(),
            out var reason,
            out var role,
            out _);

        Assert.False(ok);
        Assert.Equal("main-skeleton-gap-bottom", reason);
        Assert.Equal("bottom", role);
    }

    [Fact]
    public void TryValidateMainSkeletonSpacing_RejectsLeftGapViolation()
    {
        var ok = BaseProjectedDrawingArrangeStrategy.TryValidateMainSkeletonSpacing(
            baseRect: new ReservedRect(100, 100, 200, 200),
            topRect: null,
            bottomRect: null,
            leftRect: new ReservedRect(48, 120, 98, 180),
            rightRect: null,
            sheetWidth: 420,
            sheetHeight: 297,
            margin: 10,
            gap: 4,
            reservedAreas: System.Array.Empty<ReservedRect>(),
            out var reason,
            out var role,
            out _);

        Assert.False(ok);
        Assert.Equal("main-skeleton-gap-left", reason);
        Assert.Equal("left", role);
    }

    [Fact]
    public void TryValidateMainSkeletonSpacing_RejectsOutOfSheetPlacement()
    {
        var ok = BaseProjectedDrawingArrangeStrategy.TryValidateMainSkeletonSpacing(
            baseRect: new ReservedRect(100, 100, 200, 200),
            topRect: new ReservedRect(110, 204, 190, 320),
            bottomRect: null,
            leftRect: null,
            rightRect: null,
            sheetWidth: 420,
            sheetHeight: 297,
            margin: 10,
            gap: 4,
            reservedAreas: System.Array.Empty<ReservedRect>(),
            out var reason,
            out var role,
            out _);

        Assert.False(ok);
        Assert.Equal("main-skeleton-out-of-sheet-top", reason);
        Assert.Equal("top", role);
    }

    [Fact]
    public void TryValidateMainSkeletonSpacing_RejectsNeighborOverlap()
    {
        var ok = BaseProjectedDrawingArrangeStrategy.TryValidateMainSkeletonSpacing(
            baseRect: new ReservedRect(100, 100, 200, 200),
            topRect: new ReservedRect(110, 204, 190, 244),
            bottomRect: null,
            leftRect: null,
            rightRect: new ReservedRect(180, 210, 240, 250),
            sheetWidth: 420,
            sheetHeight: 297,
            margin: 10,
            gap: 4,
            reservedAreas: System.Array.Empty<ReservedRect>(),
            out var reason,
            out var role,
            out _);

        Assert.False(ok);
        Assert.Equal("main-skeleton-overlap-right", reason);
        Assert.Equal("right", role);
    }

    [Fact]
    public void TryValidateMainSkeletonSpacing_RejectsReservedOverlap()
    {
        var ok = BaseProjectedDrawingArrangeStrategy.TryValidateMainSkeletonSpacing(
            baseRect: new ReservedRect(100, 100, 200, 200),
            topRect: new ReservedRect(110, 204, 190, 244),
            bottomRect: null,
            leftRect: null,
            rightRect: null,
            sheetWidth: 420,
            sheetHeight: 297,
            margin: 10,
            gap: 4,
            reservedAreas: new List<ReservedRect>
            {
                new(120, 220, 180, 260)
            },
            out var reason,
            out var role,
            out _);

        Assert.False(ok);
        Assert.Equal("main-skeleton-reserved-overlap-top", reason);
        Assert.Equal("top", role);
    }

    [Fact]
    public void TryPackSupplementalViews_PlacesViewsOutsideCoreBounds()
    {
        var packed = BaseProjectedDrawingArrangeStrategy.TryPackSupplementalViews(
            new List<(double width, double height)>
            {
                (40, 25),
                (30, 20)
            },
            freeMinX: 10,
            freeMaxX: 300,
            freeMinY: 10,
            freeMaxY: 200,
            gap: 8,
            blockedRect: new ReservedRect(100, 60, 200, 140),
            out var placements);

        Assert.True(packed);
        Assert.Equal(2, placements.Count);

        var sizes = new List<(double width, double height)>
        {
            (40, 25),
            (30, 20)
        };

        for (var i = 0; i < placements.Count; i++)
        {
            var placement = placements[i];
            var size = sizes[i];
            var minX = placement.centerX - size.width / 2.0;
            var maxX = placement.centerX + size.width / 2.0;
            var minY = placement.centerY - size.height / 2.0;
            var maxY = placement.centerY + size.height / 2.0;

            Assert.True(
                maxX <= 100 || minX >= 200 || maxY <= 60 || minY >= 140,
                $"placement unexpectedly overlaps core bounds at ({placement.centerX}, {placement.centerY})");
        }
    }

    [Fact]
    public void TryPackSupplementalViews_ReturnsFalseWhenNoRoomOutsideCore()
    {
        var packed = BaseProjectedDrawingArrangeStrategy.TryPackSupplementalViews(
            new List<(double width, double height)>
            {
                (40, 40)
            },
            freeMinX: 10,
            freeMaxX: 110,
            freeMinY: 10,
            freeMaxY: 110,
            gap: 8,
            blockedRect: new ReservedRect(20, 20, 100, 100),
            out _);

        Assert.False(packed);
    }

    [Fact]
    public void TryProjectViewLocalPointToSheet_UsesOriginAndScale()
    {
        var view = new View();
        view.Origin = new Point(131.57, 162.19, 0);
        view.Attributes.Scale = 25;

        var ok = BaseProjectedDrawingArrangeStrategy.TryProjectViewLocalPointToSheet(
            view,
            new Point(7565.0, 1549.49, 0),
            out var sheetX,
            out var sheetY);

        Assert.True(ok);
        Assert.Equal(434.17, sheetX, 2);
        Assert.Equal(224.17, sheetY, 2);
    }

    [Fact]
    public void TryDeferMainSkeletonNeighbor_RemovesTopPlacementAndReservation()
    {
        var top = new View();
        var topRect = new ReservedRect(10, 20, 40, 60);
        var bottomRect = new ReservedRect(0, 0, 0, 0);
        var leftRect = new ReservedRect(0, 0, 0, 0);
        var rightRect = new ReservedRect(0, 0, 0, 0);
        var occupied = new List<ReservedRect> { topRect };
        var planned = new List<BaseProjectedDrawingArrangeStrategy.PlannedPlacement>
        {
            new(top, 25, 40)
        };
        var topPlaced = true;
        var bottomPlaced = false;
        var leftPlaced = false;
        var rightPlaced = false;

        var deferred = BaseProjectedDrawingArrangeStrategy.TryDeferMainSkeletonNeighbor(
            role: "top",
            reason: "test",
            top,
            bottom: null,
            leftNeighbor: null,
            rightNeighbor: null,
            ref topPlaced,
            ref bottomPlaced,
            ref leftPlaced,
            ref rightPlaced,
            ref topRect,
            ref bottomRect,
            ref leftRect,
            ref rightRect,
            occupied,
            planned);

        Assert.True(deferred);
        Assert.False(topPlaced);
        Assert.Empty(planned);
        Assert.Empty(occupied);
        Assert.Equal(0, topRect.MinX, 6);
        Assert.Equal(0, topRect.MaxX, 6);
        Assert.Equal(0, topRect.MinY, 6);
        Assert.Equal(0, topRect.MaxY, 6);
    }

    [Fact]
    public void TryDeferMainSkeletonNeighbor_ReturnsFalseWhenRoleIsNotPlaced()
    {
        var topRect = new ReservedRect(10, 20, 40, 60);
        var bottomRect = new ReservedRect(0, 0, 0, 0);
        var leftRect = new ReservedRect(0, 0, 0, 0);
        var rightRect = new ReservedRect(0, 0, 0, 0);
        var occupied = new List<ReservedRect> { topRect };
        var planned = new List<BaseProjectedDrawingArrangeStrategy.PlannedPlacement>();
        var topPlaced = false;
        var bottomPlaced = false;
        var leftPlaced = false;
        var rightPlaced = false;

        var deferred = BaseProjectedDrawingArrangeStrategy.TryDeferMainSkeletonNeighbor(
            role: "top",
            reason: "test",
            top: null,
            bottom: null,
            leftNeighbor: null,
            rightNeighbor: null,
            ref topPlaced,
            ref bottomPlaced,
            ref leftPlaced,
            ref rightPlaced,
            ref topRect,
            ref bottomRect,
            ref leftRect,
            ref rightRect,
            occupied,
            planned);

        Assert.False(deferred);
        Assert.Single(occupied);
        Assert.Empty(planned);
        Assert.Equal(10, topRect.MinX, 6);
        Assert.Equal(60, topRect.MaxY, 6);
    }
}
