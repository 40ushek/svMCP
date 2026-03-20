using System.Collections.Generic;
using System.Linq;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using TeklaMcpServer.Api.Algorithms.Packing;
using TeklaMcpServer.Api.Diagnostics;

namespace TeklaMcpServer.Api.Drawing;

public sealed class FrontViewDrawingArrangeStrategy : IDrawingViewArrangeStrategy
{
    internal const double RelaxedLayoutScaleCutoff = 50.0;

    private readonly GaDrawingMaxRectsArrangeStrategy _maxRectsFallback = new();
    private readonly ShelfPackingDrawingArrangeStrategy _fallback = new();

    public bool CanArrange(DrawingArrangeContext context)
        => context.Views.Any(v => v.ViewType == View.ViewTypes.FrontView);

    public bool EstimateFit(DrawingArrangeContext context, IReadOnlyList<(double w, double h)> frames)
    {
        var plan   = TryCreatePlan(context, out _);
        var maxr   = !plan && _maxRectsFallback.EstimateFit(context, frames);
        var shelf  = !plan && !maxr && context.ReservedAreas.Count == 0 && _fallback.EstimateFit(context, frames);
        return plan || maxr || shelf;
    }

    public List<ArrangedView> Arrange(DrawingArrangeContext context)
    {
        if (TryCreatePlan(context, out var planned))
        {
            PerfTrace.Write("api-view", "front_arrange_plan", 0, $"mode=custom views={planned.Count}");
            return ApplyPlan(planned);
        }

        var frames = context.Views.Select(v => (DrawingArrangeContextSizing.GetWidth(context, v), DrawingArrangeContextSizing.GetHeight(context, v))).ToList();
        if (_maxRectsFallback.EstimateFit(context, frames))
        {
            PerfTrace.Write("api-view", "front_arrange_plan", 0, "mode=maxrects-fallback");
            return _maxRectsFallback.Arrange(context);
        }

        PerfTrace.Write("api-view", "front_arrange_plan", 0, "mode=shelf-fallback");
        return _fallback.Arrange(context);
    }

    internal static bool ShouldPreferRelaxedLayout(double scale)
        => scale >= RelaxedLayoutScaleCutoff;

    internal static (double minX, double maxX, double minY, double maxY) ComputeFreeArea(DrawingArrangeContext context)
        => ComputeFreeArea(context.SheetWidth, context.SheetHeight, context.Margin, context.Gap, context.ReservedAreas);

    internal static (double minX, double maxX, double minY, double maxY) ComputeFreeArea(
        double sheetWidth,
        double sheetHeight,
        double margin,
        double gap,
        IReadOnlyList<ReservedRect> reservedAreas)
    {
        double minX = margin;
        double maxX = sheetWidth - margin;
        double minY = margin;
        double maxY = sheetHeight - margin;

        const double edgeTol = 5.0;
        const double edgeCoverageRatio = 0.75;
        var usableWidth = System.Math.Max(sheetWidth - 2 * margin, 1);
        var usableHeight = System.Math.Max(sheetHeight - 2 * margin, 1);
        foreach (var area in reservedAreas)
        {
            var widthRatio = area.Width / usableWidth;
            var heightRatio = area.Height / usableHeight;

            if (area.MinX <= margin + edgeTol && heightRatio >= edgeCoverageRatio)
                minX = System.Math.Max(minX, area.MaxX + gap);
            if (area.MaxX >= sheetWidth - margin - edgeTol && heightRatio >= edgeCoverageRatio)
                maxX = System.Math.Min(maxX, area.MinX - gap);
            if (area.MinY <= margin + edgeTol && widthRatio >= edgeCoverageRatio)
                minY = System.Math.Max(minY, area.MaxY + gap);
            if (area.MaxY >= sheetHeight - margin - edgeTol && widthRatio >= edgeCoverageRatio)
                maxY = System.Math.Min(maxY, area.MinY - gap);
        }

        if (maxX <= minX) { minX = margin; maxX = sheetWidth - margin; }
        if (maxY <= minY) { minY = margin; maxY = sheetHeight - margin; }

        return (minX, maxX, minY, maxY);
    }

