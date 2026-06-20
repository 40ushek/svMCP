using System;
using System.Collections.Generic;
using TeklaMcpServer.Api.Algorithms.Packing;

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
            return false;
        }

        sheetRect = frame.PackerRectToSheet(placement.X, placement.Y, placement.Width, placement.Height);
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
            return false;
        }

        sheetRect = frame.PackerRectToSheet(placement.X, placement.Y, placement.Width, placement.Height);
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
}
