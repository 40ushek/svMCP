using System.Collections.Generic;
using System.Linq;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using TeklaMcpServer.Api.Algorithms.Packing;
using TeklaMcpServer.Api.Diagnostics;
using TeklaMcpServer.Api.Drawing;

namespace TeklaMcpServer.Api.Drawing.ViewLayout;

public sealed partial class BaseProjectedDrawingArrangeStrategy
{
    /// <summary>
    /// Tries to place the TopView at the very top of the free area when the standard
    /// above-FrontView slot is unavailable (e.g. FrontView sits high on a tall sheet).
    /// Tries center, left, and right X positions at freeMaxY - height.
    /// </summary>
    private static bool TryFindTopViewAtSheetTop(
        DrawingArrangeContext context,
        View top,
        ViewPlacementSearchArea searchArea,
        IReadOnlyList<ReservedRect> occupied,
        out ReservedRect placement)
    {
        var freeMinX = searchArea.FreeMinX;
        var freeMaxX = searchArea.FreeMaxX;
        var freeMinY = searchArea.FreeMinY;
        var freeMaxY = searchArea.FreeMaxY;
        var width = DrawingArrangeContextSizing.GetWidth(context, top);
        var height = DrawingArrangeContextSizing.GetHeight(context, top);
        var y = freeMaxY - height;
        if (y < freeMinY || freeMaxX - freeMinX < width)
        {
            placement = new ReservedRect(0, 0, 0, 0);
            return false;
        }

        var maxX = freeMaxX - width;
        var cx = freeMinX + (freeMaxX - freeMinX - width) / 2.0;

        foreach (var candidate in EnumerateTopViewAtSheetTopCandidates(freeMinX, freeMaxX, freeMaxY, width, y, cx, maxX))
        {
            if (!IntersectsAny(candidate, occupied))
            {
                placement = candidate;
                return true;
            }
        }

        placement = new ReservedRect(0, 0, 0, 0);
        return false;
    }

    private static void PackSecondaryViewsPartial(
        DrawingArrangeContext context,
        IReadOnlyList<View> views,
        IReadOnlyList<ReservedRect> occupied,
        List<(View View, double X, double Y)> planned)
    {
        if (views.Count == 0)
            return;

        var (freeMinX, freeMaxX, freeMinY, freeMaxY) = ComputeFreeArea(context);
        var availableW = freeMaxX - freeMinX;
        var availableH = freeMaxY - freeMinY;
        if (availableW <= 0 || availableH <= 0)
            return;

        var blocked = occupied
            .Select(rect => ToBlockedRectangle(freeMinX, freeMaxY, rect))
            .ToList();

        var packer = new MaxRectsBinPacker(
            availableW + context.Gap,
            availableH + context.Gap,
            allowRotation: false,
            blockedRectangles: blocked);

        foreach (var view in views.OrderByDescending(v => DrawingArrangeContextSizing.GetWidth(context, v) * DrawingArrangeContextSizing.GetHeight(context, v)))
        {
            var width = DrawingArrangeContextSizing.GetWidth(context, view);
            var height = DrawingArrangeContextSizing.GetHeight(context, view);
            if (!packer.TryInsert(width + context.Gap, height + context.Gap, MaxRectsHeuristic.BestAreaFit, out var placement))
            {
                PerfTrace.Write("api-view", "front_arrange_secondary_skip", 0,
                    $"view={view.GetIdentifier().ID} w={width:F1} h={height:F1}");
                continue;
            }

            planned.Add((
                view,
                freeMinX + placement.X + width / 2.0,
                freeMaxY - placement.Y - height / 2.0));
        }
    }

    private static bool TryPlaceRelative(
        DrawingArrangeContext context,
        View view,
        ReservedRect anchor,
        ViewPlacementSearchArea searchArea,
        double gap,
        IReadOnlyList<ReservedRect> occupied,
        RelativePlacement preferred,
        out ReservedRect placement)
    {
        foreach (var candidate in EnumerateAvailableRelativeCandidates(
                     context,
                     view,
                     anchor,
                     searchArea,
                     gap,
                     occupied,
                     preferred))
        {
            placement = candidate;
            return true;
        }

        placement = new ReservedRect(0, 0, 0, 0);
        return false;
    }

