using System.Collections.Generic;
using TeklaMcpServer.Api.Drawing;
using TeklaMcpServer.Api.Drawing.ViewLayout;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DrawingProjectionAlignmentTests
{
    [Fact]
    public void LocalToSheet_UsesViewOriginAndScale()
    {
        var view = new ProjectionViewState(
            viewId: 10,
            originX: 100,
            originY: 200,
            scale: 50,
            width: 300,
            height: 150,
            frameOffsetSheetX: 0,
            frameOffsetSheetY: 0);

        var point = DrawingProjectionAlignmentMath.LocalToSheet(view, 250, -100);

        Assert.Equal(105, point.X, 6);
        Assert.Equal(198, point.Y, 6);
    }

    [Fact]
    public void GetFrameRect_UsesFrameOffsetAfterPostCorrection()
    {
        var view = new ProjectionViewState(
            viewId: 11,
            originX: 100,
            originY: 200,
            scale: 50,
            width: 80,
            height: 40,
            frameOffsetSheetX: 15,
            frameOffsetSheetY: -10);

        var rect = DrawingProjectionAlignmentMath.GetFrameRect(view);

        Assert.Equal(75, rect.MinX, 6);
        Assert.Equal(170, rect.MinY, 6);
        Assert.Equal(155, rect.MaxX, 6);
        Assert.Equal(210, rect.MaxY, 6);
    }

    [Fact]
    public void TranslateOrigin_KeepsFrameOffsetAndMovesSheetRect()
    {
        var view = new ProjectionViewState(12, 100, 100, 25, 60, 30, 12, 8);
        var moved = DrawingProjectionAlignmentMath.TranslateOrigin(view, 20, -5);
        var rect = DrawingProjectionAlignmentMath.GetFrameRect(moved);

        Assert.Equal(102, rect.MinX, 6);
        Assert.Equal(88, rect.MinY, 6);
        Assert.Equal(162, rect.MaxX, 6);
        Assert.Equal(118, rect.MaxY, 6);
    }

    [Fact]
    public void IntersectsAnyReserved_ReturnsTrueForOverlap()
    {
        var rect = new ProjectionRect(40, 40, 90, 90);
        var reserved = new List<ReservedRect>
        {
            new(0, 0, 20, 20),
            new(80, 80, 120, 120)
        };

        Assert.True(DrawingProjectionAlignmentMath.IntersectsAnyReserved(rect, reserved));
    }

    [Fact]
    public void IntersectsAnyView_ReturnsTrueForOverlap()
    {
        var candidate = new ProjectionRect(40, 40, 90, 90);
        var otherViews = new List<ProjectionViewState>
        {
            new(viewId: 21, originX: 60, originY: 60, scale: 1, width: 30, height: 30, frameOffsetSheetX: 0, frameOffsetSheetY: 0)
        };

        Assert.True(DrawingProjectionAlignmentMath.IntersectsAnyView(candidate, otherViews));
    }

    [Fact]
    public void IntersectsAnyView_ReturnsFalseWhenSeparated()
    {
        var candidate = new ProjectionRect(40, 40, 90, 90);
        var otherViews = new List<ProjectionViewState>
        {
            new(viewId: 22, originX: 150, originY: 150, scale: 1, width: 20, height: 20, frameOffsetSheetX: 0, frameOffsetSheetY: 0)
        };

        Assert.False(DrawingProjectionAlignmentMath.IntersectsAnyView(candidate, otherViews));
    }

    [Fact]
    public void IsWithinUsableArea_ReturnsFalseOutsideSheetMargin()
    {
        var rect = new ProjectionRect(5, 20, 60, 80);

        Assert.False(DrawingProjectionAlignmentMath.IsWithinUsableArea(rect, margin: 10, sheetWidth: 200, sheetHeight: 150));
    }

    [Fact]
    public void TrySelectCommonAxis_PrefersGuidMatch()
    {
        var frontAxes = new[]
        {
            new GridAxisInfo { Guid = "guid-b", Label = "2", Direction = "X", Coordinate = 300 },
            new GridAxisInfo { Guid = "guid-a", Label = "1", Direction = "X", Coordinate = 100 }
        };
        var targetAxes = new[]
        {
            new GridAxisInfo { Guid = "guid-a", Label = "ZZ", Direction = "X", Coordinate = 500 }
        };

        var matched = DrawingProjectionAlignmentMath.TrySelectCommonAxis(frontAxes, targetAxes, "X", out var frontAxis, out var targetAxis);

        Assert.True(matched);
        Assert.Equal("guid-a", frontAxis.Guid);
        Assert.Equal("guid-a", targetAxis.Guid);
    }

    [Fact]
    public void TrySelectCommonAxis_FallsBackToLabelAndDirection()
    {
        var frontAxes = new[]
        {
            new GridAxisInfo { Label = "B", Direction = "Y", Coordinate = 200 }
        };
        var targetAxes = new[]
        {
            new GridAxisInfo { Label = "B", Direction = "Y", Coordinate = 900 }
        };

        var matched = DrawingProjectionAlignmentMath.TrySelectCommonAxis(frontAxes, targetAxes, "Y", out var frontAxis, out var targetAxis);

        Assert.True(matched);
        Assert.Equal("B", frontAxis.Label);
        Assert.Equal("B", targetAxis.Label);
    }

    [Fact]
    public void TrySelectCommonAxis_UsesFirstAxisAfterStableSort()
    {
        var frontAxes = new[]
        {
            new GridAxisInfo { Label = "C", Direction = "X", Coordinate = 300 },
            new GridAxisInfo { Label = "A", Direction = "X", Coordinate = 100 },
            new GridAxisInfo { Label = "B", Direction = "X", Coordinate = 200 }
        };
        var targetAxes = new[]
        {
            new GridAxisInfo { Label = "A", Direction = "X", Coordinate = 700 },
            new GridAxisInfo { Label = "B", Direction = "X", Coordinate = 800 }
        };

        var matched = DrawingProjectionAlignmentMath.TrySelectCommonAxis(frontAxes, targetAxes, "X", out var frontAxis, out var targetAxis);

        Assert.True(matched);
        Assert.Equal("A", frontAxis.Label);
        Assert.Equal("A", targetAxis.Label);
    }

    [Fact]
    public void TrySelectCommonAxis_ReturnsFalseWhenNoMatchExists()
    {
        var frontAxes = new[]
        {
            new GridAxisInfo { Label = "A", Direction = "X", Coordinate = 100 }
        };
        var targetAxes = new[]
        {
            new GridAxisInfo { Label = "B", Direction = "X", Coordinate = 100 }
        };

        var matched = DrawingProjectionAlignmentMath.TrySelectCommonAxis(frontAxes, targetAxes, "X", out _, out _);

        Assert.False(matched);
    }

    [Theory]
    [InlineData(SectionPlacementSide.Left, false, true)]
    [InlineData(SectionPlacementSide.Right, false, true)]
    [InlineData(SectionPlacementSide.Top, true, true)]
    [InlineData(SectionPlacementSide.Bottom, true, true)]
    [InlineData(SectionPlacementSide.Unknown, false, false)]
    internal void TryGetSectionAlignmentAxis_UsesPlacementSideSemantics(
        SectionPlacementSide placementSide,
        bool expectedAlignX,
        bool expectedResolved)
    {
        var resolved = DrawingProjectionAlignmentMath.TryGetSectionAlignmentAxis(placementSide, out var alignX);

        Assert.Equal(expectedResolved, resolved);
        Assert.Equal(expectedAlignX, alignX);
    }

    [Theory]
    [InlineData(NeighborRole.Top, true, true)]
    [InlineData(NeighborRole.Bottom, true, true)]
    [InlineData(NeighborRole.SideLeft, false, true)]
    [InlineData(NeighborRole.SideRight, false, true)]
    [InlineData(NeighborRole.Unknown, false, false)]
    internal void TryGetNeighborAlignmentAxis_UsesNeighborRoleSemantics(
        NeighborRole role,
        bool expectedAlignX,
        bool expectedResolved)
    {
        var resolved = DrawingProjectionAlignmentMath.TryGetNeighborAlignmentAxis(role, out var alignX);

        Assert.Equal(expectedResolved, resolved);
        Assert.Equal(expectedAlignX, alignX);
    }

    [Theory]
    [InlineData(125, true)]
    [InlineData(100, true)]
    [InlineData(60, false)]
    [InlineData(50, false)]
    [InlineData(40, false)]
    public void ShouldSkipProjectionAlignment_UsesScaleCutoff(double scale, bool expected)
    {
        Assert.Equal(expected, TeklaDrawingViewApi.ShouldSkipProjectionAlignment(scale));
    }

    [Fact]
    public void FormatProjectionMoveRejectDecision_IncludesReservedOverlapBlockers()
    {
        var decision = new ProjectionMoveRejectDecision(
            stage: "projection-can-move",
            viewId: 30,
            dx: 5,
            dy: -2,
            candidateRect: new ProjectionRect(10, 20, 40, 60),
            reason: "reserved-overlap",
            blockers: new[]
            {
                new ViewPlacementBlocker(ViewPlacementBlockerKind.ReservedArea, new ReservedRect(12, 22, 44, 66))
            });

        var text = DrawingProjectionAlignmentService.FormatProjectionMoveRejectDecision(decision);

        Assert.Contains("stage=projection-can-move", text);
        Assert.Contains("view=30", text);
        Assert.Contains("reason=reserved-overlap", text);
        Assert.Contains("delta=(5.00,-2.00)", text);
        Assert.Contains("candidate=[10.0,20.0,40.0,60.0]", text);
        Assert.Contains("blockers=[12.0,22.0,44.0,66.0]", text);
    }

    [Fact]
    public void FormatProjectionMoveRejectDecision_IncludesBlockingViewId()
    {
        var decision = new ProjectionMoveRejectDecision(
            stage: "projection-can-move",
            viewId: 31,
            dx: 0,
            dy: 8,
            candidateRect: new ProjectionRect(15, 25, 45, 65),
            reason: "view-overlap",
            blockers: new[]
            {
                new ViewPlacementBlocker(ViewPlacementBlockerKind.View, new ReservedRect(14, 24, 46, 66), 99)
            });

        var text = DrawingProjectionAlignmentService.FormatProjectionMoveRejectDecision(decision);

        Assert.Contains("reason=view-overlap", text);
        Assert.Contains("blockers=99:[14.0,24.0,46.0,66.0]", text);
    }

    [Fact]
    public void FormatProjectionMoveRejectDecision_DoesNotMaskOutOfBoundsAsOverlap()
    {
        var decision = new ProjectionMoveRejectDecision(
            stage: "projection-can-move",
            viewId: 32,
            dx: -12,
            dy: 0,
            candidateRect: new ProjectionRect(2, 20, 22, 40),
            reason: "out-of-bounds");

        var text = DrawingProjectionAlignmentService.FormatProjectionMoveRejectDecision(decision);

        Assert.Contains("reason=out-of-bounds", text);
        Assert.DoesNotContain("reserved-overlap", text);
        Assert.DoesNotContain("view-overlap", text);
    }

    [Fact]
    public void ProjectionRejectDecision_UsesSameValidatorReasonAsEstimateSide()
    {
        var candidate = new ReservedRect(12, 20, 42, 60);
        var validation = ViewPlacementValidator.Validate(
            candidate,
            minX: 0,
            maxX: 100,
            minY: 0,
            maxY: 100,
            reservedAreas: new[] { new ReservedRect(10, 18, 44, 62) },
            otherViewRectsById: null);

        var decision = DrawingProjectionAlignmentService.CreateProjectionMoveRejectDecision(
            "projection-can-move",
            viewId: 33,
            dx: 0,
            dy: 0,
            candidateRect: new ProjectionRect(candidate.MinX, candidate.MinY, candidate.MaxX, candidate.MaxY),
            validation);

        Assert.False(validation.Fits);
        Assert.Equal("reserved-overlap", validation.Reason);
        Assert.Equal(validation.Reason, decision.Reason);
    }

    [Fact]
    public void TryResolveSectionAlignmentAxis_UsesResolvedSideWhenActualPlacementSideMissing()
    {
        var arranged = new ArrangedView
        {
            Id = 44,
            ActualPlacementSide = string.Empty,
            PlacementFallbackUsed = false
        };

        var ok = DrawingProjectionAlignmentService.TryResolveSectionAlignmentAxis(
            arranged,
            SectionPlacementSide.Top,
            out var alignX,
            out var reason);

        Assert.True(ok);
        Assert.True(alignX);
        Assert.Equal(string.Empty, reason);
    }

    [Theory]
    [InlineData("Top", true)]
    [InlineData("Bottom", true)]
    [InlineData("Left", false)]
    [InlineData("Right", false)]
    public void TryResolveSectionAlignmentAxis_UsesActualPlacementSideWhenResolvedSideUnknown(string actualPlacementSide, bool expectedAlignX)
    {
        var arranged = new ArrangedView
        {
            Id = 45,
            ActualPlacementSide = actualPlacementSide,
            PlacementFallbackUsed = true
        };

        var ok = DrawingProjectionAlignmentService.TryResolveSectionAlignmentAxis(
            arranged,
            SectionPlacementSide.Unknown,
            out var alignX,
            out var reason);

        Assert.True(ok);
        Assert.Equal(expectedAlignX, alignX);
        Assert.Equal(string.Empty, reason);
    }

    [Fact]
    public void TryResolveSectionAlignmentAxis_PrefersResolvedSideOverActualPlacementSide()
    {
        var arranged = new ArrangedView
        {
            Id = 46,
            ActualPlacementSide = "Right",
            PlacementFallbackUsed = true
        };

        var ok = DrawingProjectionAlignmentService.TryResolveSectionAlignmentAxis(
            arranged,
            SectionPlacementSide.Top,
            out var alignX,
            out var reason);

        Assert.True(ok);
        Assert.True(alignX);
        Assert.Equal(string.Empty, reason);
    }

    [Fact]
    public void TryResolveSectionAlignmentAxis_FallsBackToResolvedPlacementSideWhenArrangedViewMissing()
    {
        var ok = DrawingProjectionAlignmentService.TryResolveSectionAlignmentAxis(
            arrangedView: null,
            fallbackPlacementSide: SectionPlacementSide.Right,
            out var alignX,
            out var reason);

        Assert.True(ok);
        Assert.False(alignX);
        Assert.Equal(string.Empty, reason);
    }
}
