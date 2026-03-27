using System.Collections.Generic;
using System.Reflection;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
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
    internal void GetFallbackPlacementSide_MirrorsPreferredSide(
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
        var view = ViewTestHelper.Create(View.ViewTypes.FrontView, originX: 131.57, originY: 162.19, scale: 25);

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
        var top = ViewTestHelper.Create(View.ViewTypes.FrontView);
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

    [Fact]
    public void ProbeHorizontalSectionCandidate_ReturnsNoValidX_WhenTopSectionCannotShift()
    {
        var section = ViewTestHelper.Create(View.ViewTypes.SectionView, width: 30, height: 20);
        var blocker = new ReservedRect(20, 65, 90, 95);

        var result = BaseProjectedDrawingArrangeStrategy.ProbeHorizontalSectionCandidate(
            section,
            frontRect: new ReservedRect(35, 20, 65, 60),
            anchorRect: new ReservedRect(35, 20, 65, 60),
            placementSide: SectionPlacementSide.Top,
            gap: 5,
            freeMinX: 0,
            freeMaxX: 100,
            freeMinY: 0,
            freeMaxY: 200,
            occupied: new[] { blocker });

        Assert.False(result.Success);
        Assert.Equal("no-valid-x", result.RejectReason);
        Assert.Equal("occupied-intersection", result.ConflictReason);
        Assert.Equal("intersects_reserved_area", result.DiagnosticType);
        Assert.Equal("20.0,65.0,90.0,95.0", result.DiagnosticTarget);
        Assert.Equal(35, result.Rect.MinX, 6);
        Assert.Equal(65, result.Rect.MinY, 6);
        Assert.Equal(65, result.Rect.MaxX, 6);
        Assert.Equal(85, result.Rect.MaxY, 6);
    }

    [Fact]
    public void ProbeHorizontalSectionCandidate_FindsFallbackX_WhenBottomSectionCanShift()
    {
        var section = ViewTestHelper.Create(View.ViewTypes.SectionView, width: 30, height: 20);

        var result = BaseProjectedDrawingArrangeStrategy.ProbeHorizontalSectionCandidate(
            section,
            frontRect: new ReservedRect(35, 40, 65, 80),
            anchorRect: new ReservedRect(35, 40, 65, 80),
            placementSide: SectionPlacementSide.Bottom,
            gap: 5,
            freeMinX: 0,
            freeMaxX: 120,
            freeMinY: 0,
            freeMaxY: 200,
            occupied: new[] { new ReservedRect(40, 15, 60, 35) });

        Assert.True(result.Success);
        Assert.Equal(string.Empty, result.RejectReason);
        Assert.Equal(60, result.Rect.MinX, 6);
        Assert.Equal(15, result.Rect.MinY, 6);
        Assert.Equal(90, result.Rect.MaxX, 6);
        Assert.Equal(35, result.Rect.MaxY, 6);
    }

    [Fact]
    public void ProbeHorizontalSectionCandidate_FindsBlockerDerivedShiftInsidePreferredBand()
    {
        var section = ViewTestHelper.Create(View.ViewTypes.SectionView, width: 30, height: 20, originX: 75, originY: 75);

        var result = BaseProjectedDrawingArrangeStrategy.ProbeHorizontalSectionCandidate(
            section,
            frontRect: new ReservedRect(35, 20, 65, 60),
            anchorRect: new ReservedRect(35, 20, 65, 60),
            placementSide: SectionPlacementSide.Top,
            gap: 5,
            freeMinX: 0,
            freeMaxX: 120,
            freeMinY: 0,
            freeMaxY: 200,
            occupied: new[] { new ReservedRect(35, 65, 70, 85) });

        Assert.True(result.Success);
        Assert.Equal(70, result.Rect.MinX, 6);
        Assert.Equal(100, result.Rect.MaxX, 6);
        Assert.Equal(65, result.Rect.MinY, 6);
        Assert.Equal(85, result.Rect.MaxY, 6);
    }

    [Fact]
    public void ProbeHorizontalSectionCandidate_ReturnsOutOfBoundsX_WhenBandCannotFit()
    {
        var section = ViewTestHelper.Create(View.ViewTypes.SectionView, width: 80, height: 20);

        var result = BaseProjectedDrawingArrangeStrategy.ProbeHorizontalSectionCandidate(
            section,
            frontRect: new ReservedRect(35, 20, 65, 60),
            anchorRect: new ReservedRect(35, 20, 65, 60),
            placementSide: SectionPlacementSide.Top,
            gap: 5,
            freeMinX: 20,
            freeMaxX: 80,
            freeMinY: 0,
            freeMaxY: 200);

        Assert.False(result.Success);
        Assert.Equal("out-of-bounds-x", result.RejectReason);
        Assert.Equal("out-of-bounds", result.ConflictReason);
        Assert.Equal("outside_zone_bounds", result.DiagnosticType);
        Assert.Equal(string.Empty, result.DiagnosticTarget);
        Assert.Equal(0, result.Rect.MinX, 6);
        Assert.Equal(0, result.Rect.MaxX, 6);
        Assert.Equal(0, result.Rect.MinY, 6);
        Assert.Equal(0, result.Rect.MaxY, 6);
    }

    [Fact]
    public void ProbeHorizontalSectionCandidate_ReturnsIntersectsView_WhenProposedRectsBlockAllCandidates()
    {
        var section = ViewTestHelper.Create(View.ViewTypes.SectionView, width: 30, height: 20);

        var result = BaseProjectedDrawingArrangeStrategy.ProbeHorizontalSectionCandidate(
            section,
            frontRect: new ReservedRect(35, 20, 65, 60),
            anchorRect: new ReservedRect(35, 20, 65, 60),
            placementSide: SectionPlacementSide.Top,
            gap: 5,
            freeMinX: 0,
            freeMaxX: 100,
            freeMinY: 0,
            freeMaxY: 200,
            proposed: new[]
            {
                new ReservedRect(35, 65, 65, 85),
                new ReservedRect(0, 65, 30, 85),
                new ReservedRect(70, 65, 100, 85)
            });

        Assert.False(result.Success);
        Assert.Equal("no-valid-x", result.RejectReason);
        Assert.Equal("proposed-intersection", result.ConflictReason);
        Assert.Equal("intersects_view", result.DiagnosticType);
        Assert.Equal(string.Empty, result.DiagnosticTarget);
    }

    [Fact]
    public void ProbeVerticalSectionCandidate_FindsRightPlacement_WhenCandidateFits()
    {
        var section = ViewTestHelper.Create(View.ViewTypes.SectionView, width: 30, height: 20);

        var result = BaseProjectedDrawingArrangeStrategy.ProbeVerticalSectionCandidate(
            section,
            frontRect: new ReservedRect(40, 40, 80, 80),
            anchorRect: new ReservedRect(40, 40, 80, 80),
            placementSide: SectionPlacementSide.Right,
            gap: 5,
            freeMinX: 0,
            freeMaxX: 200,
            freeMinY: 0,
            freeMaxY: 200);

        Assert.True(result.Success);
        Assert.Equal(85, result.Rect.MinX, 6);
        Assert.Equal(50, result.Rect.MinY, 6);
        Assert.Equal(115, result.Rect.MaxX, 6);
        Assert.Equal(70, result.Rect.MaxY, 6);
    }

    [Fact]
    public void ProbeVerticalSectionCandidate_ReturnsOutOfBoundsY_WhenBandCannotFit()
    {
        var section = ViewTestHelper.Create(View.ViewTypes.SectionView, width: 30, height: 50);

        var result = BaseProjectedDrawingArrangeStrategy.ProbeVerticalSectionCandidate(
            section,
            frontRect: new ReservedRect(40, 10, 80, 30),
            anchorRect: new ReservedRect(40, 10, 80, 30),
            placementSide: SectionPlacementSide.Left,
            gap: 5,
            freeMinX: 0,
            freeMaxX: 200,
            freeMinY: 0,
            freeMaxY: 40);

        Assert.False(result.Success);
        Assert.Equal("out-of-bounds-y", result.RejectReason);
        Assert.Equal("out-of-bounds", result.ConflictReason);
        Assert.Equal("outside_zone_bounds", result.DiagnosticType);
        Assert.Equal(string.Empty, result.DiagnosticTarget);
    }

    [Fact]
    public void ProbeVerticalSectionCandidate_ReturnsIntersectsReservedArea_WhenOccupiedBlocksCandidate()
    {
        var section = ViewTestHelper.Create(View.ViewTypes.SectionView, width: 30, height: 20);

        var result = BaseProjectedDrawingArrangeStrategy.ProbeVerticalSectionCandidate(
            section,
            frontRect: new ReservedRect(40, 40, 80, 80),
            anchorRect: new ReservedRect(40, 40, 80, 80),
            placementSide: SectionPlacementSide.Right,
            gap: 5,
            freeMinX: 0,
            freeMaxX: 200,
            freeMinY: 0,
            freeMaxY: 200,
            occupied: new[] { new ReservedRect(90, 45, 120, 75) });

        Assert.False(result.Success);
        Assert.Equal("occupied-intersection", result.ConflictReason);
        Assert.Equal("intersects_reserved_area", result.DiagnosticType);
        Assert.Equal("90.0,45.0,120.0,75.0", result.DiagnosticTarget);
    }

    [Fact]
    public void ProbeVerticalSectionCandidate_ReturnsIntersectsView_WhenProposedRectsBlockCandidate()
    {
        var section = ViewTestHelper.Create(View.ViewTypes.SectionView, width: 30, height: 20);

        var result = BaseProjectedDrawingArrangeStrategy.ProbeVerticalSectionCandidate(
            section,
            frontRect: new ReservedRect(40, 40, 80, 80),
            anchorRect: new ReservedRect(40, 40, 80, 80),
            placementSide: SectionPlacementSide.Left,
            gap: 5,
            freeMinX: 0,
            freeMaxX: 200,
            freeMinY: 0,
            freeMaxY: 200,
            proposed: new[] { new ReservedRect(5, 50, 35, 70) });

        Assert.False(result.Success);
        Assert.Equal("proposed-intersection", result.ConflictReason);
        Assert.Equal("intersects_view", result.DiagnosticType);
        Assert.Equal(string.Empty, result.DiagnosticTarget);
    }

    [Fact]
    public void ProbeBaseRectViability_RejectsDenseTopSlotEvenWhenRawWindowFits()
    {
        var baseView = ViewTestHelper.Create(View.ViewTypes.FrontView, width: 60, height: 40);
        var topSection = ViewTestHelper.Create(View.ViewTypes.SectionView, width: 50, height: 20);
        var neighbors = new NeighborSet(baseView);
        var context = CreateArrangeContext([baseView, topSection], gap: 5);

        var decision = BaseProjectedDrawingArrangeStrategy.ProbeBaseRectViability(
            context,
            neighbors,
            leftSections: System.Array.Empty<View>(),
            rightSections: System.Array.Empty<View>(),
            topSections: [topSection],
            bottomSections: System.Array.Empty<View>(),
            baseRect: new ReservedRect(70, 120, 130, 160),
            freeMinX: 0,
            freeMaxX: 200,
            freeMinY: 0,
            freeMaxY: 220,
            blocked:
            [
                new ReservedRect(20, 165, 180, 195)
            ]);

        Assert.False(decision.IsViable);
        Assert.Equal("no-valid-x", decision.RejectReason);
        Assert.Equal("Top", decision.RejectZone);
        Assert.Equal(topSection, decision.RejectView);
    }

    [Fact]
    public void IsBetterBaseRectViability_PrefersCandidateWithViableTopViewBand()
    {
        var baseView = ViewTestHelper.Create(View.ViewTypes.FrontView, width: 60, height: 40);
        var topView = ViewTestHelper.Create(View.ViewTypes.TopView, width: 60, height: 20);
        var neighbors = new NeighborSet(baseView)
        {
            TopNeighbor = topView
        };
        var context = CreateArrangeContext([baseView, topView], gap: 5);

        var denseDecision = BaseProjectedDrawingArrangeStrategy.ProbeBaseRectViability(
            context,
            neighbors,
            leftSections: System.Array.Empty<View>(),
            rightSections: System.Array.Empty<View>(),
            topSections: System.Array.Empty<View>(),
            bottomSections: System.Array.Empty<View>(),
            baseRect: new ReservedRect(70, 160, 130, 200),
            freeMinX: 0,
            freeMaxX: 200,
            freeMinY: 0,
            freeMaxY: 210);

        var viableDecision = BaseProjectedDrawingArrangeStrategy.ProbeBaseRectViability(
            context,
            neighbors,
            leftSections: System.Array.Empty<View>(),
            rightSections: System.Array.Empty<View>(),
            topSections: System.Array.Empty<View>(),
            bottomSections: System.Array.Empty<View>(),
            baseRect: new ReservedRect(70, 120, 130, 160),
            freeMinX: 0,
            freeMaxX: 200,
            freeMinY: 0,
            freeMaxY: 210);

        Assert.False(denseDecision.IsViable);
        Assert.True(viableDecision.IsViable);
        Assert.Equal(0, denseDecision.StrictNeighborFitCount);
        Assert.Equal(1, viableDecision.StrictNeighborFitCount);
        Assert.True(BaseProjectedDrawingArrangeStrategy.IsBetterBaseRectViability(viableDecision, denseDecision));
    }

    [Fact]
    public void IsBetterBaseRectViability_PrefersCandidateWithViableTopSectionXOverTighterFit()
    {
        var baseView = ViewTestHelper.Create(View.ViewTypes.FrontView, width: 60, height: 40);
        var topSection = ViewTestHelper.Create(View.ViewTypes.SectionView, width: 50, height: 20);
        var neighbors = new NeighborSet(baseView);
        var context = CreateArrangeContext([baseView, topSection], gap: 5);
        var blocked = new[]
        {
            new ReservedRect(20, 165, 180, 195)
        };

        var denseDecision = BaseProjectedDrawingArrangeStrategy.ProbeBaseRectViability(
            context,
            neighbors,
            leftSections: System.Array.Empty<View>(),
            rightSections: System.Array.Empty<View>(),
            topSections: [topSection],
            bottomSections: System.Array.Empty<View>(),
            baseRect: new ReservedRect(70, 120, 130, 160),
            freeMinX: 0,
            freeMaxX: 200,
            freeMinY: 0,
            freeMaxY: 220,
            blocked);

        var viableDecision = BaseProjectedDrawingArrangeStrategy.ProbeBaseRectViability(
            context,
            neighbors,
            leftSections: System.Array.Empty<View>(),
            rightSections: System.Array.Empty<View>(),
            topSections: [topSection],
            bottomSections: System.Array.Empty<View>(),
            baseRect: new ReservedRect(20, 120, 80, 160),
            freeMinX: 0,
            freeMaxX: 200,
            freeMinY: 0,
            freeMaxY: 220,
            blocked);

        Assert.False(denseDecision.IsViable);
        Assert.True(viableDecision.IsViable);
        Assert.Equal(0, denseDecision.PreferredHorizontalStackFitCount);
        Assert.Equal(1, viableDecision.PreferredHorizontalStackFitCount);
        Assert.True(BaseProjectedDrawingArrangeStrategy.IsBetterBaseRectViability(viableDecision, denseDecision));
    }

    [Fact]
    public void TrySelectBaseRectWithBudgets_KeepsGapFromReservedAreaInsideBudgetWindow()
    {
        var baseView = ViewTestHelper.Create(View.ViewTypes.FrontView, width: 40, height: 40);
        var neighbors = new NeighborSet(baseView);
        var reserved = new List<ReservedRect>
        {
            new(90, 70, 110, 110)
        };
        var context = CreateArrangeContext([baseView], gap: 5, reservedAreas: reserved);

        var ok = BaseProjectedDrawingArrangeStrategy.TrySelectBaseRectWithBudgets(
            context,
            neighbors,
            leftSections: System.Array.Empty<View>(),
            rightSections: System.Array.Empty<View>(),
            topSections: System.Array.Empty<View>(),
            bottomSections: System.Array.Empty<View>(),
            blocked: new List<ReservedRect>
            {
                new(85, 65, 115, 115)
            },
            includeRelaxedCandidates: true,
            requireAllStrictNeighborsFit: false,
            out var baseRect,
            out var decision);

        Assert.True(ok);
        Assert.True(decision.IsViable);
        Assert.True(
            baseRect.MaxX <= 85 || baseRect.MinX >= 115 || baseRect.MaxY <= 65 || baseRect.MinY >= 115,
            $"baseRect unexpectedly touches reserved band: [{baseRect.MinX},{baseRect.MinY},{baseRect.MaxX},{baseRect.MaxY}]");
    }

    [Fact]
    public void TrySelectBaseRectWithBudgets_UsesSingleGapAroundReservedAreaInsideWindow()
    {
        var baseView = ViewTestHelper.Create(View.ViewTypes.FrontView, width: 40, height: 40);
        var neighbors = new NeighborSet(baseView);
        var reserved = new List<ReservedRect>
        {
            new(90, 70, 110, 110)
        };
        var context = CreateArrangeContext([baseView], gap: 5, reservedAreas: reserved);

        var ok = BaseProjectedDrawingArrangeStrategy.TrySelectBaseRectWithBudgets(
            context,
            neighbors,
            leftSections: System.Array.Empty<View>(),
            rightSections: System.Array.Empty<View>(),
            topSections: System.Array.Empty<View>(),
            bottomSections: System.Array.Empty<View>(),
            blocked: new List<ReservedRect>
            {
                new(85, 65, 115, 115)
            },
            includeRelaxedCandidates: true,
            requireAllStrictNeighborsFit: false,
            out var baseRect,
            out var decision);

        Assert.True(ok);
        Assert.True(decision.IsViable);
        Assert.False(
            baseRect.MaxX <= 80 || baseRect.MinX >= 120 || baseRect.MaxY <= 60 || baseRect.MinY >= 120,
            $"baseRect unexpectedly kept double gap: [{baseRect.MinX},{baseRect.MinY},{baseRect.MaxX},{baseRect.MaxY}]");
        Assert.True(
            baseRect.MaxX <= 85 || baseRect.MinX >= 115 || baseRect.MaxY <= 65 || baseRect.MinY >= 115,
            $"baseRect violated single-gap reserve: [{baseRect.MinX},{baseRect.MinY},{baseRect.MaxX},{baseRect.MaxY}]");
    }

    [Fact]
    public void TryPlaceDegradedStandardSections_PlacesTopSectionWithoutGenericResidual()
    {
        var baseView = ViewTestHelper.Create(View.ViewTypes.FrontView, width: 60, height: 40);
        var section = ViewTestHelper.Create(View.ViewTypes.SectionView, width: 40, height: 20);
        var context = CreateArrangeContext([baseView, section], gap: 5);
        var occupied = new List<ReservedRect> { new ReservedRect(70, 120, 130, 160) };
        var planned = new List<BaseProjectedDrawingArrangeStrategy.PlannedPlacement>();

        var ok = BaseProjectedDrawingArrangeStrategy.TryPlaceDegradedStandardSections(
            context,
            [section],
            frontRect: new ReservedRect(70, 120, 130, 160),
            preferredAnchorRect: new ReservedRect(70, 120, 130, 160),
            fallbackAnchorRect: new ReservedRect(70, 120, 130, 160),
            preferredPlacementSide: SectionPlacementSide.Top,
            freeMinX: 0,
            freeMaxX: 220,
            freeMinY: 0,
            freeMaxY: 260,
            gap: 5,
            occupied,
            planned);

        Assert.True(ok);
        Assert.Single(planned);
        Assert.Equal("Top", planned[0].PreferredPlacementSide?.ToString());
        Assert.Equal("Top", planned[0].ActualPlacementSide?.ToString());
    }

    [Fact]
    public void TryPlaceDegradedStandardSections_UsesFallbackPlacementSideWhenPreferredBandBlocked()
    {
        var baseView = ViewTestHelper.Create(View.ViewTypes.FrontView, width: 60, height: 40);
        var section = ViewTestHelper.Create(View.ViewTypes.SectionView, width: 40, height: 20);
        var context = CreateArrangeContext([baseView, section], gap: 5);
        var baseRect = new ReservedRect(70, 120, 130, 160);
        var occupied = new List<ReservedRect>
        {
            baseRect,
            new ReservedRect(80, 165, 120, 185)
        };
        var planned = new List<BaseProjectedDrawingArrangeStrategy.PlannedPlacement>();

        var ok = BaseProjectedDrawingArrangeStrategy.TryPlaceDegradedStandardSections(
            context,
            [section],
            frontRect: baseRect,
            preferredAnchorRect: baseRect,
            fallbackAnchorRect: baseRect,
            preferredPlacementSide: SectionPlacementSide.Top,
            freeMinX: 0,
            freeMaxX: 220,
            freeMinY: 0,
            freeMaxY: 260,
            gap: 5,
            occupied,
            planned);

        Assert.True(ok);
        Assert.Single(planned);
        Assert.Equal("Top", planned[0].PreferredPlacementSide?.ToString());
        Assert.Equal("Bottom", planned[0].ActualPlacementSide?.ToString());
    }

    [Fact]
    public void TryPlaceDegradedStandardSections_ReturnsFalseWhenNoDegradedBandFits()
    {
        var baseView = ViewTestHelper.Create(View.ViewTypes.FrontView, width: 60, height: 40);
        var section = ViewTestHelper.Create(View.ViewTypes.SectionView, width: 40, height: 20);
        var context = CreateArrangeContext([baseView, section], gap: 5);
        var baseRect = new ReservedRect(70, 120, 130, 160);
        var occupied = new List<ReservedRect>
        {
            baseRect,
            new ReservedRect(0, 165, 220, 185),
            new ReservedRect(0, 95, 220, 115)
        };
        var planned = new List<BaseProjectedDrawingArrangeStrategy.PlannedPlacement>();

        var ok = BaseProjectedDrawingArrangeStrategy.TryPlaceDegradedStandardSections(
            context,
            [section],
            frontRect: baseRect,
            preferredAnchorRect: baseRect,
            fallbackAnchorRect: baseRect,
            preferredPlacementSide: SectionPlacementSide.Top,
            freeMinX: 0,
            freeMaxX: 220,
            freeMinY: 0,
            freeMaxY: 260,
            gap: 5,
            occupied,
            planned);

        Assert.False(ok);
        Assert.Empty(planned);
    }

    [Theory]
    [InlineData(SectionPlacementSide.Top, 60, 40, 66, 20, 5, true)]
    [InlineData(SectionPlacementSide.Top, 60, 40, 65, 20, 5, false)]
    [InlineData(SectionPlacementSide.Left, 60, 40, 20, 46, 5, true)]
    [InlineData(SectionPlacementSide.Left, 60, 40, 20, 45, 5, false)]
    internal void IsOversizedStandardSection_UsesBaseExtentAlongPlacementAxis(
        SectionPlacementSide placementSide,
        double baseWidth,
        double baseHeight,
        double sectionWidth,
        double sectionHeight,
        double gap,
        bool expected)
    {
        Assert.Equal(
            expected,
            BaseProjectedDrawingArrangeStrategy.IsOversizedStandardSection(
                placementSide,
                baseWidth,
                baseHeight,
                sectionWidth,
                sectionHeight,
                gap));
    }

    [Fact]
    public void ProbeDetailPlacement_PrefersAnchorNearestPreferredBand()
    {
        var decision = BaseProjectedDrawingArrangeStrategy.ProbeDetailPlacement(
            ownerRect: new ReservedRect(40, 40, 80, 80),
            detailWidth: 20,
            detailHeight: 10,
            offset: 10,
            freeMinX: 0,
            freeMaxX: 200,
            freeMinY: 0,
            freeMaxY: 200,
            occupied: new[] { new ReservedRect(40, 40, 80, 80) },
            anchorX: 95,
            anchorY: 95);

        Assert.True(decision.Success);
        Assert.True(decision.PreferredBand);
        Assert.Equal(string.Empty, decision.DegradedReason);
        Assert.Equal(85, decision.Rect.MinX, 6);
        Assert.Equal(90, decision.Rect.MinY, 6);
    }

    [Fact]
    public void ProbeDetailPlacement_ReturnsCrossBandReasonWhenPreferredBandBlocked()
    {
        var decision = BaseProjectedDrawingArrangeStrategy.ProbeDetailPlacement(
            ownerRect: new ReservedRect(40, 40, 80, 80),
            detailWidth: 20,
            detailHeight: 10,
            offset: 10,
            freeMinX: 0,
            freeMaxX: 200,
            freeMinY: 0,
            freeMaxY: 200,
            occupied: new[]
            {
                new ReservedRect(40, 40, 80, 80),
                new ReservedRect(0, 85, 85, 200),
                new ReservedRect(85, 35, 200, 200)
            },
            anchorX: 100,
            anchorY: 100);

        Assert.True(decision.Success);
        Assert.False(decision.PreferredBand);
        Assert.Equal("cross-band", decision.DegradedReason);
    }

    private static Tekla.Structures.Drawing.Drawing CreateDrawing()
    {
#pragma warning disable SYSLIB0050
        return (Tekla.Structures.Drawing.Drawing)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(Tekla.Structures.Drawing.AssemblyDrawing));
#pragma warning restore SYSLIB0050
    }

    private static DrawingArrangeContext CreateArrangeContext(
        IReadOnlyList<View> views,
        double sheetWidth = 200,
        double sheetHeight = 220,
        double margin = 0,
        double gap = 4,
        IReadOnlyList<ReservedRect>? reservedAreas = null)
    {
        var drawing = CreateDrawing();
        var effectiveSizes = new Dictionary<int, (double Width, double Height)>();
        for (var i = 0; i < views.Count; i++)
        {
            effectiveSizes[i + 1] = (views[i].Width, views[i].Height);
            TrySetIdentifier(views[i], i + 1);
        }

        return new DrawingArrangeContext(
            drawing,
            views,
            sheetWidth,
            sheetHeight,
            margin,
            gap,
            reservedAreas,
            effectiveSizes);
    }

    private static void TrySetIdentifier(View view, int id)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        var identifier = view.GetIdentifier();
        if (identifier == null)
        {
            // View was uninitialized — find identifier backing field and create it
            var type = view.GetType();
            while (type != null)
            {
                foreach (var field in type.GetFields(flags))
                {
                    if (!field.FieldType.Name.Contains("Identifier")) continue;
                    try
                    {
#pragma warning disable SYSLIB0050
                        var created = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(field.FieldType);
#pragma warning restore SYSLIB0050
                        field.SetValue(view, created);
                    }
                    catch { }
                }
                type = type.BaseType;
            }
            identifier = view.GetIdentifier();
        }

        if (identifier == null) return;

        var idProperty = identifier.GetType().GetProperty("ID", flags);
        var setter = idProperty?.GetSetMethod(nonPublic: true);
        if (setter != null)
        {
            setter.Invoke(identifier, [id]);
            return;
        }

        foreach (var fieldName in new[] { "<ID>k__BackingField", "m_ID", "m_id", "_id" })
        {
            var field = identifier.GetType().GetField(fieldName, flags);
            if (field != null)
            {
                field.SetValue(identifier, id);
                return;
            }
        }
    }
}