    internal static bool TryPackSupplementalViews(
        IReadOnlyList<(double width, double height)> viewSizes,
        double freeMinX,
        double freeMaxX,
        double freeMinY,
        double freeMaxY,
        double gap,
        ReservedRect blockedRect,
        out List<(double centerX, double centerY)> placements)
    {
        placements = new List<(double centerX, double centerY)>(viewSizes.Count);
        if (viewSizes.Count == 0)
            return true;

        var availableW = freeMaxX - freeMinX;
        var availableH = freeMaxY - freeMinY;
        if (availableW <= 0 || availableH <= 0)
            return false;

        var blocked = new List<PackedRectangle>();
        var blockedMinX = System.Math.Max(freeMinX, blockedRect.MinX - gap);
        var blockedMaxX = System.Math.Min(freeMaxX, blockedRect.MaxX + gap);
        var blockedMinY = System.Math.Max(freeMinY, blockedRect.MinY - gap);
        var blockedMaxY = System.Math.Min(freeMaxY, blockedRect.MaxY + gap);
        if (blockedMaxX > blockedMinX && blockedMaxY > blockedMinY)
        {
            blocked.Add(new PackedRectangle(
                blockedMinX - freeMinX,
                freeMaxY - blockedMaxY,
                blockedMaxX - blockedMinX,
                blockedMaxY - blockedMinY));
        }

        var packer = new MaxRectsBinPacker(availableW + gap, availableH + gap, allowRotation: false, blockedRectangles: blocked);
        var ordered = viewSizes
            .Select((size, index) => (size.width, size.height, index))
            .OrderByDescending(x => x.width * x.height)
            .ToList();
        var resolved = new (double centerX, double centerY)[viewSizes.Count];

        foreach (var item in ordered)
        {
            if (!packer.TryInsert(item.width + gap, item.height + gap, MaxRectsHeuristic.BestAreaFit, out var placement))
                return false;

            resolved[item.index] = (
                freeMinX + placement.X + item.width / 2.0,
                freeMaxY - placement.Y - item.height / 2.0);
        }

        placements.AddRange(resolved);
        return true;
    }

    private static List<ArrangedView> ApplyPlan(List<(View View, double X, double Y)> planned)
    {
        var arranged = new List<ArrangedView>(planned.Count);
        foreach (var item in planned)
        {
            var origin = item.View.Origin;
            origin.X = item.X;
            origin.Y = item.Y;
            item.View.Origin = origin;
            item.View.Modify();
            arranged.Add(new ArrangedView
            {
                Id = item.View.GetIdentifier().ID,
                ViewType = item.View.ViewType.ToString(),
                OriginX = item.X,
                OriginY = item.Y
            });
        }

        return arranged;
    }

    private bool TryCreatePlan(DrawingArrangeContext context, out List<(View View, double X, double Y)> planned)
    {
        planned = new List<(View View, double X, double Y)>();

        var front = context.Views.FirstOrDefault(v => v.ViewType == View.ViewTypes.FrontView);
        if (front == null)
            return false;

        var top = context.Views.FirstOrDefault(v => v.ViewType == View.ViewTypes.TopView);
        var bottom = context.Views.FirstOrDefault(v => v.ViewType == View.ViewTypes.BottomView);
        var back = context.Views.FirstOrDefault(v => v.ViewType == View.ViewTypes.BackView);
        var primarySection = context.Views
            .Where(v => v.ViewType == View.ViewTypes.SectionView)
            .OrderByDescending(v => v.Width * v.Height)
            .FirstOrDefault();
        var secondaryViews = context.Views
            .Where(v => v != front && v != top && v != bottom && v != back && v != primarySection)
            .ToList();

        var scale = GetCurrentScale(context);
        if (!ShouldPreferRelaxedLayout(scale))
        {
            if (TryPlanStrictLayout(context, front, top, bottom, back, primarySection, secondaryViews, out planned))
                return true;
            PerfTrace.Write("api-view", "front_arrange_try", 0, "mode=strict result=failed");
        }

        if (TryPlanRelaxedLayout(context, front, top, bottom, back, primarySection, secondaryViews, out planned))
            return true;
        PerfTrace.Write("api-view", "front_arrange_try", 0, "mode=relaxed result=failed");

        var strictRetry = TryPlanStrictLayout(context, front, top, bottom, back, primarySection, secondaryViews, out planned);
        PerfTrace.Write("api-view", "front_arrange_try", 0, $"mode=strict-retry result={(strictRetry ? "ok" : "failed")}");
        return strictRetry;
    }

    private static double GetCurrentScale(DrawingArrangeContext context)
        => context.Views.Select(v => v.Attributes.Scale).FirstOrDefault(s => s > 0);

