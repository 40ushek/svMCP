using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing.ViewLayout;

internal static class ViewGroupCenteringGeometry
{
    internal static bool TryFindCenteringDelta(
        IReadOnlyList<ReservedRect> rects,
        double usableMin,
        double usableMax,
        IReadOnlyList<ReservedRect> reserved,
        bool horizontal,
        out double delta)
    {
        delta = 0;
        if (rects.Count == 0)
            return false;

        var groupMin = horizontal ? rects.Min(r => r.MinX) : rects.Min(r => r.MinY);
        var groupMax = horizontal ? rects.Max(r => r.MaxX) : rects.Max(r => r.MaxY);
        var groupSize = groupMax - groupMin;

        var targetMin = usableMin + (usableMax - usableMin - groupSize) / 2.0;
        var desired = targetMin - groupMin;

        desired = desired < 0
            ? System.Math.Max(desired, usableMin - groupMin)
            : System.Math.Min(desired, usableMax - groupMax);

        if (System.Math.Abs(desired) < 1.0)
            return false;

        double lo = 0;
        double hi = System.Math.Abs(desired);
        var sign = System.Math.Sign(desired);
        while (hi - lo > 0.5)
        {
            var mid = (lo + hi) / 2.0;
            var feasible = true;
            foreach (var r in rects)
            {
                var shifted = horizontal
                    ? new ReservedRect(r.MinX + sign * mid, r.MinY, r.MaxX + sign * mid, r.MaxY)
                    : new ReservedRect(r.MinX, r.MinY + sign * mid, r.MaxX, r.MaxY + sign * mid);
                foreach (var res in reserved)
                {
                    if (shifted.MinX < res.MaxX && shifted.MaxX > res.MinX &&
                        shifted.MinY < res.MaxY && shifted.MaxY > res.MinY)
                    {
                        feasible = false;
                        break;
                    }
                }

                if (!feasible)
                    break;
            }

            if (feasible) lo = mid; else hi = mid;
        }

        if (lo < 1.0)
            return false;

        delta = sign * lo;
        return true;
    }

    internal static List<ReservedRect> ShiftRects(IReadOnlyList<ReservedRect> rects, double dx, double dy)
        => rects.Select(r => new ReservedRect(r.MinX + dx, r.MinY + dy, r.MaxX + dx, r.MaxY + dy)).ToList();
}
