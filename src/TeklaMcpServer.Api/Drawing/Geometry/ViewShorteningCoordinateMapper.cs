using System;
using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.Geometry3d;

namespace TeklaMcpServer.Api.Drawing;

public readonly struct ViewShorteningVisibleBox
{
    public ViewShorteningVisibleBox(double minX, double minY, double maxX, double maxY)
    {
        MinX = minX;
        MinY = minY;
        MaxX = maxX;
        MaxY = maxY;
    }

    public double MinX { get; }
    public double MinY { get; }
    public double MaxX { get; }
    public double MaxY { get; }
}

public readonly struct ViewShorteningInterval
{
    public ViewShorteningInterval(double min, double max)
    {
        Min = min;
        Max = max;
    }

    public double Min { get; }
    public double Max { get; }
}

public sealed class ViewShorteningCoordinateMapper
{
    private const double Epsilon = 1e-6;

    private readonly List<Interval> _xIntervals;
    private readonly List<Interval> _yIntervals;
    private readonly IReadOnlyList<ViewShorteningInterval> _publicXIntervals;
    private readonly IReadOnlyList<ViewShorteningInterval> _publicYIntervals;
    private readonly double _spaceBetweenCutParts;

    private ViewShorteningCoordinateMapper(
        List<Interval> xIntervals,
        List<Interval> yIntervals,
        double spaceBetweenCutParts)
    {
        _xIntervals = xIntervals;
        _yIntervals = yIntervals;
        _publicXIntervals = ToPublicIntervals(xIntervals);
        _publicYIntervals = ToPublicIntervals(yIntervals);
        _spaceBetweenCutParts = Math.Max(0.0, spaceBetweenCutParts);
    }

    public bool HasShorteningX => _xIntervals.Count > 1;

    public bool HasShorteningY => _yIntervals.Count > 1;

    public bool HasShortening => HasShorteningX || HasShorteningY;

    public IReadOnlyList<ViewShorteningInterval> XIntervals => _publicXIntervals;

    public IReadOnlyList<ViewShorteningInterval> YIntervals => _publicYIntervals;

    public static ViewShorteningCoordinateMapper FromVisibleBoxes(
        IReadOnlyList<ViewShorteningVisibleBox> boxes,
        double spaceBetweenCutParts = 0.0)
    {
        if (boxes == null)
            throw new ArgumentNullException(nameof(boxes));

        return new ViewShorteningCoordinateMapper(
            BuildIntervals(boxes, axis: 0),
            BuildIntervals(boxes, axis: 1),
            spaceBetweenCutParts);
    }

    public static ViewShorteningCoordinateMapper FromAabbs(
        IReadOnlyList<AABB> boxes,
        double spaceBetweenCutParts = 0.0)
    {
        if (boxes == null)
            throw new ArgumentNullException(nameof(boxes));

        var visibleBoxes = boxes
            .Where(static box => box != null)
            .Select(static box => new ViewShorteningVisibleBox(
                box.MinPoint.X,
                box.MinPoint.Y,
                box.MaxPoint.X,
                box.MaxPoint.Y))
            .ToList();

        return FromVisibleBoxes(visibleBoxes, spaceBetweenCutParts);
    }

    public double[] ConvertPoint(double x, double y)
    {
        ConvertPointToVisual(x, y, out var convertedX, out var convertedY);
        return new[] { convertedX, convertedY };
    }

    public void ConvertPoint(double x, double y, out double convertedX, out double convertedY)
        => ConvertPointToVisual(x, y, out convertedX, out convertedY);

    public double[] ConvertPointToVisual(double rawX, double rawY)
    {
        ConvertPointToVisual(rawX, rawY, out var visualX, out var visualY);
        return new[] { visualX, visualY };
    }

    public void ConvertPointToVisual(double rawX, double rawY, out double visualX, out double visualY)
    {
        visualX = ConvertAxisToVisual(rawX, _xIntervals, _spaceBetweenCutParts);
        visualY = ConvertAxisToVisual(rawY, _yIntervals, _spaceBetweenCutParts);
    }

    public double[] ConvertPointToRaw(double visualX, double visualY)
    {
        ConvertPointToRaw(visualX, visualY, out var rawX, out var rawY);
        return new[] { rawX, rawY };
    }

    public void ConvertPointToRaw(double visualX, double visualY, out double rawX, out double rawY)
    {
        rawX = ConvertAxisToRaw(visualX, _xIntervals, _spaceBetweenCutParts);
        rawY = ConvertAxisToRaw(visualY, _yIntervals, _spaceBetweenCutParts);
    }

