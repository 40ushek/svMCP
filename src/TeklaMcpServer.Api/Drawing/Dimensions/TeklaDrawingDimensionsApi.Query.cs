using System.Collections.Generic;
using System.Linq;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;

namespace TeklaMcpServer.Api.Drawing;

public sealed partial class TeklaDrawingDimensionsApi
{
    internal List<DrawingDimensionInfo> GetDimensionSnapshots(int? viewId)
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

            return dimensions;
        }
        finally
        {
            DrawingEnumeratorBase.AutoFetch = previousAutoFetch;
        }
    }

    internal List<DimensionGroup> GetDimensionGroups(int? viewId) => DimensionGroupFactory.BuildGroups(GetDimensionSnapshots(viewId));

    internal DimensionReductionDebugResult GetDimensionGroupReductionDebug(int? viewId)
    {
        var debug = DimensionGroupFactory.BuildGroupsWithReductionDebug(GetDimensionSnapshots(viewId));
        AttachDimensionContexts(debug);
        return debug;
    }

    public GetDimensionsResult GetDimensions(int? viewId)
    {
        var snapshots = GetDimensionSnapshots(viewId);
        var groups = DimensionGroupFactory.BuildGroups(snapshots);
        return BuildGetDimensionsResult(snapshots, groups);
    }

    private static GetDimensionsResult BuildGetDimensionsResult(
        IReadOnlyList<DrawingDimensionInfo> snapshots,
        IReadOnlyList<DimensionGroup> groups)
    {
        var result = new GetDimensionsResult
        {
            Total = groups.Sum(static group => group.DimensionList.Count),
            DrawingDimensionCount = snapshots.Count,
            RawItemCount = groups.Sum(static group => group.RawItemCount),
            ReducedItemCount = groups.Sum(static group => group.ReducedItemCount),
            GroupCount = groups.Count
        };

        foreach (var group in groups)
        {
            var info = new DimensionGroupInfo
            {
                ViewId = group.ViewId,
                ViewType = group.ViewType,
                DimensionType = group.DimensionType,
                TeklaDimensionType = group.TeklaDimensionType,
                Direction = group.Direction.HasValue
                    ? new DrawingVectorInfo
                    {
                        X = group.Direction.Value.X,
                        Y = group.Direction.Value.Y
                    }
                    : null,
                TopDirection = group.TopDirection,
                ReferenceLine = CopyLine(group.ReferenceLine),
                LeadLineMain = CopyLine(group.LeadLineMain),
                LeadLineSecond = CopyLine(group.LeadLineSecond),
                MaximumDistance = System.Math.Round(group.MaximumDistance, 3),
                RawItemCount = group.RawItemCount,
                ReducedItemCount = group.ReducedItemCount
            };

            foreach (var item in group.DimensionList)
            {
                info.Items.Add(new DimensionItemInfo
                {
                    Id = item.DimensionId,
                    SegmentIds = item.SegmentIds.ToList(),
                    ViewId = item.ViewId,
                    DimensionType = item.DimensionType,
                    TeklaDimensionType = item.TeklaDimensionType,
                    ReferenceLine = CopyLine(item.ReferenceLine),
                    StartPoint = new DrawingPointInfo { X = item.StartX, Y = item.StartY, Order = item.StartPointOrder },
                    EndPoint = new DrawingPointInfo { X = item.EndX, Y = item.EndY, Order = item.EndPointOrder },
                    CenterPoint = new DrawingPointInfo { X = item.CenterX, Y = item.CenterY, Order = -1 },
                    PointList = item.PointList.Select(static point => new DrawingPointInfo
                    {
                        X = point.X,
                        Y = point.Y,
                        Order = point.Order
                    }).ToList(),
                    LengthList = item.LengthList.ToList(),
                    RealLengthList = item.RealLengthList.ToList(),
                    Distance = System.Math.Round(item.Distance, 3)
                });
            }

            result.Groups.Add(info);
        }

        return result;
    }

    private DrawingDimensionInfo BuildDimensionInfo(StraightDimensionSet dimSet)
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
        info.GeometryKind = ResolveDimensionGeometryKind(info.Orientation);
        var sourceSummary = ResolveDimensionSourceSummary(dimSet, segments);
        info.SourceKind = sourceSummary.SourceKind;
        info.SourceObjectIds.AddRange(sourceSummary.SourceObjectIds);
        info.ClassifiedDimensionType = DimensionGroupFactory.MapDomainDimensionType(info.SourceKind, info.GeometryKind);

        return info;
    }

    private (DimensionSourceKind SourceKind, List<int> SourceObjectIds) ResolveDimensionSourceSummary(
        StraightDimensionSet dimSet,
        IReadOnlyList<StraightDimension> segments)
    {
        var hasPart = false;
        var hasGrid = false;
        var sourceObjectIds = new HashSet<int>();

        CollectDimensionSourceSummary(dimSet.GetRelatedObjects(), ref hasPart, ref hasGrid, sourceObjectIds);

        foreach (var segment in segments)
            CollectDimensionSourceSummary(segment.GetRelatedObjects(), ref hasPart, ref hasGrid, sourceObjectIds);

        var sourceKind = (hasPart, hasGrid) switch
        {
            (true, false) => DimensionSourceKind.Part,
            (false, true) => DimensionSourceKind.Grid,
            _ => DimensionSourceKind.Unknown
        };

        return (sourceKind, sourceObjectIds.OrderBy(static id => id).ToList());
    }

    private void CollectDimensionSourceSummary(
        DrawingObjectEnumerator? relatedObjects,
        ref bool hasPart,
        ref bool hasGrid,
        HashSet<int> sourceObjectIds)
    {
        if (relatedObjects == null)
            return;

        while (relatedObjects.MoveNext())
        {
            var relatedObject = relatedObjects.Current;
            var sourceKind = ResolveRelatedObjectSourceKind(relatedObject);
            if (sourceKind == DimensionSourceKind.Part)
                hasPart = true;
            else if (sourceKind == DimensionSourceKind.Grid)
                hasGrid = true;

            if (TryGetRelatedObjectId(relatedObject, out var sourceObjectId))
                sourceObjectIds.Add(sourceObjectId);
        }
    }

    private static bool TryGetRelatedObjectId(object? relatedObject, out int id)
    {
        id = 0;
        if (relatedObject == null)
            return false;

        if (relatedObject is Tekla.Structures.Drawing.ModelObject drawingModelObject)
        {
            try
            {
                var modelId = drawingModelObject.ModelIdentifier.ID;
                if (modelId > 0)
                {
                    id = modelId;
                    return true;
                }
            }
            catch
            {
            }
        }

        if (relatedObject is DrawingObject drawingObject)
        {
            try
            {
                var drawingId = drawingObject.GetIdentifier().ID;
                if (drawingId > 0)
                {
                    id = drawingId;
                    return true;
                }
            }
            catch
            {
            }
        }

        return false;
    }

    private DimensionSourceKind ResolveRelatedObjectSourceKind(object? relatedObject)
    {
        if (relatedObject == null)
            return DimensionSourceKind.Unknown;

        if (relatedObject is GridLine)
            return DimensionSourceKind.Grid;

        if (relatedObject is Tekla.Structures.Drawing.Part)
            return DimensionSourceKind.Part;

        if (relatedObject is Tekla.Structures.Drawing.ModelObject drawingModelObject)
        {
            var modelObject = TrySelectRelatedModelObject(drawingModelObject);
            if (modelObject is Tekla.Structures.Model.Part)
                return DimensionSourceKind.Part;

            if (modelObject != null && modelObject.GetType().Name.IndexOf("Grid", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return DimensionSourceKind.Grid;
        }

        var typeName = relatedObject.GetType().Name;
        if (typeName.IndexOf("Grid", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return DimensionSourceKind.Grid;
        if (typeName.IndexOf("Part", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return DimensionSourceKind.Part;

        return DimensionSourceKind.Unknown;
    }

    private Tekla.Structures.Model.ModelObject? TrySelectRelatedModelObject(Tekla.Structures.Drawing.ModelObject drawingModelObject)
    {
        try
        {
            return _model.SelectModelObject(drawingModelObject.ModelIdentifier);
        }
        catch
        {
            return null;
        }
    }

    internal static DimensionGeometryKind ResolveDimensionGeometryKind(string orientation)
    {
        return orientation switch
        {
            "horizontal" => DimensionGeometryKind.Horizontal,
            "vertical" => DimensionGeometryKind.Vertical,
            "angled" => DimensionGeometryKind.Free,
            _ => DimensionGeometryKind.Unknown
        };
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

    private void AttachDimensionContexts(DimensionReductionDebugResult debug)
    {
        var items = debug.Groups
            .SelectMany(static group => group.Items)
            .Select(static item => item.Item)
            .Distinct()
            .ToList();
        if (items.Count == 0)
            return;

        var builder = new DimensionContextBuilder(new TeklaDrawingPartPointApi(_model));
        var contexts = builder.Build(items)
            .Contexts
            .Where(static context => context.Item != null)
            .ToDictionary(static context => context.Item);

        foreach (var group in debug.Groups)
        {
            foreach (var item in group.Items)
            {
                if (contexts.TryGetValue(item.Item, out var context))
                    item.Context = context;
            }
        }
    }
}
