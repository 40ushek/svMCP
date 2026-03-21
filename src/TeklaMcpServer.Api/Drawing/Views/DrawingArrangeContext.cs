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

    /// <summary>
    /// Returns true when the bounding box center is close enough to Origin to be trusted
    /// as the current view position. A stale bbox (returned by Tekla after Modify/CommitChanges
    /// before the internal state is updated) can have its center displaced from Origin by many
    /// hundreds of mm — far beyond the view's own dimensions. Rejecting such values prevents
    /// frame-offset corrections from misplacing the view far outside the sheet.
    /// Threshold: max(width, height) — generous enough to allow large frame offsets in views
    /// where the model origin is near an edge of the part, but tight enough to catch stale data.
    /// </summary>
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

    /// <summary>
    /// Builds a dictionary of actual view bounding rects by enumerating sheet.GetAllObjects().
    /// Views obtained via GetAllObjects() return the physical frame bbox (always fresh),
    /// whereas IAxisAlignedBoundingBox on a view from GetViews() may return stale content
    /// bbox after Modify/CommitChanges (e.g. DetailViews with large model-space offsets).
    /// </summary>
    public static IReadOnlyDictionary<int, ReservedRect> BuildActualViewRects(
        Tekla.Structures.Drawing.Drawing drawing)
    {
        var result = new Dictionary<int, ReservedRect>();
        var sheet = drawing.GetSheet();
        var sheetId = sheet.GetIdentifier().ID;
        var objects = sheet.GetAllObjects();
        while (objects.MoveNext())
        {
            if (objects.Current is not View viewObj)
                continue;
            var ownerView = ((DrawingObject)objects.Current).GetView();
            if (ownerView == null || ownerView.GetIdentifier().ID != sheetId)
                continue;
            if (viewObj is not IAxisAlignedBoundingBox bounded)
                continue;
            var box = bounded.GetAxisAlignedBoundingBox();
            if (box != null)
                result[viewObj.GetIdentifier().ID] = new ReservedRect(
                    box.MinPoint.X, box.MinPoint.Y, box.MaxPoint.X, box.MaxPoint.Y);
        }
        return result;
    }

    public static bool TryGetBoundingRect(View view, out ReservedRect rect)
        => TryGetBoundingRect(view, null, out rect);

    public static bool TryGetBoundingRect(
        View view,
        IReadOnlyDictionary<int, ReservedRect>? actualRects,
        out ReservedRect rect)
    {
        // Prefer precomputed actual rects from GetAllObjects() — they are always fresh
        // and reflect the physical frame position, unlike GetAxisAlignedBoundingBox() on
        // views from GetViews() which may be stale after Modify/CommitChanges.
        if (actualRects != null && actualRects.TryGetValue(view.GetIdentifier().ID, out rect))
            return true;

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
        => TryGetCenter(view, null, out centerX, out centerY);

    public static bool TryGetCenter(
        View view,
        IReadOnlyDictionary<int, ReservedRect>? actualRects,
        out double centerX,
        out double centerY)
    {
        if (TryGetBoundingRect(view, actualRects, out var rect))
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
