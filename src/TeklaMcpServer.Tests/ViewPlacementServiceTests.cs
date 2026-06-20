using System;
using System.Collections.Generic;
using TeklaMcpServer.Api.Algorithms.Packing;
using TeklaMcpServer.Api.Drawing;
using TeklaMcpServer.Api.Drawing.ViewLayout;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class ViewPlacementServiceTests
{
    private const double Eps = 1e-9;

    // ── PlacementFrame flip contract ─────────────────────────────────────────

    [Fact]
    public void Frame_RoundTrip_PointIsIdentity()
    {
        var frame = new PlacementFrame(10, 20, 410, 320);

        foreach (var (sx, sy) in new[] { (10.0, 20.0), (410.0, 320.0), (123.4, 250.6), (10.0, 320.0) })
        {
            var (px, py) = frame.ToPacker(sx, sy);
            var (rx, ry) = frame.ToSheet(px, py);
            Assert.Equal(sx, rx, 9);
            Assert.Equal(sy, ry, 9);
        }
    }

    [Fact]
    public void Frame_PackerYZero_IsAtFrameTop_MaxY()
    {
        var frame = new PlacementFrame(0, 0, 100, 200);

        // A sheet point at the top edge (MaxY) must map to packer Y = 0.
        var (_, pyTop) = frame.ToPacker(50, frame.MaxY);
        Assert.Equal(0.0, pyTop, 9);

        // A sheet point at the bottom edge (MinY) maps to packer Y = Height.
        var (_, pyBottom) = frame.ToPacker(50, frame.MinY);
        Assert.Equal(frame.Height, pyBottom, 9);
    }

    [Fact]
    public void Frame_PackerRectToSheet_PlacesTopLeftCorrectly()
    {
        var frame = new PlacementFrame(10, 20, 410, 320);
        // Packer rect at origin (0,0), 40×30: its top sits at sheet MaxY (320),
        // its left at sheet MinX (10), bottom at 320-30=290.
        var rect = frame.PackerRectToSheet(0, 0, 40, 30);
        Assert.Equal(10, rect.MinX, 9);
        Assert.Equal(290, rect.MinY, 9);
        Assert.Equal(50, rect.MaxX, 9);
        Assert.Equal(320, rect.MaxY, 9);
    }

    // ── Equivalence to the old hand-rolled formula ───────────────────────────
    // Models the historical Details.cs path:
    //   bin = usable size, origin (usableMin),
    //   packerTargetX = targetX - usableMinX
    //   packerTargetY = usableMaxY - targetY
    //   candidate = (usableMinX + p.X, usableMaxY - p.Y - h, ... , usableMaxY - p.Y)

    [Fact]
    public void TryPlaceNearPoint_MatchesOldFlipFormula_NoBlockers()
    {
        double usableMinX = 10, usableMinY = 10, usableMaxX = 1179, usableMaxY = 831;
        double w = 109, h = 85;
        double targetX = 738.8, targetY = 651.1;

        // --- old hand-rolled path ---
        var packer = new MaxRectsBinPacker(
            usableMaxX - usableMinX, usableMaxY - usableMinY, allowRotation: false);
        var packerTargetX = targetX - usableMinX;
        var packerTargetY = usableMaxY - targetY;
        Assert.True(packer.TryInsertClosestToPoint(w, h, packerTargetX, packerTargetY, out var p));
        var oldRect = new ReservedRect(
            usableMinX + p.X,
            usableMaxY - p.Y - h,
            usableMinX + p.X + w,
            usableMaxY - p.Y);

        // --- new service path ---
        var frame = new PlacementFrame(usableMinX, usableMinY, usableMaxX, usableMaxY);

        Assert.True(ViewPlacementService.TryPlaceNearPoint(frame, w, h, targetX, targetY,
            Array.Empty<ReservedRect>(), gap: 0, out var newRect));

        Assert.Equal(oldRect.MinX, newRect.MinX, 6);
        Assert.Equal(oldRect.MinY, newRect.MinY, 6);
        Assert.Equal(oldRect.MaxX, newRect.MaxX, 6);
        Assert.Equal(oldRect.MaxY, newRect.MaxY, 6);
    }

    [Fact]
    public void TryPlaceNearPoint_ResultStaysInsideFrame()
    {
        var frame = new PlacementFrame(0, 0, 200, 100);
        Assert.True(ViewPlacementService.TryPlaceNearPoint(frame, 50, 40, 1000, 1000,
            Array.Empty<ReservedRect>(), gap: 0, out var rect));

        Assert.True(rect.MinX >= -Eps);
        Assert.True(rect.MinY >= -Eps);
        Assert.True(rect.MaxX <= 200 + Eps);
        Assert.True(rect.MaxY <= 100 + Eps);
    }

    [Fact]
    public void TryPlaceNearPoint_AvoidsGapExpandedBlocker()
    {
        var frame = new PlacementFrame(0, 0, 200, 100);

        var blocked = new[] { new ReservedRect(0, 0, 100, 100) };

        // Without gap a 50×50 view could sit at x≈100. With gap=10 the blocker
        // is expanded to x≤110, so the placed rect must start at x ≥ 110.
        Assert.True(ViewPlacementService.TryPlaceNearPoint(frame, 50, 50, 0, 50, blocked, gap: 10, out var rect));
        Assert.True(rect.MinX >= 110 - Eps,
            $"expected MinX >= 110 with gap-expanded blocker, got {rect.MinX}");
    }

    [Fact]
    public void TryPlaceNearAnchor_PlacesAtAnchorWhenFree()
    {
        var frame = new PlacementFrame(0, 0, 400, 400);
        // Anchor well inside an empty frame → view centered on the anchor.
        Assert.True(ViewPlacementService.TryPlaceNearAnchor(frame, 40, 30, 200, 200,
            Array.Empty<ReservedRect>(), gap: 0, out var rect));

        var cx = (rect.MinX + rect.MaxX) / 2.0;
        var cy = (rect.MinY + rect.MaxY) / 2.0;
        Assert.Equal(200, cx, 6);
        Assert.Equal(200, cy, 6);
    }

    [Fact]
    public void TryInsertBestArea_FlipMatchesManualFormula()
    {
        // Verify TryInsertBestArea uses the same flip as the hand-rolled
        // BestAreaFit sites: bin origin at frame.Min, packer Y down from MaxY.
        double minX = 10, minY = 10, maxX = 414, maxY = 291;
        double w = 100, h = 80;

        var packer = new MaxRectsBinPacker(maxX - minX, maxY - minY, allowRotation: false);
        Assert.True(packer.TryInsert(w, h, MaxRectsHeuristic.BestAreaFit, out var p));
        var manual = new ReservedRect(
            minX + p.X, maxY - p.Y - h, minX + p.X + w, maxY - p.Y);

        var frame = new PlacementFrame(minX, minY, maxX, maxY);
        Assert.True(ViewPlacementService.TryInsertBestArea(
            frame, w, h, Array.Empty<ReservedRect>(), gap: 0, out var rect));

        Assert.Equal(manual.MinX, rect.MinX, 6);
        Assert.Equal(manual.MinY, rect.MinY, 6);
        Assert.Equal(manual.MaxX, rect.MaxX, 6);
        Assert.Equal(manual.MaxY, rect.MaxY, 6);
    }

    [Fact]
    public void CanFit_TrueWhenItemsFit_FalseWhenOversized()
    {
        var frame = new PlacementFrame(0, 0, 100, 100);

        var noBlock = Array.Empty<ReservedRect>();

        Assert.True(ViewPlacementService.CanFit(frame, new[] { (40.0, 40.0), (40.0, 40.0) }, noBlock, gap: 0));
        Assert.False(ViewPlacementService.CanFit(frame, new[] { (120.0, 40.0) }, noBlock, gap: 0));
    }

    [Fact]
    public void TryPlaceNearPoint_FailsOnDegenerateFrame()
    {
        var frame = new PlacementFrame(0, 0, 0, 0);

        Assert.False(ViewPlacementService.TryPlaceNearPoint(frame, 10, 10, 0, 0,
            Array.Empty<ReservedRect>(), gap: 0, out _));
    }
}
