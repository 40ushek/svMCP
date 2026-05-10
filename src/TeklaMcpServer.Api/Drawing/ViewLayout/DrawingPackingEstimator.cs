using System.Collections.Generic;
using System.Linq;
using TeklaMcpServer.Api.Algorithms.Packing;
using TeklaMcpServer.Api.Drawing;

namespace TeklaMcpServer.Api.Drawing.ViewLayout;

internal static class DrawingPackingEstimator
{
    public sealed class RelaxedPackingResult
    {
        public bool Fits { get; set; }
        public string Order { get; set; } = string.Empty;
        public MaxRectsHeuristic Heuristic { get; set; }
        public int Attempts { get; set; }
        public int FrameCount { get; set; }
        public int ReservedAreaCount { get; set; }
        public double AvailableWidth { get; set; }
        public double AvailableHeight { get; set; }
    }

    public static bool FitsShelfPacking(IReadOnlyList<(double w, double h)> frames, double availableWidth, double availableHeight, double gap)
    {
        if (availableWidth <= 0 || availableHeight <= 0)
            return false;

        double curX = 0;
        double curY = availableHeight;
        double rowH = 0;

        foreach (var (w, h) in frames.OrderByDescending(f => f.h))
        {
            if (curX + w > availableWidth && curX > 0)
            {
                curX = 0;
                curY -= rowH + gap;
                rowH = 0;
            }

            if (w > availableWidth || curY - h < 0)
                return false;

            curX += w + gap;
            if (h > rowH)
                rowH = h;
        }

        return true;
    }

    public static RelaxedPackingResult CheckRelaxedMaxRectsFit(
        IReadOnlyList<(double w, double h)> frames,
        double sheetWidth,
        double sheetHeight,
        double margin,
        double gap,
        IReadOnlyList<ReservedRect> reservedAreas)
    {
        var availableWidth = sheetWidth - (2 * margin);
        var availableHeight = sheetHeight - (2 * margin);
        var result = new RelaxedPackingResult
        {
            FrameCount = frames.Count,
            ReservedAreaCount = reservedAreas.Count,
            AvailableWidth = availableWidth,
            AvailableHeight = availableHeight
        };

        if (availableWidth <= 0 || availableHeight <= 0)
            return result;

        var orders = CreatePackingOrders(frames);
        var heuristics = new[]
        {
            MaxRectsHeuristic.BestAreaFit,
            MaxRectsHeuristic.BestShortSideFit,
            MaxRectsHeuristic.BestLongSideFit
        };

        foreach (var order in orders)
        {
            foreach (var heuristic in heuristics)
            {
                result.Attempts++;
                if (!FitsMaxRectsOrdered(
                        order.Frames,
                        availableWidth,
                        availableHeight,
                        gap,
                        heuristic,
                        ToBlockedRectangles(reservedAreas, sheetWidth, sheetHeight, margin, gap)))
                {
                    continue;
                }

                result.Fits = true;
                result.Order = order.Name;
                result.Heuristic = heuristic;
                return result;
            }
        }

        return result;
    }

    public static bool FitsMaxRects(IReadOnlyList<(double w, double h)> frames, double availableWidth, double availableHeight, double gap, MaxRectsHeuristic heuristic)
    {
        if (availableWidth <= 0 || availableHeight <= 0)
            return false;

        // Inflate items by gap and expand bin by the same gap so outer edges do not lose usable area.
        var packer = new MaxRectsBinPacker(availableWidth + gap, availableHeight + gap, allowRotation: false);
        foreach (var (w, h) in frames.OrderByDescending(f => f.w * f.h))
        {
            if (!packer.TryInsert(w + gap, h + gap, heuristic, out _))
                return false;
        }

        return true;
    }

    private static bool FitsMaxRectsOrdered(
        IReadOnlyList<(double w, double h)> frames,
        double availableWidth,
        double availableHeight,
        double gap,
        MaxRectsHeuristic heuristic,
        IEnumerable<PackedRectangle> blockedRectangles)
    {
        var packer = new MaxRectsBinPacker(
            availableWidth + gap,
            availableHeight + gap,
            allowRotation: false,
            blockedRectangles: blockedRectangles);

        foreach (var (w, h) in frames)
        {
            if (!packer.TryInsert(w + gap, h + gap, heuristic, out _))
                return false;
        }

        return true;
    }

    private static IReadOnlyList<(string Name, IReadOnlyList<(double w, double h)> Frames)> CreatePackingOrders(
        IReadOnlyList<(double w, double h)> frames)
        => new[]
        {
            (Name: "area-desc", Frames: (IReadOnlyList<(double w, double h)>)frames.OrderByDescending(f => f.w * f.h).ToList()),
            (Name: "width-desc", Frames: frames.OrderByDescending(f => f.w).ToList()),
            (Name: "height-desc", Frames: frames.OrderByDescending(f => f.h).ToList()),
            (Name: "input-order", Frames: frames.ToList())
        };

    private static IEnumerable<PackedRectangle> ToBlockedRectangles(
        IReadOnlyList<ReservedRect> reservedAreas,
        double sheetWidth,
        double sheetHeight,
        double margin,
        double gap)
    {
        foreach (var area in reservedAreas)
        {
            var minX = System.Math.Max(margin, area.MinX - gap);
            var maxX = System.Math.Min(sheetWidth - margin, area.MaxX + gap);
            var minY = System.Math.Max(margin, area.MinY - gap);
            var maxY = System.Math.Min(sheetHeight - margin, area.MaxY + gap);

            if (maxX <= minX || maxY <= minY)
                continue;

            yield return new PackedRectangle(
                minX - margin,
                (sheetHeight - margin) - maxY,
                maxX - minX,
                maxY - minY);
        }
    }
}

