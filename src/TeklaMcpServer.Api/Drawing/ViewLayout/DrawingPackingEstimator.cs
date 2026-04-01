using System.Collections.Generic;
using System.Linq;
using TeklaMcpServer.Api.Algorithms.Packing;
using TeklaMcpServer.Api.Drawing;

namespace TeklaMcpServer.Api.Drawing.ViewLayout;

internal static class DrawingPackingEstimator
{
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
}

