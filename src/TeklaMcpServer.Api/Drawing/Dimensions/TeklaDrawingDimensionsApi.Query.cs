using System.Collections.Generic;
using System.Linq;
using System.Text;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;

namespace TeklaMcpServer.Api.Drawing;

public sealed partial class TeklaDrawingDimensionsApi
{
    internal List<TeklaDimensionSetSnapshot> GetDimensionSnapshots(int? viewId)
        => DimensionStableReadHelper.ReadStable(
            () => ReadDimensionSnapshotsCore(viewId),
            BuildDimensionSnapshotFingerprint);

    private List<TeklaDimensionSetSnapshot> ReadDimensionSnapshotsCore(int? viewId)
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

            var dimensions = new List<TeklaDimensionSetSnapshot>();
            while (dimObjects.MoveNext())
            {
                if (dimObjects.Current is not StraightDimensionSet dimSet)
                    continue;

                dimensions.Add(BuildDimensionSnapshot(dimSet));
            }

            return dimensions;
        }
        finally
        {
            DrawingEnumeratorBase.AutoFetch = previousAutoFetch;
        }
    }

    internal List<DimensionGroup> GetDimensionGroups(int? viewId)
        => DimensionGroupFactory.BuildGroups(GetDimensionSnapshots(viewId));

    internal DimensionReductionDebugResult GetDimensionGroupReductionDebug(int? viewId)
    {
        var debug = DimensionGroupFactory.BuildGroupsWithReductionDebug(GetDimensionSnapshots(viewId));
        AttachDimensionContexts(debug);
        return debug;
    }

    internal DimensionOrchestrationDebugResult GetDimensionOrchestrationDebug(int? viewId)
    {
        var debug = GetDimensionGroupReductionDebug(viewId);
        return DimensionOrchestrationDebugBuilder.Build(debug, viewId);
    }

    internal DimensionAiOrchestrationPlanResult GetDimensionAiOrchestrationPlan(int? viewId)
    {
        var debug = GetDimensionGroupReductionDebug(viewId);
        return new DimensionAiAssistedOrchestrator().Build(debug, viewId);
    }

    public GetDimensionsResult GetDimensions(int? viewId)
    {
        var snapshots = GetDimensionSnapshots(viewId);
        var groups = DimensionGroupFactory.BuildGroups(snapshots);
        return BuildGetDimensionsResult(snapshots.Count, groups);
    }

    private static GetDimensionsResult BuildGetDimensionsResult(
        int drawingDimensionCount,
        IReadOnlyList<DimensionGroup> groups)
    {
        var result = new GetDimensionsResult
        {
            Total = groups.Sum(static group => group.DimensionList.Count),
            DrawingDimensionCount = drawingDimensionCount,
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

    internal static DrawingDimensionInfo ProjectDimensionSnapshotToReadModel(TeklaDimensionSetSnapshot snapshot)
    {
        var info = new DrawingDimensionInfo
        {
            Id = snapshot.Id,
            Type = snapshot.Type,
            DimensionType = snapshot.TeklaDimensionType,
            ViewId = snapshot.ViewId,
            ViewType = snapshot.ViewType,
            ViewScale = snapshot.ViewScale,
            Orientation = snapshot.Orientation,
            Distance = snapshot.Distance,
            DirectionX = snapshot.DirectionX,
            DirectionY = snapshot.DirectionY,
            TopDirection = snapshot.TopDirection,
            Bounds = snapshot.Bounds == null ? null : CreateBoundsInfo(
                snapshot.Bounds.MinX,
                snapshot.Bounds.MinY,
                snapshot.Bounds.MaxX,
                snapshot.Bounds.MaxY),
            ReferenceLine = CopyLine(snapshot.ReferenceLine),
            SourceKind = snapshot.SourceKind,
            GeometryKind = snapshot.GeometryKind,
            ClassifiedDimensionType = snapshot.ClassifiedDimensionType
        };

        info.MeasuredPoints.AddRange(snapshot.MeasuredPoints.Select(static point => new DrawingPointInfo
        {
            X = point.X,
            Y = point.Y,
            Order = point.Order
        }));

        info.Segments.AddRange(snapshot.Segments.Select(ProjectDimensionSegmentSnapshotToReadModel));
        info.SourceObjectIds.AddRange(snapshot.SourceObjectIds);
        return info;
    }

    internal static string BuildDimensionSnapshotFingerprint(IReadOnlyList<TeklaDimensionSetSnapshot> snapshots)
    {
        var builder = new StringBuilder();
        foreach (var snapshot in snapshots.OrderBy(static item => item.Id))
        {
            builder.Append(snapshot.Id).Append('|');
            builder.Append(snapshot.Segments.Count).Append('|');
            foreach (var segment in snapshot.Segments.OrderBy(static segment => segment.Id))
                builder.Append(segment.Id).Append(',');

            builder.Append('|');
            builder.Append(System.Math.Round(snapshot.Distance, 3)).Append('|');

            if (snapshot.ReferenceLine != null)
            {
                builder.Append(System.Math.Round(snapshot.ReferenceLine.StartX, 3)).Append(',');
                builder.Append(System.Math.Round(snapshot.ReferenceLine.StartY, 3)).Append(',');
                builder.Append(System.Math.Round(snapshot.ReferenceLine.EndX, 3)).Append(',');
                builder.Append(System.Math.Round(snapshot.ReferenceLine.EndY, 3));
            }

            builder.Append(';');
        }

        return builder.ToString();
    }

    private TeklaDimensionSetSnapshot BuildDimensionSnapshot(StraightDimensionSet dimSet)
    {
        var (ownerViewId, ownerViewType, ownerViewScale) = GetOwnerViewInfo(dimSet);
        var segments = EnumerateSegments(dimSet);
        var lineContext = TryCreateDimensionLineContext(segments, dimSet.Distance);
        var snapshot = new TeklaDimensionSetSnapshot
        {
            Id = dimSet.GetIdentifier().ID,
            Type = dimSet.GetType().Name,
            TeklaDimensionType = TryGetDimensionType(dimSet),
            ViewId = ownerViewId,
            ViewType = ownerViewType,
            ViewScale = ownerViewScale,
            Distance = dimSet.Distance,
            Bounds = TryGetBounds(dimSet)
        };

        foreach (var segment in segments)
            snapshot.Segments.Add(BuildDimensionSegmentSnapshot(segment, dimSet, dimSet.Distance, lineContext));

        var projectedSegments = snapshot.Segments
            .Select(ProjectDimensionSegmentSnapshotToReadModel)
            .ToList();

        snapshot.Bounds ??= CombineBounds(snapshot.Segments.Select(static s => s.Bounds));
        if (lineContext.HasValue)
        {
            snapshot.DirectionX = lineContext.Value.Direction.X;
            snapshot.DirectionY = lineContext.Value.Direction.Y;
            snapshot.TopDirection = lineContext.Value.TopDirection;
            snapshot.ReferenceLine = lineContext.Value.ReferenceLine;
        }
        else
        {
            var representative = projectedSegments
                .Where(static s => s.DimensionLine != null)
                .OrderByDescending(static s => s.DimensionLine!.Length)
                .FirstOrDefault()
                ?? projectedSegments.OrderByDescending(static s => (s.EndX - s.StartX) * (s.EndX - s.StartX) + (s.EndY - s.StartY) * (s.EndY - s.StartY)).FirstOrDefault();
            if (representative != null)
            {
                snapshot.DirectionX = representative.DirectionX;
                snapshot.DirectionY = representative.DirectionY;
                snapshot.TopDirection = representative.TopDirection;
                snapshot.ReferenceLine = TryCreateReferenceLine(representative);
            }
        }

        snapshot.MeasuredPoints.AddRange(BuildMeasuredPointList(projectedSegments, snapshot.DirectionX, snapshot.DirectionY));

        snapshot.Orientation = DetermineDimensionOrientation(
            snapshot.DirectionX,
            snapshot.DirectionY,
            snapshot.ReferenceLine,
            projectedSegments);
        snapshot.GeometryKind = ResolveDimensionGeometryKind(snapshot.Orientation);
        var sourceSummary = ResolveDimensionSourceSummary(dimSet, segments);
        snapshot.SourceKind = sourceSummary.SourceKind;
        snapshot.SourceReferences.AddRange(sourceSummary.SourceReferences.Select(static source => new DimensionSourceReference
        {
            SourceKind = source.SourceKind,
            DrawingObjectId = source.DrawingObjectId,
            ModelId = source.ModelId
        }));
        snapshot.SourceObjectIds.AddRange(sourceSummary.SourceObjectIds);
        snapshot.ClassifiedDimensionType = DimensionGroupFactory.MapDomainDimensionType(snapshot.SourceKind, snapshot.GeometryKind);

        return snapshot;
    }

    private (DimensionSourceKind SourceKind, List<DimensionSourceReference> SourceReferences, List<int> SourceObjectIds) ResolveDimensionSourceSummary(
        StraightDimensionSet dimSet,
        IReadOnlyList<StraightDimension> segments)
    {
        var hasPart = false;
        var hasGrid = false;
        var sourceObjectIds = new HashSet<int>();
        var sourceReferences = new Dictionary<(DimensionSourceKind SourceKind, int? DrawingObjectId, int? ModelId), DimensionSourceReference>();

        CollectDimensionSourceSummary(dimSet.GetRelatedObjects(), ref hasPart, ref hasGrid, sourceReferences, sourceObjectIds);

        foreach (var segment in segments)
            CollectDimensionSourceSummary(segment.GetRelatedObjects(), ref hasPart, ref hasGrid, sourceReferences, sourceObjectIds);

        var sourceKind = (hasPart, hasGrid) switch
        {
            (true, false) => DimensionSourceKind.Part,
            (false, true) => DimensionSourceKind.Grid,
            _ => DimensionSourceKind.Unknown
        };

        return (
            sourceKind,
            sourceReferences.Values
                .OrderBy(static source => source.SourceKind)
                .ThenBy(static source => source.ModelId)
                .ThenBy(static source => source.DrawingObjectId)
                .ToList(),
            sourceObjectIds.OrderBy(static id => id).ToList());
    }

    private void CollectDimensionSourceSummary(
        DrawingObjectEnumerator? relatedObjects,
        ref bool hasPart,
        ref bool hasGrid,
        IDictionary<(DimensionSourceKind SourceKind, int? DrawingObjectId, int? ModelId), DimensionSourceReference> sourceReferences,
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

            if (DimensionRelatedObjectHelper.TryGetRelatedObjectId(relatedObject, out var sourceObjectId))
                sourceObjectIds.Add(sourceObjectId);

            if (TryCreateSourceReference(relatedObject, sourceKind, out var sourceReference))
            {
                sourceReferences[(sourceReference.SourceKind, sourceReference.DrawingObjectId, sourceReference.ModelId)] = sourceReference;
            }
        }
    }

    private static bool TryCreateSourceReference(
        object? relatedObject,
        DimensionSourceKind sourceKind,
        out DimensionSourceReference sourceReference)
    {
        int? drawingObjectId = null;
        int? modelId = null;

        if (DimensionRelatedObjectHelper.TryGetRelatedObjectId(relatedObject, out var sourceObjectId))
            drawingObjectId = sourceObjectId;

        if (relatedObject is Tekla.Structures.Drawing.ModelObject drawingModelObject)
        {
            var candidateModelId = drawingModelObject.ModelIdentifier.ID;
            if (candidateModelId > 0)
                modelId = candidateModelId;
        }

        if (!drawingObjectId.HasValue && !modelId.HasValue)
        {
            sourceReference = new DimensionSourceReference();
            return false;
        }

        sourceReference = new DimensionSourceReference
        {
            SourceKind = sourceKind,
            DrawingObjectId = drawingObjectId,
            ModelId = modelId
        };
        return true;
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

    private static DimensionSegmentInfo ProjectDimensionSegmentSnapshotToReadModel(TeklaDimensionSegmentSnapshot snapshot)
    {
        return new DimensionSegmentInfo
        {
            Id = snapshot.Id,
            StartX = snapshot.StartX,
            StartY = snapshot.StartY,
            EndX = snapshot.EndX,
            EndY = snapshot.EndY,
            Distance = snapshot.Distance,
            DirectionX = snapshot.DirectionX,
            DirectionY = snapshot.DirectionY,
            TopDirection = snapshot.TopDirection,
            Bounds = snapshot.Bounds == null ? null : CreateBoundsInfo(
                snapshot.Bounds.MinX,
                snapshot.Bounds.MinY,
                snapshot.Bounds.MaxX,
                snapshot.Bounds.MaxY),
            TextBounds = snapshot.TextBounds == null ? null : CreateBoundsInfo(
                snapshot.TextBounds.MinX,
                snapshot.TextBounds.MinY,
                snapshot.TextBounds.MaxX,
                snapshot.TextBounds.MaxY),
            DimensionLine = CopyLine(snapshot.DimensionLine),
            LeadLineMain = CopyLine(snapshot.LeadLineMain),
            LeadLineSecond = CopyLine(snapshot.LeadLineSecond)
        };
    }

    private static TeklaDimensionSegmentSnapshot BuildDimensionSegmentSnapshot(
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

        return new TeklaDimensionSegmentSnapshot
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

        var associationResolver = new DimensionSourceAssociationResolver(_model, new TeklaDrawingPartPointApi(_model));
        var builder = new DimensionContextBuilder(associationResolver);
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

        AttachLayoutPolicyDecisions(debug, contexts);
    }

    private static void AttachLayoutPolicyDecisions(
        DimensionReductionDebugResult debug,
        IReadOnlyDictionary<DimensionItem, DimensionContext> contexts)
    {
        var reducedItems = debug.Groups
            .SelectMany(static group => group.ReducedGroup.DimensionList)
            .Distinct()
            .Where(contexts.ContainsKey)
            .ToList();
        if (reducedItems.Count <= 1)
            return;

        var decisions = DimensionLayoutPolicyEvaluator.Evaluate(reducedItems, contexts);
        var itemsByDimensionId = reducedItems.ToDictionary(static item => item.DimensionId);
        foreach (var group in debug.Groups)
        {
            DimensionLayoutPolicyEvaluator.AttachCombineCandidates(itemsByDimensionId, decisions, group.CombineCandidates);
        }

        DimensionLayoutPolicyEvaluator.AttachRecommendedActions(decisions);

        foreach (var group in debug.Groups)
        {
            foreach (var item in group.Items)
            {
                if (decisions.TryGetValue(item.Item, out var decision))
                    item.LayoutPolicy = decision;
            }
        }
    }
}