    private static bool TryFindTopViewAtSheetTop(
        DrawingArrangeContext context,
        View top,
        double freeMinX,
        double freeMaxX,
        double freeMinY,
        double freeMaxY,
        IReadOnlyList<ReservedRect> occupied,
        out ReservedRect placement)
        => TryFindTopViewAtSheetTop(
            context,
            top,
            CreateSearchArea(freeMinX, freeMaxX, freeMinY, freeMaxY),
            occupied,
            out placement);

    private static IEnumerable<ReservedRect> EnumerateTopViewAtSheetTopCandidates(
        double freeMinX,
        double freeMaxX,
        double freeMaxY,
        double width,
        double y,
        double centeredMinX,
        double maxX)
    {
        yield return new ReservedRect(
            System.Math.Min(centeredMinX, maxX),
            y,
            System.Math.Min(centeredMinX, maxX) + width,
            freeMaxY);
        yield return new ReservedRect(
            freeMinX,
            y,
            freeMinX + width,
            freeMaxY);
        yield return new ReservedRect(
            System.Math.Max(freeMinX, maxX),
            y,
            System.Math.Max(freeMinX, maxX) + width,
            freeMaxY);
    }

    private static bool TryPlaceRelative(
        DrawingArrangeContext context,
        View view,
        ReservedRect anchor,
        double freeMinX,
        double freeMaxX,
        double freeMinY,
        double freeMaxY,
        double gap,
        IReadOnlyList<ReservedRect> occupied,
        RelativePlacement preferred,
        out ReservedRect placement)
        => TryPlaceRelative(
            context,
            view,
            anchor,
            new ViewPlacementSearchArea(anchor, freeMinX, freeMaxX, freeMinY, freeMaxY),
            gap,
            occupied,
            preferred,
            out placement);

    private static ReservedRect? FindRelativeRect(
        DrawingArrangeContext context,
        View view,
        ReservedRect anchor,
        ViewPlacementSearchArea searchArea,
        double gap,
        IReadOnlyList<ReservedRect> occupied,
        RelativePlacement preferred)
        => TryPlaceRelative(
            context,
            view,
            anchor,
            searchArea,
            gap,
            occupied,
            preferred,
            out var placement)
            ? placement
            : null;

    private static ReservedRect? FindRelativeRect(
        DrawingArrangeContext context,
        View view,
        ReservedRect anchor,
        double freeMinX,
        double freeMaxX,
        double freeMinY,
        double freeMaxY,
        double gap,
        IReadOnlyList<ReservedRect> occupied,
        RelativePlacement preferred)
        => FindRelativeRect(
            context,
            view,
            anchor,
            new ViewPlacementSearchArea(anchor, freeMinX, freeMaxX, freeMinY, freeMaxY),
            gap,
            occupied,
            preferred);

    private static IEnumerable<ReservedRect> EnumerateAvailableRelativeCandidates(
        DrawingArrangeContext context,
        View view,
        ReservedRect anchor,
        ViewPlacementSearchArea searchArea,
        double gap,
        IReadOnlyList<ReservedRect> occupied,
        RelativePlacement preferred)
    {
        foreach (var candidate in EnumerateRelativeCandidates(context, view, anchor, searchArea, gap, preferred))
        {
            if (IntersectsAny(candidate, occupied))
                continue;

            yield return candidate;
        }
    }

    private static IEnumerable<ReservedRect> EnumerateAvailableRelativeCandidates(
        DrawingArrangeContext context,
        View view,
        ReservedRect anchor,
        double freeMinX,
        double freeMaxX,
        double freeMinY,
        double freeMaxY,
        double gap,
        IReadOnlyList<ReservedRect> occupied,
        RelativePlacement preferred)
        => EnumerateAvailableRelativeCandidates(
            context,
            view,
            anchor,
            new ViewPlacementSearchArea(anchor, freeMinX, freeMaxX, freeMinY, freeMaxY),
            gap,
            occupied,
            preferred);

