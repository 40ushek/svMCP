using System;
using System.Collections.Generic;
using System.Globalization;
using TeklaMcpServer.Api.Algorithms.Packing;
using TeklaMcpServer.Api.Diagnostics;

namespace TeklaMcpServer.Api.Drawing.ViewLayout;

/// <summary>
/// Single placement entry point over <see cref="MaxRectsBinPacker"/>. All input
/// and output is in sheet coordinates; packer coordinates never leak out.
///
/// Coordinate flip is owned by <see cref="PlacementFrame"/>.
///
/// Gap contract (roadmap Phase 7): gap is applied ONE way only — blocked
/// rectangles are expanded by gap, the view size is passed raw. Call sites that
/// historically inflated the bin or the item size are migrated to this model
/// (preserving their effective spacing — see roadmap for the 2*gap cases).
/// </summary>
internal static class ViewPlacementService
{
    /// <summary>
    /// Place a w×h view as close as possible to a sheet target point.
    /// Replaces hand-rolled <c>TryInsertClosestToPoint</c> + manual flip.
    /// </summary>
    public static bool TryPlaceNearPoint(
        PlacementFrame frame,
        double width,
        double height,
        double sheetTargetX,
        double sheetTargetY,
        IReadOnlyList<ReservedRect> blocked,
        double gap,
        out ReservedRect sheetRect)
    {
        if (!TryCreatePacker(frame, width, height, blocked, gap, out var packer))
        {
            sheetRect = null!;
            return false;
        }

        var (px, py) = frame.ToPacker(sheetTargetX, sheetTargetY);
        if (!packer.TryInsertClosestToPoint(width, height, px, py, out var placement))
        {
            sheetRect = null!;
            Trace("near-point", frame, width, height, sheetTargetX, sheetTargetY, blocked.Count, gap, null);
            return false;
        }

        sheetRect = frame.PackerRectToSheet(placement.X, placement.Y, placement.Width, placement.Height);
        Trace("near-point", frame, width, height, sheetTargetX, sheetTargetY, blocked.Count, gap, sheetRect);
        return true;
    }

    /// <summary>
    /// Place a w×h view as close as possible to a sheet anchor point.
    /// Replaces hand-rolled <c>TryInsertClosestToAnchor</c> + manual flip.
    /// </summary>
    public static bool TryPlaceNearAnchor(
        PlacementFrame frame,
        double width,
        double height,
        double sheetAnchorX,
        double sheetAnchorY,
        IReadOnlyList<ReservedRect> blocked,
        double gap,
        out ReservedRect sheetRect)
    {
        if (!TryCreatePacker(frame, width, height, blocked, gap, out var packer))
        {
            sheetRect = null!;
            return false;
        }

        var (px, py) = frame.ToPacker(sheetAnchorX, sheetAnchorY);
        if (!packer.TryInsertClosestToAnchor(width, height, px, py, out var placement))
        {
            sheetRect = null!;
            Trace("near-anchor", frame, width, height, sheetAnchorX, sheetAnchorY, blocked.Count, gap, null);
            return false;
        }

        sheetRect = frame.PackerRectToSheet(placement.X, placement.Y, placement.Width, placement.Height);
        Trace("near-anchor", frame, width, height, sheetAnchorX, sheetAnchorY, blocked.Count, gap, sheetRect);
        return true;
    }

    /// <summary>
    /// Place a w×h view using best-area-fit (no target point). Intended to
    /// replace the hand-rolled <c>TryInsert(BestAreaFit)</c> + manual flip in the
    /// area-packing strategies (Ga / Relative / base projected) — not yet wired:
    /// those sites await a live GA/area-fit drawing for origin verification
    /// (roadmap 7.3b blocker).
    /// </summary>
    public static bool TryInsertBestArea(
        PlacementFrame frame,
        double width,
        double height,
        IReadOnlyList<ReservedRect> blocked,
        double gap,
        out ReservedRect sheetRect)
    {
        if (!TryCreatePacker(frame, width, height, blocked, gap, out var packer))
        {
            sheetRect = null!;
            return false;
        }

        if (!packer.TryInsert(width, height, MaxRectsHeuristic.BestAreaFit, out var placement))
        {
            sheetRect = null!;
            Trace("best-area", frame, width, height, null, null, blocked.Count, gap, null);
            return false;
        }

        sheetRect = frame.PackerRectToSheet(placement.X, placement.Y, placement.Width, placement.Height);
        Trace("best-area", frame, width, height, null, null, blocked.Count, gap, sheetRect);
        return true;
    }

