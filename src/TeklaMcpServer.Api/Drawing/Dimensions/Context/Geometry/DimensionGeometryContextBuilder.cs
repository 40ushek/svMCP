using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class DimensionGeometryContextBuilder
{
    public DimensionGeometryContext Build(DimensionItem item)
    {
        var context = new DimensionGeometryContext
        {
            ReferenceLine = CopyLine(item.ReferenceLine),
            Distance = item.Distance
        };

        if (context.ReferenceLine != null)
        {
            context.DimensionLineStart = new DrawingPointInfo
            {
                X = context.ReferenceLine.StartX,
                Y = context.ReferenceLine.StartY,
                Order = -1
            };
            context.DimensionLineEnd = new DrawingPointInfo
            {
                X = context.ReferenceLine.EndX,
                Y = context.ReferenceLine.EndY,
                Order = -1
            };
        }
        else
        {
            context.Warnings.Add("reference_line_unavailable");
        }

        foreach (var point in GetMeasuredPoints(item))
            context.MeasuredPoints.Add(point);

        foreach (var segment in BuildSegmentGeometries(item))
            context.SegmentGeometries.Add(segment);

        context.TextBounds = TeklaDrawingDimensionsApi.CombineBounds(
            context.SegmentGeometries.Select(static segment => segment.TextBounds));
        if (!context.HasTextBounds)
            context.Warnings.Add("text_bounds_unavailable");

        if (!TryResolveLineDirection(item, context.ReferenceLine, out var lineDirection))
        {
            context.Warnings.Add("line_direction_unavailable");
            return context;
        }

        context.LineDirection = CreateVector(lineDirection.X, lineDirection.Y);
        context.NormalDirection = CreateNormalDirection(lineDirection.X, lineDirection.Y, item.TopDirection, context.Warnings);

        if (context.ReferenceLine == null || context.NormalDirection == null)
            return context;

        var originX = context.ReferenceLine.StartX;
        var originY = context.ReferenceLine.StartY;

        if (context.MeasuredPoints.Count > 0)
        {
            var alongValues = context.MeasuredPoints
                .Select(point => Round(Project(point.X - originX, point.Y - originY, lineDirection.X, lineDirection.Y)))
                .OrderBy(static value => value)
                .ToList();
            context.StartAlong = alongValues[0];
            context.EndAlong = alongValues[alongValues.Count - 1];
        }
        else
        {
            context.Warnings.Add("measured_points_unavailable");
            context.StartAlong = 0;
            context.EndAlong = Round(context.ReferenceLine.Length);
        }

        context.LocalBand = TryBuildBand(context, originX, originY, lineDirection);
        return context;
    }

    private static IReadOnlyList<DrawingPointInfo> GetMeasuredPoints(DimensionItem item)
    {
        var points = item.Dimension.MeasuredPoints.Count > 0
            ? item.Dimension.MeasuredPoints
            : item.PointList;

        return points
            .OrderBy(static point => point.Order)
            .Select(static point => new DrawingPointInfo
            {
                X = point.X,
                Y = point.Y,
                Order = point.Order
            })
            .ToList();
    }

    private static IReadOnlyList<DimensionSegmentGeometry> BuildSegmentGeometries(DimensionItem item)
    {
        if (item.Dimension.Segments.Count > 0)
        {
            return item.Dimension.Segments.Select(static segment => new DimensionSegmentGeometry
            {
                SegmentId = segment.Id,
                DimensionLine = CopyLine(segment.DimensionLine),
                LeadLineMain = CopyLine(segment.LeadLineMain),
                LeadLineSecond = CopyLine(segment.LeadLineSecond),
                TextBounds = CopyBounds(segment.TextBounds),
                StartPoint = new DrawingPointInfo
                {
                    X = segment.StartX,
                    Y = segment.StartY,
                    Order = -1
                },
                EndPoint = new DrawingPointInfo
                {
                    X = segment.EndX,
                    Y = segment.EndY,
                    Order = -1
                }
            }).ToList();
        }

        if (item.ReferenceLine == null && item.LeadLineMain == null && item.LeadLineSecond == null)
            return [];

        return
        [
            new DimensionSegmentGeometry
            {
                SegmentId = item.SegmentId,
                DimensionLine = CopyLine(item.ReferenceLine),
                LeadLineMain = CopyLine(item.LeadLineMain),
                LeadLineSecond = CopyLine(item.LeadLineSecond),
                StartPoint = new DrawingPointInfo
                {
                    X = item.StartX,
                    Y = item.StartY,
                    Order = item.StartPointOrder
                },
                EndPoint = new DrawingPointInfo
                {
                    X = item.EndX,
                    Y = item.EndY,
                    Order = item.EndPointOrder
                }
            }
        ];
    }

    private static bool TryResolveLineDirection(
        DimensionItem item,
        DrawingLineInfo? referenceLine,
        out (double X, double Y) direction)
    {
        if (referenceLine != null &&
            TeklaDrawingDimensionsApi.TryNormalizeDirection(
                referenceLine.EndX - referenceLine.StartX,
                referenceLine.EndY - referenceLine.StartY,
                out direction))
        {
            return true;
        }

        if (item.Direction.HasValue)
        {
            direction = item.Direction.Value;
            return true;
        }

        direction = default;
        return false;
    }

    private static DrawingVectorInfo CreateVector(double x, double y) => new()
    {
        X = Round(x),
        Y = Round(y)
    };

    private static DrawingVectorInfo CreateNormalDirection(double directionX, double directionY, int topDirection, List<string> warnings)
    {
        var sign = topDirection == 0 ? 1 : topDirection;
        if (topDirection == 0)
            warnings.Add("normal_direction_fallback");

        return CreateVector(-directionY * sign, directionX * sign);
    }

    private static DimensionGeometryBand? TryBuildBand(
        DimensionGeometryContext context,
        double originX,
        double originY,
        (double X, double Y) lineDirection)
    {
        if (context.NormalDirection == null)
            return null;

        var samples = new List<(double X, double Y)>();
        AddLineSamples(samples, context.ReferenceLine);

        foreach (var point in context.MeasuredPoints)
            samples.Add((point.X, point.Y));

        foreach (var segment in context.SegmentGeometries)
        {
            AddLineSamples(samples, segment.DimensionLine);
            AddBoundsSamples(samples, segment.TextBounds);
        }

        if (samples.Count == 0)
            return null;

        var alongValues = samples
            .Select(sample => Project(sample.X - originX, sample.Y - originY, lineDirection.X, lineDirection.Y))
            .ToList();
        var offsetValues = samples
            .Select(sample => Project(sample.X - originX, sample.Y - originY, context.NormalDirection.X, context.NormalDirection.Y))
            .ToList();

        return new DimensionGeometryBand
        {
            StartAlong = Round(alongValues.Min()),
            EndAlong = Round(alongValues.Max()),
            MinOffset = Round(offsetValues.Min()),
            MaxOffset = Round(offsetValues.Max())
        };
    }

    private static void AddLineSamples(List<(double X, double Y)> samples, DrawingLineInfo? line)
    {
        if (line == null)
            return;

        samples.Add((line.StartX, line.StartY));
        samples.Add((line.EndX, line.EndY));
    }

    private static void AddBoundsSamples(List<(double X, double Y)> samples, DrawingBoundsInfo? bounds)
    {
        if (bounds == null)
            return;

        samples.Add((bounds.MinX, bounds.MinY));
        samples.Add((bounds.MinX, bounds.MaxY));
        samples.Add((bounds.MaxX, bounds.MinY));
        samples.Add((bounds.MaxX, bounds.MaxY));
    }

    private static DrawingLineInfo? CopyLine(DrawingLineInfo? line)
    {
        return line == null
            ? null
            : TeklaDrawingDimensionsApi.CreateLineInfo(line.StartX, line.StartY, line.EndX, line.EndY);
    }

    private static DrawingBoundsInfo? CopyBounds(DrawingBoundsInfo? bounds)
    {
        return bounds == null
            ? null
            : TeklaDrawingDimensionsApi.CreateBoundsInfo(bounds.MinX, bounds.MinY, bounds.MaxX, bounds.MaxY);
    }

    private static double Project(double x, double y, double axisX, double axisY) => (x * axisX) + (y * axisY);

    private static double Round(double value) => System.Math.Round(value, 3);
}
