using System.Collections.Generic;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;

namespace TeklaMcpServer.Api.Drawing;

public sealed class DrawingArrangeContext
{
    public DrawingArrangeContext(
        Tekla.Structures.Drawing.Drawing drawing,
        IReadOnlyList<View> views,
        double sheetWidth,
        double sheetHeight,
        double margin,
        double gap,
        IReadOnlyList<ReservedRect>? reservedAreas = null,
        IReadOnlyDictionary<int, (double Width, double Height)>? effectiveFrameSizes = null)
    {
        Drawing = drawing ?? throw new System.ArgumentNullException(nameof(drawing));
        Views = views ?? throw new System.ArgumentNullException(nameof(views));
        SheetWidth = sheetWidth;
        SheetHeight = sheetHeight;
        Margin = margin;
        Gap = gap;
        ReservedAreas = reservedAreas ?? System.Array.Empty<ReservedRect>();
        EffectiveFrameSizes = effectiveFrameSizes ?? new Dictionary<int, (double Width, double Height)>();
    }

    public Tekla.Structures.Drawing.Drawing Drawing { get; }
    public IReadOnlyList<View> Views { get; }
    public double SheetWidth { get; }
    public double SheetHeight { get; }
    public double Margin { get; }
    public double Gap { get; }
    public IReadOnlyList<ReservedRect> ReservedAreas { get; }
    public IReadOnlyDictionary<int, (double Width, double Height)> EffectiveFrameSizes { get; }
}

internal static class DrawingArrangeContextSizing
{
    public static double GetWidth(DrawingArrangeContext context, View view)
        => context.EffectiveFrameSizes.TryGetValue(view.GetIdentifier().ID, out var size) ? size.Width : view.Width;

    public static double GetHeight(DrawingArrangeContext context, View view)
        => context.EffectiveFrameSizes.TryGetValue(view.GetIdentifier().ID, out var size) ? size.Height : view.Height;
}

internal static class DrawingViewSheetGeometry
{
    internal static bool IsBoundingBoxOffsetPlausible(
        double originX,
        double originY,
        double width,
        double height,
        ReservedRect rect)
    {
        if (width <= 0 || height <= 0)
            return true;

        var centerX = (rect.MinX + rect.MaxX) * 0.5;
        var centerY = (rect.MinY + rect.MaxY) * 0.5;
        var maxOffset = System.Math.Max(width, height);
        return System.Math.Abs(centerX - originX) <= maxOffset
            && System.Math.Abs(centerY - originY) <= maxOffset;
    }

    public static bool TryGetCenterOffsetFromOrigin(View view, out double offsetX, out double offsetY)
    {
        offsetX = 0;
        offsetY = 0;

        var origin = view.Origin;
        if (origin == null)
            return false;

        if (!TryGetCenter(view, out var centerX, out var centerY))
            return false;

        offsetX = centerX - origin.X;
        offsetY = centerY - origin.Y;
        return true;
    }

    public static bool TryGetBoundingRect(View view, out ReservedRect rect)
    {
        if (view is IAxisAlignedBoundingBox bounded)
        {
            var box = bounded.GetAxisAlignedBoundingBox();
            if (box != null)
            {
                var candidate = new ReservedRect(box.MinPoint.X, box.MinPoint.Y, box.MaxPoint.X, box.MaxPoint.Y);
                var origin = view.Origin;
                if (origin == null
                    || IsBoundingBoxOffsetPlausible(origin.X, origin.Y, view.Width, view.Height, candidate))
                {
                    rect = candidate;
                    return true;
                }
            }
        }

        var fallbackOrigin = view.Origin;
        if (fallbackOrigin != null)
        {
            var halfWidth = view.Width * 0.5;
            var halfHeight = view.Height * 0.5;
            rect = new ReservedRect(
                fallbackOrigin.X - halfWidth,
                fallbackOrigin.Y - halfHeight,
                fallbackOrigin.X + halfWidth,
                fallbackOrigin.Y + halfHeight);
            return true;
        }

        rect = default;
        return false;
    }

    public static bool TryGetBoundingRectAtOrigin(
        View view,
        double originX,
        double originY,
        double width,
        double height,
        out ReservedRect rect)
    {
        if (TryGetCenterOffsetFromOrigin(view, out var offsetX, out var offsetY))
        {
            var centerX = originX + offsetX;
            var centerY = originY + offsetY;
            rect = new ReservedRect(
                centerX - width * 0.5,
                centerY - height * 0.5,
                centerX + width * 0.5,
                centerY + height * 0.5);
            return true;
        }

        rect = new ReservedRect(
            originX - width * 0.5,
            originY - height * 0.5,
            originX + width * 0.5,
            originY + height * 0.5);
        return true;
    }

    public static bool TryGetCenter(View view, out double centerX, out double centerY)
    {
        if (TryGetBoundingRect(view, out var rect))
        {
            centerX = (rect.MinX + rect.MaxX) * 0.5;
            centerY = (rect.MinY + rect.MaxY) * 0.5;
            return true;
        }

        centerX = 0;
        centerY = 0;
        return false;
    }
}