    private bool TryPlanStrictLayout(
        DrawingArrangeContext context,
        View front,
        View? top,
        View? bottom,
        View? back,
        View? primarySection,
        IReadOnlyList<View> secondaryViews,
        out List<(View View, double X, double Y)> planned)
    {
        planned = new List<(View View, double X, double Y)>();

        var gap = context.Gap;
        var blocked = NormalizeReservedAreas(context);
        var freeArea = ComputeFreeArea(context);
        var frontWidth = DrawingArrangeContextSizing.GetWidth(context, front);
        var frontHeight = DrawingArrangeContextSizing.GetHeight(context, front);
        var topWidth = top != null ? DrawingArrangeContextSizing.GetWidth(context, top) : 0;
        var topHeight = top != null ? DrawingArrangeContextSizing.GetHeight(context, top) : 0;
        var bottomWidth = bottom != null ? DrawingArrangeContextSizing.GetWidth(context, bottom) : 0;
        var bottomHeight = bottom != null ? DrawingArrangeContextSizing.GetHeight(context, bottom) : 0;
        var backWidth = back != null ? DrawingArrangeContextSizing.GetWidth(context, back) : 0;
        var backHeight = back != null ? DrawingArrangeContextSizing.GetHeight(context, back) : 0;
        var sectionWidth = primarySection != null ? DrawingArrangeContextSizing.GetWidth(context, primarySection) : 0;
        var sectionHeight = primarySection != null ? DrawingArrangeContextSizing.GetHeight(context, primarySection) : 0;
        PerfTrace.Write(
            "api-view",
            "front_arrange_strict_input",
            0,
            $"free=({freeArea.minX:F2},{freeArea.maxX:F2},{freeArea.minY:F2},{freeArea.maxY:F2}) front=({frontWidth:F2},{frontHeight:F2}) top=({topWidth:F2},{topHeight:F2}) section=({sectionWidth:F2},{sectionHeight:F2}) back=({backWidth:F2},{backHeight:F2}) bottom=({bottomWidth:F2},{bottomHeight:F2})");

        var leftSlotW = back != null ? backWidth + gap : 0;
        var rightSlotW = primarySection != null ? sectionWidth + gap : 0;
        var topSlotH = top != null ? topHeight + gap : 0;
        var bottomSlotH = bottom != null ? bottomHeight + gap : 0;

        if (!TryFindFrontViewRect(
                frontWidth,
                frontHeight,
                freeArea.minX + leftSlotW,
                freeArea.maxX - rightSlotW,
                freeArea.minY + bottomSlotH,
                freeArea.maxY - topSlotH,
                blocked,
                out var frontRect))
            return false;

        planned.Add((front, CenterX(frontRect), CenterY(frontRect)));

        var occupied = new List<ReservedRect>(blocked) { frontRect };

        if (top != null)
        {
            var rect = new ReservedRect(
                CenterX(frontRect) - topWidth / 2.0,
                frontRect.MaxY + gap,
                CenterX(frontRect) + topWidth / 2.0,
                frontRect.MaxY + gap + topHeight);
            if (!IsWithinArea(rect, freeArea.minX, freeArea.maxX, freeArea.minY, freeArea.maxY) || IntersectsAny(rect, occupied))
                return false;

            planned.Add((top, CenterX(rect), CenterY(rect)));
            occupied.Add(rect);
        }

        if (bottom != null)
        {
            var rect = new ReservedRect(
                CenterX(frontRect) - bottomWidth / 2.0,
                frontRect.MinY - gap - bottomHeight,
                CenterX(frontRect) + bottomWidth / 2.0,
                frontRect.MinY - gap);
            if (!IsWithinArea(rect, freeArea.minX, freeArea.maxX, freeArea.minY, freeArea.maxY) || IntersectsAny(rect, occupied))
                return false;

            planned.Add((bottom, CenterX(rect), CenterY(rect)));
            occupied.Add(rect);
        }

        if (back != null)
        {
            var rect = new ReservedRect(
                frontRect.MinX - gap - backWidth,
                CenterY(frontRect) - backHeight / 2.0,
                frontRect.MinX - gap,
                CenterY(frontRect) + backHeight / 2.0);
            if (!IsWithinArea(rect, freeArea.minX, freeArea.maxX, freeArea.minY, freeArea.maxY) || IntersectsAny(rect, occupied))
                return false;

            planned.Add((back, CenterX(rect), CenterY(rect)));
            occupied.Add(rect);
        }

        if (primarySection != null)
        {
            var rect = new ReservedRect(
                frontRect.MaxX + gap,
                CenterY(frontRect) - sectionHeight / 2.0,
                frontRect.MaxX + gap + sectionWidth,
                CenterY(frontRect) + sectionHeight / 2.0);
            if (!IsWithinArea(rect, freeArea.minX, freeArea.maxX, freeArea.minY, freeArea.maxY) || IntersectsAny(rect, occupied))
                return false;

            planned.Add((primarySection, CenterX(rect), CenterY(rect)));
            occupied.Add(rect);
        }

        return TryPackSecondaryViews(context, secondaryViews, occupied, planned);
    }

