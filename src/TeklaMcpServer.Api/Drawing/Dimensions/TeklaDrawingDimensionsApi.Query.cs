using System.Collections.Generic;
using System.Linq;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;

namespace TeklaMcpServer.Api.Drawing;

public sealed partial class TeklaDrawingDimensionsApi
{
    internal List<DimensionGroup> GetDimensionGroups(int? viewId) => DimensionGroupFactory.BuildGroups(GetDimensions(viewId));

    public GetDimensionsResult GetDimensions(int? viewId)
    {
        var activeDrawing = new DrawingHandler().GetActiveDrawing();
        if (activeDrawing == null)
            throw new DrawingNotOpenException();

        var previousAutoFetch = DrawingEnumeratorBase.AutoFetch;
        DrawingEnumeratorBase.AutoFetch = false;
        try
        {
            DrawingObjectEnumerator dimObjects;
            if (viewId.HasValue)
            {
                var view = EnumerateViews(activeDrawing).FirstOrDefault(v => v.GetIdentifier().ID == viewId.Value)
                    ?? throw new ViewNotFoundException(viewId.Value);
                dimObjects = view.GetAllObjects(typeof(StraightDimensionSet));
            }
            else
            {
                dimObjects = activeDrawing.GetSheet().GetAllObjects(typeof(StraightDimensionSet));
            }

            var dimensions = new List<DrawingDimensionInfo>();
            while (dimObjects.MoveNext())
            {
                if (dimObjects.Current is not StraightDimensionSet dimSet)
                    continue;

                dimensions.Add(BuildDimensionInfo(dimSet));
            }

            return new GetDimensionsResult
            {
                Total = dimensions.Count,
                Dimensions = dimensions
            };
        }
        finally
        {
            DrawingEnumeratorBase.AutoFetch = previousAutoFetch;
        }
    }

    private static DrawingDimensionInfo BuildDimensionInfo(StraightDimensionSet dimSet)
    {
        var (ownerViewId, ownerViewType, ownerViewScale) = GetOwnerViewInfo(dimSet);
        var segments = EnumerateSegments(dimSet);
        var lineContext = TryCreateDimensionLineContext(segments, dimSet.Distance);
        var info = new DrawingDimensionInfo
        {
            Id = dimSet.GetIdentifier().ID,
            Type = dimSet.GetType().Name,
            DimensionType = TryGetDimensionType(dimSet),
            ViewId = ownerViewId,
            ViewType = ownerViewType,
            ViewScale = ownerViewScale,
            Distance = dimSet.Distance,
            Bounds = TryGetBounds(dimSet)
        };

        foreach (var segment in segments)
        {
            info.Segments.Add(BuildSegmentInfo(segment, dimSet, dimSet.Distance, lineContext));
        }

        info.Bounds ??= CombineBounds(info.Segments.Select(static s => s.Bounds));
        if (lineContext.HasValue)
        {
            info.DirectionX = lineContext.Value.Direction.X;
            info.DirectionY = lineContext.Value.Direction.Y;
            info.TopDirection = lineContext.Value.TopDirection;
            info.ReferenceLine = lineContext.Value.ReferenceLine;
        }
        else
        {
            var representative = info.Segments
                .Where(static s => s.DimensionLine != null)
                .OrderByDescending(static s => s.DimensionLine!.Length)
                .FirstOrDefault()
                ?? info.Segments.OrderByDescending(static s => (s.EndX - s.StartX) * (s.EndX - s.StartX) + (s.EndY - s.StartY) * (s.EndY - s.StartY)).FirstOrDefault();
            if (representative != null)
            {
                info.DirectionX = representative.DirectionX;
                info.DirectionY = representative.DirectionY;
                info.TopDirection = representative.TopDirection;
                info.ReferenceLine = TryCreateReferenceLine(representative);
            }
        }

        info.MeasuredPoints = BuildMeasuredPointList(info.Segments, info.DirectionX, info.DirectionY);

        info.Orientation = DetermineDimensionOrientation(
            info.DirectionX,
            info.DirectionY,
            info.ReferenceLine,
            info.Segments);

        return info;
    }

