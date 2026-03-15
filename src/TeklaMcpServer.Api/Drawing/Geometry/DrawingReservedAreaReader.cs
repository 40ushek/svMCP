using System;
using System.Collections.Generic;
using System.Linq;
using Tekla.Common.Geometry;
using TeklaMcpServer.Api.Diagnostics;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.DrawingPresentationModel;

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

        AddLayoutTableReservedAreas(reserved, usableMinX, usableMinY, usableMaxX, usableMaxY, ReadLayoutTableGeometries());

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
                if (drawingObject is not Tekla.Structures.Drawing.View contentView)
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

    private static void AddLayoutTableReservedAreas(
        List<ReservedRect> reserved,
        double usableMinX,
        double usableMinY,
        double usableMaxX,
        double usableMaxY,
        IReadOnlyList<LayoutTableGeometryInfo> tableGeometries)
    {
        foreach (var table in tableGeometries)
        {
            if (!table.HasGeometry || table.Bounds == null)
                continue;

            var minX = Clamp(table.Bounds.MinX, usableMinX, usableMaxX);
            var minY = Clamp(table.Bounds.MinY, usableMinY, usableMaxY);
            var maxX = Clamp(table.Bounds.MaxX, usableMinX, usableMaxX);
            var maxY = Clamp(table.Bounds.MaxY, usableMinY, usableMaxY);

            if (maxX - minX < MinObstacleSize || maxY - minY < MinObstacleSize)
                continue;

            var widthRatio = (maxX - minX) / (usableMaxX - usableMinX);
            var heightRatio = (maxY - minY) / (usableMaxY - usableMinY);
            if (widthRatio >= FullSheetCoverageRatio && heightRatio >= FullSheetCoverageRatio)
                continue;

            reserved.Add(new ReservedRect(minX, minY, maxX, maxY));
        }
    }

    internal static IReadOnlyList<LayoutTableGeometryInfo> ReadLayoutTableGeometries()
    {
        // Disabled for now: layout-handler calls can block in the bridge runtime context.
        // Returning no table geometries keeps view arrangement responsive and predictable.
        PerfTrace.Write("api-view", "reserved_tables_skip", 0, "reason=disabled_unstable_layout_handler");
        return Array.Empty<LayoutTableGeometryInfo>();
    }

    internal static LayoutTableGeometryInfo BuildLayoutTableGeometryInfo(int tableId, Segment? segment)
    {
        var primitiveCount = CountPrimitives(segment);
        if (!TryGetSegmentBounds(segment, out var bounds))
        {
            return new LayoutTableGeometryInfo
            {
                TableId = tableId,
                PrimitiveCount = primitiveCount,
                HasGeometry = false
            };
        }

        return new LayoutTableGeometryInfo
        {
            TableId = tableId,
            PrimitiveCount = primitiveCount,
            HasGeometry = true,
            Bounds = bounds
        };
    }

    internal static bool TryGetSegmentBounds(Segment? segment, out ReservedRect bounds)
    {
        var acc = new BoundsAccumulator();
        if (segment != null)
            AccumulatePrimitiveBounds(segment, ref acc);

        if (!acc.HasValue)
        {
            bounds = new ReservedRect(0, 0, 0, 0);
            return false;
        }

        bounds = new ReservedRect(acc.MinX, acc.MinY, acc.MaxX, acc.MaxY);
        return true;
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

    private static void AccumulatePrimitiveBounds(PrimitiveBase primitive, ref BoundsAccumulator acc)
    {
        switch (primitive)
        {
            case Segment nestedSegment:
                foreach (var child in nestedSegment.Primitives)
                    AccumulatePrimitiveBounds(child, ref acc);
                return;

            case PrimitiveGroup group:
                foreach (var child in group.Primitives)
                    AccumulatePrimitiveBounds(child, ref acc);
                return;

            case LinePrimitive line:
                Include(ref acc, line.StartPoint);
                Include(ref acc, line.EndPoint);
                return;

            case PathPrimitive path:
                foreach (var segment in path.Segments)
                    AccumulatePathableBounds(segment, ref acc);
                return;

            case LoopPrimitive loop:
                foreach (var segment in loop.Segments)
                    AccumulatePathableBounds(segment, ref acc);
                return;

            case PolygonPrimitive polygon:
                AccumulatePrimitiveBounds(polygon.OuterLoop, ref acc);
                if (polygon.InnerLoops != null)
                {
                    foreach (var inner in polygon.InnerLoops)
                        AccumulatePrimitiveBounds(inner, ref acc);
                }
                return;

            case ArcPrimitive arc:
                IncludeArcBounds(ref acc, arc);
                return;

            case CirclePrimitive circle:
                Include(ref acc, circle.CenterPoint.X - circle.Radius, circle.CenterPoint.Y - circle.Radius);
                Include(ref acc, circle.CenterPoint.X + circle.Radius, circle.CenterPoint.Y + circle.Radius);
                return;

            case PointPrimitive point:
                Include(ref acc, point.Position);
                return;

            case BitmapPrimitive bitmap:
                IncludeRotatedBox(ref acc, bitmap.Position, bitmap.Width, bitmap.Height, bitmap.Angle.Radians);
                return;

            case SymbolPrimitive symbol:
                IncludeRotatedBox(ref acc, symbol.Position, symbol.Width, symbol.Height, symbol.Angle);
                return;

            case TextPrimitive text:
                IncludeEstimatedTextBox(ref acc, text);
                return;
        }
    }

    private static int CountPrimitives(PrimitiveBase? primitive)
    {
        if (primitive == null)
            return 0;

        switch (primitive)
        {
            case Segment segment:
                return segment.Primitives.Sum(CountPrimitives);
            case PrimitiveGroup group:
                return group.Primitives.Sum(CountPrimitives);
            case PolygonPrimitive polygon:
                var count = CountPrimitives(polygon.OuterLoop);
                if (polygon.InnerLoops != null)
                    count += polygon.InnerLoops.Sum(CountPrimitives);
                return count;
            case LoopPrimitive loop:
                return loop.Segments.Sum(CountPathablePrimitives);
            case PathPrimitive path:
                return path.Segments.Sum(CountPathablePrimitives);
            default:
                return 1;
        }
    }

    private static int CountPathablePrimitives(IPathable pathable)
    {
        return pathable switch
        {
            PrimitiveBase primitive => CountPrimitives(primitive),
            _ => 1
        };
    }

    private static void AccumulatePathableBounds(IPathable pathable, ref BoundsAccumulator acc)
    {
        switch (pathable)
        {
            case ArcPrimitive arc:
                IncludeArcBounds(ref acc, arc);
                break;
            case PathPrimitive path:
                foreach (var segment in path.Segments)
                    AccumulatePathableBounds(segment, ref acc);
                break;
            default:
                Include(ref acc, pathable.StartPoint);
                Include(ref acc, pathable.EndPoint);
                break;
        }
    }

    private static void IncludeArcBounds(ref BoundsAccumulator acc, ArcPrimitive arc)
    {
        var geometry = arc.GetArc();
        Include(ref acc, arc.StartPoint);
        Include(ref acc, arc.EndPoint);

        var start = NormalizeRadians(geometry.StartAngle.Radians);
        var end = NormalizeRadians(start + geometry.DeltaAngle.Radians);
        var radius = geometry.Circle.Radius;
        var center = geometry.Circle.Center;

        IncludeArcCardinal(ref acc, center, radius, start, end, 0.0);
        IncludeArcCardinal(ref acc, center, radius, start, end, Math.PI / 2.0);
        IncludeArcCardinal(ref acc, center, radius, start, end, Math.PI);
        IncludeArcCardinal(ref acc, center, radius, start, end, Math.PI * 1.5);
    }

    private static void IncludeArcCardinal(
        ref BoundsAccumulator acc,
        Vector2 center,
        double radius,
        double start,
        double end,
        double angle)
    {
        var normalizedAngle = NormalizeRadians(angle);
        if (!AngleWithinSweep(normalizedAngle, start, end))
            return;

        Include(
            ref acc,
            center.X + radius * Math.Cos(normalizedAngle),
            center.Y + radius * Math.Sin(normalizedAngle));
    }

    private static bool AngleWithinSweep(double angle, double start, double end)
    {
        while (end < start)
            end += Math.PI * 2.0;
        while (angle < start)
            angle += Math.PI * 2.0;

        return angle <= end;
    }

    private static double NormalizeRadians(double angle)
    {
        var full = Math.PI * 2.0;
        angle %= full;
        if (angle < 0)
            angle += full;
        return angle;
    }

    private static void IncludeEstimatedTextBox(ref BoundsAccumulator acc, TextPrimitive text)
    {
        var width = Math.Max(text.Height, text.Text?.Length > 0
            ? text.Text.Length * text.Height * Math.Max(text.Proportion, 0.5)
            : text.Height);
        IncludeRotatedBox(ref acc, text.Position, width, text.Height, text.Angle);
    }

    private static void IncludeRotatedBox(ref BoundsAccumulator acc, Vector2 origin, double width, double height, double angleRadians)
    {
        var cos = Math.Cos(angleRadians);
        var sin = Math.Sin(angleRadians);

        Include(ref acc, origin.X, origin.Y);
        Include(ref acc, origin.X + width * cos, origin.Y + width * sin);
        Include(ref acc, origin.X - height * sin, origin.Y + height * cos);
        Include(ref acc, origin.X + width * cos - height * sin, origin.Y + width * sin + height * cos);
    }

    private static void Include(ref BoundsAccumulator acc, Vector2 point)
    {
        Include(ref acc, point.X, point.Y);
    }

    private static void Include(ref BoundsAccumulator acc, double x, double y)
    {
        if (!acc.HasValue)
        {
            acc = new BoundsAccumulator
            {
                HasValue = true,
                MinX = x,
                MinY = y,
                MaxX = x,
                MaxY = y
            };
            return;
        }

        if (x < acc.MinX) acc.MinX = x;
        if (y < acc.MinY) acc.MinY = y;
        if (x > acc.MaxX) acc.MaxX = x;
        if (y > acc.MaxY) acc.MaxY = y;
    }

    private static double Clamp(double value, double min, double max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    private struct BoundsAccumulator
    {
        public bool HasValue;
        public double MinX;
        public double MinY;
        public double MaxX;
        public double MaxY;
    }
}

public sealed class LayoutTableGeometryInfo
{
    public int TableId { get; set; }
    public int PrimitiveCount { get; set; }
    public bool HasGeometry { get; set; }
    public ReservedRect? Bounds { get; set; }
}