    private bool TryPlanRelaxedLayout(
        DrawingArrangeContext context,
        View front,
        View? top,
        View? bottom,
        View? back,
        View? primarySection,
        IReadOnlyList<View> secondaryViews,
        out List<(View View, double X, double Y)> planned)
    {
        planned = new List<(View View, double X, double Y)>();

        var blocked = NormalizeReservedAreas(context);
        var (freeMinX, freeMaxX, freeMinY, freeMaxY) = ComputeFreeArea(context);
        var frontWidth = DrawingArrangeContextSizing.GetWidth(context, front);
        var frontHeight = DrawingArrangeContextSizing.GetHeight(context, front);

        // Try to reserve right space for section view before placing front.
        // Without this, front occupies the right side and the section ends up packed to the
        // left edge (outside the sheet boundary).
        var relaxedSectionW = primarySection != null ? DrawingArrangeContextSizing.GetWidth(context, primarySection) + context.Gap : 0;
        ReservedRect frontRect;
        if (relaxedSectionW > 0 &&
            TryFindFrontViewRect(frontWidth, frontHeight, freeMinX, freeMaxX - relaxedSectionW, freeMinY, freeMaxY, blocked, out frontRect))
        {
            // found a position that leaves right slot for section — use it
        }
        else if (!TryFindFrontViewRect(frontWidth, frontHeight, freeMinX, freeMaxX, freeMinY, freeMaxY, blocked, out frontRect))
        {
            return false;
        }

        planned.Add((front, CenterX(frontRect), CenterY(frontRect)));
        var occupied = new List<ReservedRect>(blocked) { frontRect };

        var deferred = new List<View>(secondaryViews);

        if (top != null)
        {
            if (TryPlaceRelative(context, top, frontRect, freeMinX, freeMaxX, freeMinY, freeMaxY, context.Gap, occupied, RelativePlacement.Top, out var rect))
            {
                planned.Add((top, CenterX(rect), CenterY(rect)));
                occupied.Add(rect);
            }
            else
            {
                deferred.Add(top);
            }
        }

        if (primarySection != null)
        {
            if (TryPlaceRelative(context, primarySection, frontRect, freeMinX, freeMaxX, freeMinY, freeMaxY, context.Gap, occupied, RelativePlacement.Right, out var rect))
            {
                planned.Add((primarySection, CenterX(rect), CenterY(rect)));
                occupied.Add(rect);
            }
            else
            {
                deferred.Add(primarySection);
            }
        }

        if (bottom != null)
        {
            if (TryPlaceRelative(context, bottom, frontRect, freeMinX, freeMaxX, freeMinY, freeMaxY, context.Gap, occupied, RelativePlacement.Bottom, out var rect))
            {
                planned.Add((bottom, CenterX(rect), CenterY(rect)));
                occupied.Add(rect);
            }
            else
            {
                deferred.Add(bottom);
            }
        }

        if (back != null)
        {
            if (TryPlaceRelative(context, back, frontRect, freeMinX, freeMaxX, freeMinY, freeMaxY, context.Gap, occupied, RelativePlacement.Left, out var rect))
            {
                planned.Add((back, CenterX(rect), CenterY(rect)));
                occupied.Add(rect);
            }
            else
            {
                deferred.Add(back);
            }
        }

        return TryPackSecondaryViews(context, deferred, occupied, planned);
    }

    private static bool TryPackSecondaryViews(
        DrawingArrangeContext context,
        IReadOnlyList<View> views,
        IReadOnlyList<ReservedRect> occupied,
        List<(View View, double X, double Y)> planned)
    {
        if (views.Count == 0)
            return true;

        var (freeMinX, freeMaxX, freeMinY, freeMaxY) = ComputeFreeArea(context);
        var availableW = freeMaxX - freeMinX;
        var availableH = freeMaxY - freeMinY;
        if (availableW <= 0 || availableH <= 0)
            return false;

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
                return false;

            planned.Add((
                view,
                freeMinX + placement.X + width / 2.0,
                freeMaxY - placement.Y - height / 2.0));
        }