    /// <summary>
    /// Pure feasibility probe: can every item be packed into the frame without
    /// overlapping each other or the (gap-expanded) blocked rectangles?
    /// No sheet rects are produced. Replaces ad-hoc estimator packers.
    /// </summary>
    public static bool CanFit(
        PlacementFrame frame,
        IReadOnlyList<(double W, double H)> items,
        IReadOnlyList<ReservedRect> blocked,
        double gap)
    {
        if (frame.Width <= 0 || frame.Height <= 0)
            return items.Count == 0;

        var packer = new MaxRectsBinPacker(
            frame.Width,
            frame.Height,
            allowRotation: false,
            blockedRectangles: ToPackerBlocked(frame, blocked, gap));

        foreach (var (w, h) in items)
        {
            if (w <= 0 || h <= 0)
                continue;
            if (!packer.TryInsert(w, h, MaxRectsHeuristic.BestAreaFit, out _))
                return false;
        }

        return true;
    }

    private static bool TryCreatePacker(
        PlacementFrame frame,
        double width,
        double height,
        IReadOnlyList<ReservedRect> blocked,
        double gap,
        out MaxRectsBinPacker packer)
    {
        packer = null!;
        if (frame.Width <= 0 || frame.Height <= 0 || width <= 0 || height <= 0)
            return false;

        packer = new MaxRectsBinPacker(
            frame.Width,
            frame.Height,
            allowRotation: false,
            blockedRectangles: ToPackerBlocked(frame, blocked, gap));
        return true;
    }

    /// <summary>
    /// Convert sheet-space blocked rects into packer-space rectangles, expanded
    /// by gap on every side. Expansion is the single place gap enters the model.
    /// </summary>
    private static IEnumerable<PackedRectangle> ToPackerBlocked(
        PlacementFrame frame,
        IReadOnlyList<ReservedRect> blocked,
        double gap)
    {
        var g = Math.Max(gap, 0.0);
        foreach (var b in blocked)
        {
            var minX = b.MinX - g;
            var maxX = b.MaxX + g;
            var minY = b.MinY - g;
            var maxY = b.MaxY + g;

            // Packer rect origin is top-left in packer space: use the sheet
            // top edge (maxY) for packer-Y, height/width from the expansion.
            var (packerX, packerY) = frame.ToPacker(minX, maxY);
            yield return new PackedRectangle(packerX, packerY, maxX - minX, maxY - minY);
        }
    }

    /// <summary>
    /// Trace one placement attempt to the shared view-layout log
    /// (C:\temp\svmcp-view-layout.log), same channel as the rest of layout.
    /// target is null for best-area mode; result is null on failure.
    /// </summary>
    private static void Trace(
        string mode,
        PlacementFrame frame,
        double width,
        double height,
        double? targetX,
        double? targetY,
        int blockedCount,
        double gap,
        ReservedRect? result)
    {
        var inv = CultureInfo.InvariantCulture;
        var target = targetX.HasValue && targetY.HasValue
            ? string.Format(inv, "({0:F1},{1:F1})", targetX.Value, targetY.Value)
            : "none";
        var res = result != null
            ? string.Format(inv, "[{0:F1},{1:F1},{2:F1},{3:F1}]",
                result.MinX, result.MinY, result.MaxX, result.MaxY)
            : "fail";
        PerfTrace.Write("api-view", "view_placement", 0, string.Format(
            inv,
            "mode={0} frame=[{1:F1},{2:F1},{3:F1},{4:F1}] size=({5:F1}x{6:F1}) target={7} blockers={8} gap={9:F1} result={10}",
            mode, frame.MinX, frame.MinY, frame.MaxX, frame.MaxY,
            width, height, target, blockedCount, gap, res));
    }
}
