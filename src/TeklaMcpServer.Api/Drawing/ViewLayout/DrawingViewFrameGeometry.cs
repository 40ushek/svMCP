using System.Collections.Generic;
using System.Globalization;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using TeklaMcpServer.Api.Diagnostics;
using TeklaMcpServer.Api.Drawing;

namespace TeklaMcpServer.Api.Drawing.ViewLayout;

internal static class DrawingViewFrameGeometry
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

    public static IReadOnlyDictionary<int, ReservedRect> BuildActualViewRects(
        Tekla.Structures.Drawing.Drawing drawing)
    {
        var result = new Dictionary<int, ReservedRect>();
        var sheet = drawing.GetSheet();
        var sheetId = sheet.GetIdentifier().ID;
        var objects = sheet.GetAllObjects(typeof(View));
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
        if (actualRects != null && actualRects.TryGetValue(view.GetIdentifier().ID, out rect))
            return true;

        if (view is IAxisAlignedBoundingBox bounded)
        {
            try
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
            catch (System.NullReferenceException) { }
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

    public static Dictionary<int, (double X, double Y)> TryGetFrameOffsets(
        IReadOnlyList<View> views,
        IReadOnlyDictionary<int, ReservedRect> actualRects)
    {
        var offsets = new Dictionary<int, (double X, double Y)>(views.Count);
        foreach (var view in views)
        {
            var origin = view.Origin;
            var originX = origin?.X ?? 0;
            var originY = origin?.Y ?? 0;

            if (!TryGetCenter(view, actualRects, out var centerX, out var centerY))
            {
                offsets.Clear();
                return offsets;
            }

            var scale = view.Attributes.Scale > 0 ? view.Attributes.Scale : 1.0;
            var offsetSheetX = centerX - originX;
            var offsetSheetY = centerY - originY;
            var offsetStoredX = offsetSheetX * scale;
            var offsetStoredY = offsetSheetY * scale;
            var viewId = view.GetIdentifier().ID;
            offsets[viewId] = (offsetStoredX, offsetStoredY);

            if (PerfTrace.IsActive && actualRects.TryGetValue(viewId, out var rect))
            {
                PerfTrace.Write(
                    "api-view",
                    "view_frame_offset_capture",
                    0,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "view={0} origin=({1:F2},{2:F2}) bbox=[{3:F2},{4:F2},{5:F2},{6:F2}] center=({7:F2},{8:F2}) scale={9:F2} offsetSheet=({10:F2},{11:F2}) offsetStored=({12:F2},{13:F2}) plausible={14}",
                        viewId,
                        originX,
                        originY,
                        rect.MinX,
                        rect.MinY,
                        rect.MaxX,
                        rect.MaxY,
                        centerX,
                        centerY,
                        scale,
                        offsetSheetX,
                        offsetSheetY,
                        offsetStoredX,
                        offsetStoredY,
                        IsBoundingBoxOffsetPlausible(originX, originY, view.Width, view.Height, rect) ? 1 : 0));
            }
        }

        return offsets;
    }

    public static Dictionary<int, (double Width, double Height)> TryGetFrameSizes(
        IReadOnlyList<View> views,
        IReadOnlyDictionary<int, ReservedRect>? actualRects = null)
    {
        var sizes = new Dictionary<int, (double Width, double Height)>(views.Count);
        foreach (var view in views)
        {
            if (!TryGetBoundingRect(view, actualRects, out var rect))
            {
                sizes.Clear();
                return sizes;
            }

            sizes[view.GetIdentifier().ID] = (
                rect.MaxX - rect.MinX,
                rect.MaxY - rect.MinY);
        }

        return sizes;
    }
}