    public List<double[]> ConvertPolygon(IReadOnlyList<double[]> polygon)
    {
        if (polygon == null)
            throw new ArgumentNullException(nameof(polygon));

        var result = new List<double[]>(polygon.Count);
        foreach (var point in polygon)
        {
            if (point == null || point.Length < 2)
                throw new ArgumentException("Polygon points must contain at least X and Y coordinates.", nameof(polygon));

            ConvertPointToVisual(point[0], point[1], out var convertedX, out var convertedY);
            result.Add(new[] { convertedX, convertedY });
        }

        return result;
    }

    public List<double[]> ConvertPolygonToRaw(IReadOnlyList<double[]> polygon)
    {
        if (polygon == null)
            throw new ArgumentNullException(nameof(polygon));

        var result = new List<double[]>(polygon.Count);
        foreach (var point in polygon)
        {
            if (point == null || point.Length < 2)
                throw new ArgumentException("Polygon points must contain at least X and Y coordinates.", nameof(polygon));

            ConvertPointToRaw(point[0], point[1], out var rawX, out var rawY);
            result.Add(new[] { rawX, rawY });
        }

        return result;
    }

    private static List<Interval> BuildIntervals(
        IReadOnlyList<ViewShorteningVisibleBox> boxes,
        int axis)
    {
        return boxes
            .Select(box => axis == 0
                ? new Interval(box.MinX, box.MaxX)
                : new Interval(box.MinY, box.MaxY))
            .Where(static interval => interval.Max > interval.Min + Epsilon)
            .OrderBy(static interval => interval.Min)
            .ThenBy(static interval => interval.Max)
            .Aggregate(new List<Interval>(), MergeInterval);
    }

    private static List<Interval> MergeInterval(List<Interval> merged, Interval next)
    {
        if (merged.Count == 0)
        {
            merged.Add(next);
            return merged;
        }

        var last = merged[merged.Count - 1];
        if (next.Min <= last.Max + Epsilon)
        {
            merged[merged.Count - 1] = new Interval(last.Min, Math.Max(last.Max, next.Max));
            return merged;
        }

        merged.Add(next);
        return merged;
    }

    private static IReadOnlyList<ViewShorteningInterval> ToPublicIntervals(IReadOnlyList<Interval> intervals)
    {
        return intervals
            .Select(static interval => new ViewShorteningInterval(interval.Min, interval.Max))
            .ToList();
    }

    private static double ConvertAxisToVisual(
        double value,
        IReadOnlyList<Interval> intervals,
        double spaceBetweenCutParts)
    {
        if (intervals.Count <= 1)
            return value;

        var removedBefore = 0.0;
        for (var i = 0; i < intervals.Count; i++)
        {
            var interval = intervals[i];
            if (value <= interval.Max + Epsilon)
                return value - removedBefore;

            if (i + 1 < intervals.Count)
            {
                removedBefore += CalculateRemovedGap(interval, intervals[i + 1], spaceBetweenCutParts);
            }
        }

        return value - removedBefore;
    }

    private static double ConvertAxisToRaw(
        double value,
        IReadOnlyList<Interval> intervals,
        double spaceBetweenCutParts)
    {
        if (intervals.Count <= 1)
            return value;

        var visualStart = intervals[0].Min;
        var removedBefore = 0.0;
        for (var i = 0; i < intervals.Count; i++)
        {
            var interval = intervals[i];
            var intervalLength = interval.Max - interval.Min;
            var visualEnd = visualStart + intervalLength;

            if (value <= visualEnd + Epsilon)
                return value + removedBefore;

            if (i + 1 < intervals.Count)
            {
                removedBefore += CalculateRemovedGap(interval, intervals[i + 1], spaceBetweenCutParts);
            }

            visualStart = visualEnd;
        }

        return value + removedBefore;
    }

    private static double CalculateRemovedGap(
        Interval before,
        Interval after,
        double spaceBetweenCutParts)
    {
        var rawGap = after.Min - before.Max;
        if (rawGap <= Epsilon)
            return 0.0;

        return Math.Max(0.0, rawGap - spaceBetweenCutParts);
    }

    private readonly struct Interval
    {
        public Interval(double min, double max)
        {
            Min = min;
            Max = max;
        }

        public double Min { get; }
        public double Max { get; }
    }
}