    private static IEnumerable<ReservedRect> EnumerateRelativeCandidates(
        DrawingArrangeContext context,
        View view,
        ReservedRect anchor,
        ViewPlacementSearchArea searchArea,
        double gap,
        RelativePlacement placement)
    {
        var freeMinX = searchArea.FreeMinX;
        var freeMaxX = searchArea.FreeMaxX;
        var freeMinY = searchArea.FreeMinY;
        var freeMaxY = searchArea.FreeMaxY;
        var width = DrawingArrangeContextSizing.GetWidth(context, view);
        var height = DrawingArrangeContextSizing.GetHeight(context, view);
        var maxX = freeMaxX - width;
        var maxY = freeMaxY - height;

        static double Clamp(double value, double min, double max)
            => value < min ? min : (value > max ? max : value);

        IEnumerable<ReservedRect> BuildTop()
        {
            var y = anchor.MaxY + gap;
            if (y > maxY)
                yield break;
            var xs = new[]
            {
                Clamp(CenterX(anchor) - width / 2.0, freeMinX, maxX),
                Clamp(anchor.MinX, freeMinX, maxX),
                Clamp(anchor.MaxX - width, freeMinX, maxX)
            };

            foreach (var x in xs.Distinct())
                yield return new ReservedRect(x, y, x + width, y + height);
        }

        IEnumerable<ReservedRect> BuildBottom()
        {
            var y = anchor.MinY - gap - height;
            if (y < freeMinY)
                yield break;
            var xs = new[]
            {
                Clamp(CenterX(anchor) - width / 2.0, freeMinX, maxX),
                Clamp(anchor.MinX, freeMinX, maxX),
                Clamp(anchor.MaxX - width, freeMinX, maxX)
            };

            foreach (var x in xs.Distinct())
                yield return new ReservedRect(x, y, x + width, y + height);
        }

        IEnumerable<ReservedRect> BuildRight()
        {
            var x = anchor.MaxX + gap;
            if (x > maxX)
                yield break;
            var ys = new[]
            {
                Clamp(CenterY(anchor) - height / 2.0, freeMinY, maxY),
                Clamp(anchor.MinY, freeMinY, maxY),
                Clamp(anchor.MaxY - height, freeMinY, maxY)
            };

            foreach (var y in ys.Distinct())
                yield return new ReservedRect(x, y, x + width, y + height);
        }

        IEnumerable<ReservedRect> BuildLeft()
        {
            var x = anchor.MinX - gap - width;
            if (x < freeMinX)
                yield break;
            var ys = new[]
            {
                Clamp(CenterY(anchor) - height / 2.0, freeMinY, maxY),
                Clamp(anchor.MinY, freeMinY, maxY),
                Clamp(anchor.MaxY - height, freeMinY, maxY)
            };

            foreach (var y in ys.Distinct())
                yield return new ReservedRect(x, y, x + width, y + height);
        }

        return placement switch
        {
            RelativePlacement.Top => BuildTop().Concat(BuildRight()).Concat(BuildLeft()).Concat(BuildBottom()),
            RelativePlacement.Right => BuildRight().Concat(BuildTop()).Concat(BuildBottom()).Concat(BuildLeft()),
            RelativePlacement.Bottom => BuildBottom().Concat(BuildLeft()).Concat(BuildRight()).Concat(BuildTop()),
            RelativePlacement.Left => BuildLeft().Concat(BuildTop()).Concat(BuildBottom()).Concat(BuildRight()),
            _ => System.Array.Empty<ReservedRect>()
        };
    }

    private static IEnumerable<ReservedRect> EnumerateRelativeCandidates(
        DrawingArrangeContext context,
        View view,
        ReservedRect anchor,
        double freeMinX,
        double freeMaxX,
        double freeMinY,
        double freeMaxY,
        double gap,
        RelativePlacement placement)
        => EnumerateRelativeCandidates(
            context,
            view,
            anchor,
            new ViewPlacementSearchArea(anchor, freeMinX, freeMaxX, freeMinY, freeMaxY),
            gap,
            placement);
}