        return true;
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
    {
        foreach (var candidate in EnumerateRelativeCandidates(context, view, anchor, freeMinX, freeMaxX, freeMinY, freeMaxY, gap, preferred))
        {
            if (IntersectsAny(candidate, occupied))
                continue;

            placement = candidate;
            return true;
        }

        placement = new ReservedRect(0, 0, 0, 0);
        return false;
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
    {
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

    /// <summary>
    /// Tries to place a view of the given size within [minX..maxX, minY..maxY] without
    /// overlapping any blocked area. Tries 9 positions (center, right, left) × (center, top, bottom)
    /// in order of visual preference, returning the first non-overlapping placement.
    /// </summary>
    internal static bool TryFindFrontViewRect(
        double width,
        double height,
        double minX,
        double maxX,
        double minY,
        double maxY,
        IReadOnlyList<ReservedRect> blocked,
        out ReservedRect placement)
    {
        if (width <= 0 || height <= 0 || maxX - minX < width || maxY - minY < height)
        {
            placement = new ReservedRect(0, 0, 0, 0);
            return false;
        }

        var cx = minX + (maxX - minX - width) / 2.0;
        var cy = minY + (maxY - minY - height) / 2.0;
        var rx = maxX - width;
        var lx = minX;
        var ty = maxY - height;
        var by = minY;

        // Try center first (most balanced), then shifts to avoid reserved corners
        var candidates = new[]
        {
            new ReservedRect(cx, cy, cx + width, cy + height),
            new ReservedRect(rx, cy, rx + width, cy + height),
            new ReservedRect(lx, cy, lx + width, cy + height),
            new ReservedRect(cx, ty, cx + width, ty + height),
            new ReservedRect(rx, ty, rx + width, ty + height),
            new ReservedRect(lx, ty, lx + width, ty + height),
            new ReservedRect(cx, by, cx + width, by + height),
            new ReservedRect(rx, by, rx + width, by + height),
            new ReservedRect(lx, by, lx + width, by + height),
        };

        foreach (var candidate in candidates)
        {
            if (!IntersectsAny(candidate, blocked))
            {
                placement = candidate;
                return true;
            }
        }

        placement = new ReservedRect(0, 0, 0, 0);
        return false;
    }

    internal static bool TryCreateCenteredRect(
        double width,
        double height,
        double minX,
        double maxX,
        double minY,
        double maxY,
        out ReservedRect placement)
    {
        if (width <= 0 || height <= 0)
        {
            placement = new ReservedRect(0, 0, 0, 0);
            return false;
        }

        var availableW = maxX - minX;
        var availableH = maxY - minY;
        if (availableW < width || availableH < height)
        {
            placement = new ReservedRect(0, 0, 0, 0);
            return false;
        }

        var x = minX + (availableW - width) / 2.0;
        var y = minY + (availableH - height) / 2.0;
        placement = new ReservedRect(x, y, x + width, y + height);
        return true;
    }

    private static bool IntersectsAny(ReservedRect rect, IReadOnlyList<ReservedRect> others)
        => others.Any(other =>
            rect.MinX < other.MaxX &&
            rect.MaxX > other.MinX &&
            rect.MinY < other.MaxY &&
            rect.MaxY > other.MinY);

    private static bool IsWithinArea(ReservedRect rect, double minX, double maxX, double minY, double maxY)
        => rect.MinX >= minX
           && rect.MaxX <= maxX
           && rect.MinY >= minY
           && rect.MaxY <= maxY;

    private static List<ReservedRect> NormalizeReservedAreas(DrawingArrangeContext context)
    {
        var normalized = new List<ReservedRect>(context.ReservedAreas.Count);
        foreach (var area in context.ReservedAreas)
        {
            var minX = System.Math.Max(context.Margin, area.MinX - context.Gap);
            var maxX = System.Math.Min(context.SheetWidth - context.Margin, area.MaxX + context.Gap);
            var minY = System.Math.Max(context.Margin, area.MinY - context.Gap);
            var maxY = System.Math.Min(context.SheetHeight - context.Margin, area.MaxY + context.Gap);

            if (maxX <= minX || maxY <= minY)
                continue;

            normalized.Add(new ReservedRect(minX, minY, maxX, maxY));
        }

        return normalized;
    }

    private static PackedRectangle ToBlockedRectangle(double freeMinX, double freeMaxY, ReservedRect rect)
        => new(
            rect.MinX - freeMinX,
            freeMaxY - rect.MaxY,
            rect.MaxX - rect.MinX,
            rect.MaxY - rect.MinY);

    private static double CenterX(ReservedRect rect) => (rect.MinX + rect.MaxX) / 2.0;

    private static double CenterY(ReservedRect rect) => (rect.MinY + rect.MaxY) / 2.0;

    private enum RelativePlacement
    {
        Top,
        Right,
        Bottom,
        Left
    }
}
