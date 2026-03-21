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
    public static bool TryGetBoundingRect(View view, out ReservedRect rect)
    {
        if (view is IAxisAlignedBoundingBox bounded)
        {
            var box = bounded.GetAxisAlignedBoundingBox();
            if (box != null)
            {
                rect = new ReservedRect(box.MinPoint.X, box.MinPoint.Y, box.MaxPoint.X, box.MaxPoint.Y);
                return true;
            }
        }

        var origin = view.Origin;
        if (origin != null)
        {
            var halfWidth = view.Width * 0.5;
            var halfHeight = view.Height * 0.5;
            rect = new ReservedRect(
                origin.X - halfWidth,
                origin.Y - halfHeight,
                origin.X + halfWidth,
                origin.Y + halfHeight);
            return true;
        }

        rect = default;
        return false;
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