    private static DimensionSegmentInfo BuildSegmentInfo(
        StraightDimension segment,
        StraightDimensionSet dimSet,
        double distance,
        DimensionLineContext? lineContext = null)
    {
        var start = segment.StartPoint;
        var end = segment.EndPoint;
        var segmentDirection = default((double X, double Y));
        var upDirection = lineContext?.UpDirection ?? default;
        var hasUpDirection = lineContext.HasValue || TryGetUpDirection(segment, out upDirection);

        DrawingLineInfo? dimensionLine = null;
        DrawingLineInfo? leadLineMain = null;
        DrawingLineInfo? leadLineSecond = null;
        var topDirection = 0;
        if (hasUpDirection)
        {
            segmentDirection = lineContext?.Direction ?? CanonicalizeDirection(-upDirection.Y, upDirection.X);
            var referenceStart = lineContext?.ReferenceLine != null
                ? ProjectPointToReferenceLine(
                    start.X,
                    start.Y,
                    lineContext.Value.ReferenceLine.StartX,
                    lineContext.Value.ReferenceLine.StartY,
                    segmentDirection.X,
                    segmentDirection.Y)
                : CreateReferencePoint(start.X, start.Y, upDirection, distance);
            var referenceEnd = ProjectPointToReferenceLine(
                end.X,
                end.Y,
                referenceStart.X,
                referenceStart.Y,
                segmentDirection.X,
                segmentDirection.Y);

            dimensionLine = CreateLineInfo(referenceStart.X, referenceStart.Y, referenceEnd.X, referenceEnd.Y);
            leadLineMain = CreateLineInfo(start.X, start.Y, referenceStart.X, referenceStart.Y);
            leadLineSecond = CreateLineInfo(end.X, end.Y, referenceEnd.X, referenceEnd.Y);
            topDirection = lineContext?.TopDirection ?? GetTopDirection(upDirection.X, upDirection.Y);
        }
        else
        {
            TryNormalizeDirection(end.X - start.X, end.Y - start.Y, out segmentDirection);
        }

        return new DimensionSegmentInfo
        {
            Id = segment.GetIdentifier().ID,
            StartX = System.Math.Round(start.X, 1),
            StartY = System.Math.Round(start.Y, 1),
            EndX = System.Math.Round(end.X, 1),
            EndY = System.Math.Round(end.Y, 1),
            Distance = System.Math.Round(distance, 3),
            DirectionX = segmentDirection.X,
            DirectionY = segmentDirection.Y,
            TopDirection = topDirection,
            Bounds = TryGetBounds(segment)
                ?? (dimensionLine != null ? CreateBoundsFromLine(dimensionLine) : CreateBoundsFromSegmentPoints(start, end)),
            TextBounds = TryGetTextBounds(segment, dimSet, dimensionLine),
            DimensionLine = dimensionLine,
            LeadLineMain = leadLineMain,
            LeadLineSecond = leadLineSecond
        };
    }

    private static List<StraightDimension> EnumerateSegments(StraightDimensionSet dimSet)
    {
        var segments = new List<StraightDimension>();
        var segEnum = dimSet.GetObjects();
        while (segEnum.MoveNext())
        {
            if (segEnum.Current is StraightDimension segment)
                segments.Add(segment);
        }

        return segments;
    }

    private static DimensionLineContext? TryCreateDimensionLineContext(
        IReadOnlyList<StraightDimension> segments,
        double distance)
    {
        if (segments.Count == 0)
            return null;

        if (!TryGetUpDirection(segments[0], out var upDirection))
            return null;

        var points = new List<(double X, double Y)>(segments.Count * 2);
        foreach (var segment in segments)
        {
            points.Add((segment.StartPoint.X, segment.StartPoint.Y));
            points.Add((segment.EndPoint.X, segment.EndPoint.Y));
        }

        var referenceLine = TryCreateCommonReferenceLine(points, upDirection, distance, out var direction);
        if (referenceLine == null)
            return null;

        return new DimensionLineContext(
            upDirection,
            direction,
            GetTopDirection(upDirection.X, upDirection.Y),
            referenceLine);
    }
}
