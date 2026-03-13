using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;

namespace TeklaMcpServer.Api.Drawing;

internal static class DrawingReservedAreaReader
{
    private const double MinObstacleSize = 1.0;
    private const double FullSheetCoverageRatio = 0.95;

    public static IReadOnlyList<ReservedRect> Read(
        Tekla.Structures.Drawing.Drawing drawing,
        double margin,
        double titleBlockHeight,
        IReadOnlyCollection<int>? excludeViewIds = null)
    {
        var reserved = new List<ReservedRect>();
        var size = drawing.Layout.SheetSize;
        var usableMinX = margin;
        var usableMinY = margin;
        var usableMaxX = size.Width - margin;
        var usableMaxY = size.Height - margin;

        if (usableMaxX <= usableMinX || usableMaxY <= usableMinY)
            return reserved;

        if (titleBlockHeight > 0)
        {
            var manualTop = Clamp(usableMinY + titleBlockHeight, usableMinY, usableMaxY);
            if (manualTop - usableMinY >= MinObstacleSize)
                reserved.Add(new ReservedRect(usableMinX, usableMinY, usableMaxX, manualTop));
        }

        var sheet = drawing.GetSheet();
        var sheetId = sheet.GetIdentifier().ID;
        var objects = sheet.GetAllObjects();
        while (objects.MoveNext())
        {
            if (objects.Current is not DrawingObject drawingObject)
                continue;

            var owner = drawingObject.GetView();
            if (owner == null || owner.GetIdentifier().ID != sheetId)
                continue;

            if (drawingObject is ViewBase)
            {
                if (drawingObject is not View contentView)
                    continue;
                if (excludeViewIds != null && excludeViewIds.Contains(contentView.GetIdentifier().ID))
                    continue;
            }

            if (drawingObject is not IAxisAlignedBoundingBox bounded)
                continue;

            var box = bounded.GetAxisAlignedBoundingBox();
            if (box == null)
                continue;

            var minX = Clamp(box.MinPoint.X, usableMinX, usableMaxX);
            var minY = Clamp(box.MinPoint.Y, usableMinY, usableMaxY);
            var maxX = Clamp(box.MaxPoint.X, usableMinX, usableMaxX);
            var maxY = Clamp(box.MaxPoint.Y, usableMinY, usableMaxY);

            if (maxX - minX < MinObstacleSize || maxY - minY < MinObstacleSize)
                continue;

            var widthRatio = (maxX - minX) / (usableMaxX - usableMinX);
            var heightRatio = (maxY - minY) / (usableMaxY - usableMinY);
            if (widthRatio >= FullSheetCoverageRatio && heightRatio >= FullSheetCoverageRatio)
                continue;

            reserved.Add(new ReservedRect(minX, minY, maxX, maxY));
        }

        return MergeOverlaps(reserved);
    }

    private static IReadOnlyList<ReservedRect> MergeOverlaps(List<ReservedRect> source)
    {
        if (source.Count <= 1)
            return source;

        var pending = source
            .OrderBy(r => r.MinX)
            .ThenBy(r => r.MinY)
            .ToList();

        var merged = new List<ReservedRect>();
        foreach (var rect in pending)
        {
            var current = rect;
            var mergedAny = true;
            while (mergedAny)
            {
                mergedAny = false;
                for (var i = merged.Count - 1; i >= 0; i--)
                {
                    if (!IntersectsOrTouches(current, merged[i]))
                        continue;

                    current = new ReservedRect(
                        System.Math.Min(current.MinX, merged[i].MinX),
                        System.Math.Min(current.MinY, merged[i].MinY),
                        System.Math.Max(current.MaxX, merged[i].MaxX),
                        System.Math.Max(current.MaxY, merged[i].MaxY));
                    merged.RemoveAt(i);
                    mergedAny = true;
                }
            }

            merged.Add(current);
        }

        return merged;
    }

    private static bool IntersectsOrTouches(ReservedRect left, ReservedRect right)
    {
        return left.MinX <= right.MaxX
            && left.MaxX >= right.MinX
            && left.MinY <= right.MaxY
            && left.MaxY >= right.MinY;
    }

    private static double Clamp(double value, double min, double max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }
}
