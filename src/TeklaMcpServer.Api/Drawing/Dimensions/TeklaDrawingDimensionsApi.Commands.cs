using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using TeklaMcpServer.Api.Algorithms.Geometry;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.DrawingPresentationModel;
using Tekla.Structures.DrawingPresentationModelInterface;
using Tekla.Structures.Geometry3d;
using PresentationConnection = Tekla.Structures.DrawingPresentationModelInterface.Connection;

namespace TeklaMcpServer.Api.Drawing;

public sealed partial class TeklaDrawingDimensionsApi
{
    public DimensionSourceDebugResult GetDimensionSourceDebug(int? viewId, int? dimensionId)
    {
        var activeDrawing = new DrawingHandler().GetActiveDrawing();
        if (activeDrawing == null)
            throw new DrawingNotOpenException();

        var previousAutoFetch = DrawingEnumeratorBase.AutoFetch;
        DrawingEnumeratorBase.AutoFetch = false;
        try
        {
            var partPointApi = new TeklaDrawingPartPointApi(_model);
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

            var result = new DimensionSourceDebugResult
            {
                ViewId = viewId
            };

            while (dimObjects.MoveNext())
            {
                if (dimObjects.Current is not StraightDimensionSet dimSet)
                    continue;

                var currentDimensionId = dimSet.GetIdentifier().ID;
                if (dimensionId.HasValue && currentDimensionId != dimensionId.Value)
                    continue;

                var info = new DimensionSourceDebugInfo
                {
                    DimensionId = currentDimensionId,
                    DimensionType = TryGetDimensionType(dimSet),
                    TeklaDimensionType = TryGetDimensionType(dimSet)
                };
                var ownerViewId = dimSet.GetView()?.GetIdentifier().ID;
                var dimensionInfo = BuildDimensionInfo(dimSet);
                info.MeasuredPoints.AddRange(dimensionInfo.MeasuredPoints.Select(static point => new DrawingPointInfo
                {
                    X = point.X,
                    Y = point.Y,
                    Order = point.Order
                }));

                CollectDimensionSourceCandidates(info.Candidates, dimSet.GetRelatedObjects(), "dimensionSet", ownerViewId, partPointApi);
                foreach (var segment in EnumerateSegments(dimSet))
                    CollectDimensionSourceCandidates(info.Candidates, segment.GetRelatedObjects(), $"segment:{segment.GetIdentifier().ID}", ownerViewId, partPointApi);
                var mapper = new DimensionPointObjectMapper();
                var pointMappings = mapper.Map(
                    info.MeasuredPoints,
                    info.Candidates,
                    BuildPreferredOwnersByPointOrder(info.MeasuredPoints, dimensionInfo.Segments));
                info.PointMappings.AddRange(pointMappings.Select(static mapping => new DimensionPointObjectMappingInfo
                {
                    Order = mapping.Point.Order,
                    X = mapping.Point.X,
                    Y = mapping.Point.Y,
                    Status = mapping.Status.ToString(),
                    MatchedOwner = mapping.MatchedCandidate?.Owner ?? string.Empty,
                    MatchedDrawingObjectId = mapping.MatchedCandidate?.DrawingObjectId,
                    MatchedModelId = mapping.MatchedCandidate?.ModelId,
                    MatchedType = mapping.MatchedCandidate?.Type ?? string.Empty,
                    MatchedSourceKind = mapping.MatchedCandidate?.SourceKind ?? string.Empty,
                    DistanceToGeometry = mapping.DistanceToGeometry,
                    NearestGeometryPoint = mapping.NearestGeometryPoint == null
                        ? null
                        : new DrawingPointInfo
                        {
                            X = mapping.NearestGeometryPoint.X,
                            Y = mapping.NearestGeometryPoint.Y,
                            Order = mapping.NearestGeometryPoint.Order
                        },
                    CandidateCount = mapping.CandidateCount,
                    Warning = mapping.Warning
                }));

                result.Dimensions.Add(info);
            }

            result.Total = result.Dimensions.Count;
            return result;
        }
        finally
        {
            DrawingEnumeratorBase.AutoFetch = previousAutoFetch;
        }
    }

    public DimensionTextPlacementDebugResult GetDimensionTextPlacementDebug(int? viewId, int? dimensionId)
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

            var result = new DimensionTextPlacementDebugResult
            {
                ViewId = viewId
            };
            using var presentationConnection = TryCreatePresentationConnection();

            while (dimObjects.MoveNext())
            {
                if (dimObjects.Current is not StraightDimensionSet dimSet)
                    continue;

                var currentDimensionId = dimSet.GetIdentifier().ID;
                if (dimensionId.HasValue && currentDimensionId != dimensionId.Value)
                    continue;

                var segments = EnumerateSegments(dimSet);
                var lineContext = TryCreateDimensionLineContext(segments, dimSet.Distance);
                var info = new DimensionTextPlacementDebugInfo
                {
                    DimensionId = currentDimensionId,
                    DimensionType = TryGetDimensionType(dimSet),
                    TextPlacing = TryGetDimensionTextPlacing(dimSet),
                    ShortDimension = TryGetShortDimension(dimSet),
                    PlacingDirectionSign = TryGetDimensionPlacingDirectionSign(dimSet),
                    LeftTagLineOffset = TryGetTagLineOffset(dimSet, left: true),
                    RightTagLineOffset = TryGetTagLineOffset(dimSet, left: false)
                };

                foreach (var segment in segments)
                {
                    var segmentInfo = BuildSegmentInfo(segment, dimSet, dimSet.Distance, lineContext);
                    var expectedText = string.Empty;
                    if (segment.GetView() is Tekla.Structures.Drawing.View ownerView)
                        expectedText = TryGetMeasuredValueText(segment, dimSet, ownerView) ?? string.Empty;

                    var candidateList = new List<RelatedTextCandidateDebugInfo>();
                    var runtimeTextBoxes = DimensionTextBoxCollector.Collect(segment, dimSet, FrameTypes.None);
                    CollectRuntimeTextDebug(candidateList, runtimeTextBoxes, expectedText, segmentInfo.DimensionLine);
                    CollectPresentationTextDebug(candidateList, presentationConnection, segment.GetIdentifier().ID, "presentation:segment", expectedText, segmentInfo.DimensionLine);
                    CollectPresentationTextDebug(candidateList, presentationConnection, currentDimensionId, "presentation:dimensionSet", expectedText, segmentInfo.DimensionLine);

                    var selectedSource = candidateList.Any(static candidate => candidate.MatchesExpected)
                        ? "runtime"
                        : "fallback";

                    info.Segments.Add(new DimensionSegmentTextPlacementDebugInfo
                    {
                        SegmentId = segment.GetIdentifier().ID,
                        ExpectedText = expectedText,
                        DimensionLine = segmentInfo.DimensionLine,
                        SelectedSource = selectedSource,
                        RelatedTextCandidates = candidateList
                            .OrderBy(static candidate => candidate.Score)
                            .ToList()
                    });
                }

                result.Dimensions.Add(info);
            }

            result.Total = result.Dimensions.Count;
            return result;
        }
        finally
        {
            DrawingEnumeratorBase.AutoFetch = previousAutoFetch;
        }
    }

    public DrawDimensionTextBoxesResult DrawDimensionTextBoxes(int? viewId, int? dimensionId, string color, string group)
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

            var normalizedGroup = string.IsNullOrWhiteSpace(group) ? "dimension-text-boxes" : group.Trim();
            var normalizedColor = string.IsNullOrWhiteSpace(color) ? "Yellow" : color.Trim();
            var request = new DrawingDebugOverlayRequest
            {
                Group = normalizedGroup,
                ClearGroupFirst = true
            };

            var dimensionIds = new HashSet<int>();
            var segmentCount = 0;
            while (dimObjects.MoveNext())
            {
                if (dimObjects.Current is not StraightDimensionSet dimSet)
                    continue;

                var currentDimensionId = dimSet.GetIdentifier().ID;
                if (dimensionId.HasValue && currentDimensionId != dimensionId.Value)
                    continue;

                var ownerView = dimSet.GetView();
                var ownerViewId = ownerView?.GetIdentifier().ID;
                var segments = EnumerateSegments(dimSet);
                var lineContext = TryCreateDimensionLineContext(segments, dimSet.Distance);
                foreach (var segment in segments)
                {
                    var info = BuildSegmentInfo(segment, dimSet, dimSet.Distance, lineContext);
                    var polygons = DimensionTextBoxCollector.Collect(segment, dimSet, FrameTypes.None)
                        .Select(static candidate => candidate.Polygon)
                        .Where(static polygon => polygon.Count >= 4)
                        .ToList();
                    if (polygons.Count == 0)
                    {
                        var fallback = TryCreateTextPolygon(segment, dimSet, info.DimensionLine);
                        if (fallback != null && fallback.Count >= 4)
                            polygons.Add(fallback);
                    }

                    if (polygons.Count == 0)
                        continue;

                    foreach (var polygon in polygons)
                    {
                        request.Shapes.Add(new DrawingDebugShape
                        {
                            Kind = "polygon",
                            ViewId = ownerViewId,
                            Points = polygon,
                            Color = normalizedColor,
                            LineType = "DashDot"
                        });

                        segmentCount++;
                    }
                    dimensionIds.Add(currentDimensionId);
                }
            }

            var overlayApi = new TeklaDrawingDebugOverlayApi();
            if (request.Shapes.Count == 0)
            {
                var cleared = overlayApi.ClearOverlay(normalizedGroup);
                return new DrawDimensionTextBoxesResult
                {
                    Group = normalizedGroup,
                    ClearedCount = cleared.ClearedCount,
                    CreatedCount = 0,
                    DimensionCount = 0,
                    SegmentCount = 0
                };
            }

            var overlayResult = overlayApi.DrawOverlay(JsonSerializer.Serialize(request));
            return new DrawDimensionTextBoxesResult
            {
                Group = overlayResult.Group,
                ClearedCount = overlayResult.ClearedCount,
                CreatedCount = overlayResult.CreatedCount,
                CreatedIds = overlayResult.CreatedIds,
                DimensionCount = dimensionIds.Count,
                SegmentCount = segmentCount
            };
        }
        finally
        {
            DrawingEnumeratorBase.AutoFetch = previousAutoFetch;
        }
    }

    private void CollectDimensionSourceCandidates(
        List<DimensionSourceCandidateInfo> target,
        DrawingObjectEnumerator? relatedObjects,
        string owner,
        int? ownerViewId,
        IDrawingPartPointApi partPointApi)
    {
        if (relatedObjects == null)
            return;

        while (relatedObjects.MoveNext())
        {
            var current = relatedObjects.Current;
            var candidate = new DimensionSourceCandidateInfo
            {
                Owner = owner,
                Type = current?.GetType().Name ?? string.Empty,
                SourceKind = ResolveRelatedObjectSourceKind(current).ToString()
            };

            if (current is DrawingObject drawingObject)
                candidate.DrawingObjectId = drawingObject.GetIdentifier().ID;

            if (current is Tekla.Structures.Drawing.ModelObject drawingModelObject)
            {
                candidate.ModelId = drawingModelObject.ModelIdentifier.ID;
                candidate.ResolvedModelType = TrySelectRelatedModelObject(drawingModelObject)?.GetType().Name ?? string.Empty;
            }

            PopulateCandidateGeometry(candidate, ownerViewId, partPointApi);
            target.Add(candidate);
        }
    }

    private static IReadOnlyDictionary<int, IReadOnlyList<string>> BuildPreferredOwnersByPointOrder(
        IReadOnlyList<DrawingPointInfo> measuredPoints,
        IReadOnlyList<DimensionSegmentInfo> segments)
    {
        var result = new Dictionary<int, IReadOnlyList<string>>();
        foreach (var point in measuredPoints)
        {
            var owners = segments
                .Where(segment => PointMatchesSegmentEndpoint(point, segment))
                .Select(static segment => $"segment:{segment.Id}")
                .Distinct()
                .OrderBy(static owner => owner)
                .ToList();
            if (owners.Count > 0)
                result[point.Order] = owners;
        }

        return result;
    }

    private static bool PointMatchesSegmentEndpoint(DrawingPointInfo point, DimensionSegmentInfo segment, double tolerance = 0.5)
    {
        return MatchesPoint(point, segment.StartX, segment.StartY, tolerance)
            || MatchesPoint(point, segment.EndX, segment.EndY, tolerance);
    }

    private static bool MatchesPoint(DrawingPointInfo point, double x, double y, double tolerance)
    {
        return System.Math.Abs(point.X - x) <= tolerance
            && System.Math.Abs(point.Y - y) <= tolerance;
    }

    private void PopulateCandidateGeometry(DimensionSourceCandidateInfo candidate, int? ownerViewId, IDrawingPartPointApi partPointApi)
    {
        if (!ownerViewId.HasValue)
        {
            candidate.GeometryWarnings.Add("view_unavailable");
            return;
        }

        if (!candidate.ModelId.HasValue || candidate.ModelId.Value <= 0)
        {
            candidate.GeometryWarnings.Add("model_id_unavailable");
            return;
        }

        if (!string.Equals(candidate.SourceKind, DimensionSourceKind.Part.ToString(), System.StringComparison.Ordinal))
        {
            candidate.GeometryWarnings.Add("geometry_probe_not_supported_for_source_kind");
            return;
        }

        candidate.GeometrySource = "part_points";

        GetPartPointsResult partPoints;
        try
        {
            partPoints = partPointApi.GetPartPointsInView(ownerViewId.Value, candidate.ModelId.Value);
        }
        catch
        {
            candidate.GeometryWarnings.Add("part_points_failed");
            return;
        }

        if (!partPoints.Success)
        {
            candidate.GeometryWarnings.Add(partPoints.Error ?? "part_points_unavailable");
            return;
        }

        foreach (var point in partPoints.Points.Where(static point => point.Point.Length >= 2))
        {
            candidate.GeometryPoints.Add(new DrawingPointInfo
            {
                X = System.Math.Round(point.Point[0], 3),
                Y = System.Math.Round(point.Point[1], 3),
                Order = point.Index
            });
        }

        candidate.GeometryPointCount = candidate.GeometryPoints.Count;
        if (candidate.GeometryPoints.Count == 0)
        {
            candidate.GeometryWarnings.Add("geometry_points_empty");
            return;
        }

        candidate.HasGeometry = true;
        candidate.GeometryBounds = TeklaDrawingDimensionsApi.CreateBoundsInfo(
            candidate.GeometryPoints.Min(static point => point.X),
            candidate.GeometryPoints.Min(static point => point.Y),
            candidate.GeometryPoints.Max(static point => point.X),
            candidate.GeometryPoints.Max(static point => point.Y));
    }

    private static void CollectRuntimeTextDebug(
        List<RelatedTextCandidateDebugInfo> target,
        IReadOnlyList<DimensionTextBoxCandidate> candidates,
        string expectedText,
        DrawingLineInfo? dimensionLine)
    {
        foreach (var candidate in candidates)
        {
            var center = GetPolygonCenter(candidate.Polygon);
            target.Add(new RelatedTextCandidateDebugInfo
            {
                Owner = candidate.Owner,
                Type = candidate.Type,
                Text = candidate.Text,
                MatchesExpected = MatchesDimensionText(candidate.Text, expectedText),
                Score = dimensionLine == null ? double.MaxValue : ScoreTextPolygonAgainstDimensionLine(candidate.Polygon, dimensionLine),
                CenterX = System.Math.Round(center.X, 3),
                CenterY = System.Math.Round(center.Y, 3)
            });
        }
    }

    private static PresentationConnection? TryCreatePresentationConnection()
    {
        try
        {
            return new PresentationConnection();
        }
        catch
        {
            return null;
        }
    }

    private static void CollectPresentationTextDebug(
        List<RelatedTextCandidateDebugInfo> target,
        PresentationConnection? connection,
        int objectId,
        string owner,
        string expectedText,
        DrawingLineInfo? dimensionLine)
    {
        if (connection == null)
            return;

        Segment? segment;
        try
        {
            segment = connection.Service.GetObjectPresentation(objectId);
        }
        catch
        {
            return;
        }

        if (segment == null)
            return;

        foreach (var textPrimitive in EnumeratePresentationTextPrimitives(segment))
        {
            var centerX = textPrimitive.Position.X;
            var centerY = textPrimitive.Position.Y;
            var score = dimensionLine == null
                ? double.MaxValue
                : ScorePresentationTextAgainstDimensionLine(textPrimitive, dimensionLine);

            target.Add(new RelatedTextCandidateDebugInfo
            {
                Owner = owner,
                Type = nameof(TextPrimitive),
                Text = textPrimitive.Text ?? string.Empty,
                MatchesExpected = MatchesDimensionText(textPrimitive.Text, expectedText),
                Score = score,
                CenterX = System.Math.Round(centerX, 3),
                CenterY = System.Math.Round(centerY, 3)
            });
        }
    }

    private static IEnumerable<TextPrimitive> EnumeratePresentationTextPrimitives(Segment segment)
    {
        foreach (var primitive in segment.Primitives)
        {
            foreach (var textPrimitive in EnumeratePresentationTextPrimitives(primitive))
                yield return textPrimitive;
        }
    }

    private static IEnumerable<TextPrimitive> EnumeratePresentationTextPrimitives(PrimitiveBase primitive)
    {
        switch (primitive)
        {
            case TextPrimitive textPrimitive:
                yield return textPrimitive;
                yield break;
            case Segment nestedSegment:
                foreach (var nestedPrimitive in nestedSegment.Primitives)
                foreach (var nestedTextPrimitive in EnumeratePresentationTextPrimitives(nestedPrimitive))
                    yield return nestedTextPrimitive;
                yield break;
            case PrimitiveGroup group:
                foreach (var groupedPrimitive in group.Primitives)
                foreach (var groupedTextPrimitive in EnumeratePresentationTextPrimitives(groupedPrimitive))
                    yield return groupedTextPrimitive;
                yield break;
        }
    }

    private static double ScorePresentationTextAgainstDimensionLine(
        TextPrimitive textPrimitive,
        DrawingLineInfo dimensionLine)
    {
        if (!TryNormalizeDirection(
                dimensionLine.EndX - dimensionLine.StartX,
                dimensionLine.EndY - dimensionLine.StartY,
                out var axis))
        {
            return double.MaxValue;
        }

        var lineCenterX = (dimensionLine.StartX + dimensionLine.EndX) / 2.0;
        var lineCenterY = (dimensionLine.StartY + dimensionLine.EndY) / 2.0;
        var normalX = -axis.Y;
        var normalY = axis.X;
        var alongDelta = System.Math.Abs(((textPrimitive.Position.X - lineCenterX) * axis.X) + ((textPrimitive.Position.Y - lineCenterY) * axis.Y));
        var normalDelta = System.Math.Abs(((textPrimitive.Position.X - lineCenterX) * normalX) + ((textPrimitive.Position.Y - lineCenterY) * normalY));
        return (normalDelta * 1000.0) + alongDelta;
    }

    private static string TryGetShortDimension(StraightDimensionSet dimSet)
    {
        try
        {
            if (dimSet.Attributes is StraightDimensionSet.StraightDimensionSetAttributes attributes)
                return attributes.ShortDimension.ToString();
        }
        catch
        {
        }

        return string.Empty;
    }

    public MoveDimensionResult MoveDimension(int dimensionId, double delta)
    {
        var activeDrawing = new DrawingHandler().GetActiveDrawing();
        if (activeDrawing == null)
            throw new DrawingNotOpenException();

        var previousAutoFetch = DrawingEnumeratorBase.AutoFetch;
        DrawingEnumeratorBase.AutoFetch = false;
        try
        {
            StraightDimensionSet? dimSet = null;
            var allDims = activeDrawing.GetSheet().GetAllObjects(typeof(StraightDimensionSet));
            while (allDims.MoveNext())
            {
                if (allDims.Current is StraightDimensionSet ds && ds.GetIdentifier().ID == dimensionId)
                {
                    dimSet = ds;
                    break;
                }
            }

            if (dimSet == null)
                throw new System.Exception($"DimensionSet {dimensionId} not found");

            dimSet.Distance += delta;
            dimSet.Modify();
            activeDrawing.CommitChanges();
            return new MoveDimensionResult { Moved = true, DimensionId = dimensionId, NewDistance = dimSet.Distance };
        }
        finally
        {
            DrawingEnumeratorBase.AutoFetch = previousAutoFetch;
        }
    }

    public CreateDimensionResult CreateDimension(int viewId, double[] points, string direction, double distance, string attributesFile)
    {
        var activeDrawing = new DrawingHandler().GetActiveDrawing();
        if (activeDrawing == null)
            throw new DrawingNotOpenException();

        var view = EnumerateViews(activeDrawing).FirstOrDefault(v => v.GetIdentifier().ID == viewId)
            ?? throw new ViewNotFoundException(viewId);

        if (points == null || points.Length < 6 || points.Length % 3 != 0)
            return new CreateDimensionResult { Error = "points must be a flat array [x0,y0,z0, x1,y1,z1, ...] with at least 2 points" };

        var pointList = new PointList();
        for (int i = 0; i + 2 < points.Length; i += 3)
            pointList.Add(new Point(points[i], points[i + 1], points[i + 2]));

        var dirVector = DimensionCreatePlacementHelper.ResolveDirection(direction);
        var attr = DimensionCreatePlacementHelper.CreateAttributes(attributesFile);

        var dim = new StraightDimensionSetHandler().CreateDimensionSet(
            view, pointList, dirVector, distance, attr);

        if (dim == null)
            return new CreateDimensionResult { Error = "CreateDimensionSet returned null" };

        activeDrawing.CommitChanges("(MCP) CreateDimension");

        return new CreateDimensionResult
        {
            Created = true,
            DimensionId = dim.GetIdentifier().ID,
            ViewId = viewId,
            PointCount = pointList.Count
        };
    }

    public DeleteDimensionResult DeleteDimension(int dimensionId)
    {
        var drawingHandler = new DrawingHandler();
        var activeDrawing = drawingHandler.GetActiveDrawing();
        if (activeDrawing == null)
        {
            return new DeleteDimensionResult
            {
                HasActiveDrawing = false,
                Deleted = false,
                DimensionId = dimensionId
            };
        }

        var deleted = false;
        var viewEnum = activeDrawing.GetSheet().GetViews();
        while (viewEnum.MoveNext())
        {
            if (viewEnum.Current is not Tekla.Structures.Drawing.View view)
                continue;

            var dimEnum = view.GetAllObjects(new[] { typeof(StraightDimensionSet) });
            while (dimEnum.MoveNext())
            {
                if (dimEnum.Current is not StraightDimensionSet dimensionSet)
                    continue;
                if (dimensionSet.GetIdentifier().ID != dimensionId)
                    continue;

                dimensionSet.Delete();
                activeDrawing.CommitChanges();
                deleted = true;
                break;
            }

            if (deleted)
                break;
        }

        return new DeleteDimensionResult
        {
            HasActiveDrawing = true,
            Deleted = deleted,
            DimensionId = dimensionId
        };
    }

    public CombineDimensionsResult CombineDimensions(int? viewId, IReadOnlyList<int>? dimensionIds, bool previewOnly)
    {
        var activeDrawing = new DrawingHandler().GetActiveDrawing();
        if (activeDrawing == null)
            throw new DrawingNotOpenException();

        var normalizedDimensionIds = dimensionIds?
            .Where(static id => id > 0)
            .Distinct()
            .ToList();
        var debug = GetDimensionGroupReductionDebug(viewId);
        var candidates = DimensionCombineActionPlanner.BuildCandidates(debug, normalizedDimensionIds);
        var result = new CombineDimensionsResult
        {
            PreviewOnly = previewOnly,
            CandidateCount = candidates.Count
        };

        foreach (var candidate in candidates)
        {
            if (!candidate.CanCombine)
            {
                result.Skipped.Add(CreateCombineCandidateResult(candidate, previewOnly, combined: false, createdDimensionId: null, reasonOverride: candidate.Reason));
                continue;
            }

            if (previewOnly)
            {
                result.Combined.Add(CreateCombineCandidateResult(candidate, previewOnly: true, combined: false, createdDimensionId: null));
                continue;
            }

            var applyResult = TryApplyCombineCandidate(activeDrawing, candidate);
            if (!applyResult.Success)
            {
                result.Skipped.Add(CreateCombineCandidateResult(candidate, previewOnly: false, combined: false, createdDimensionId: null, reasonOverride: applyResult.Reason));
                continue;
            }

            result.Combined.Add(CreateCombineCandidateResult(
                candidate,
                previewOnly: false,
                combined: true,
                createdDimensionId: applyResult.CreatedDimensionId,
                deletedDimensionIdsOverride: candidate.DimensionIds));
        }

        result.CombinedCount = result.Combined.Count(static item => item.Combined);
        result.SkippedCount = result.Skipped.Count;
        return result;
    }

    private static CombineDimensionCandidateResult CreateCombineCandidateResult(
        DimensionCombineActionCandidate candidate,
        bool previewOnly,
        bool combined,
        int? createdDimensionId,
        IReadOnlyList<int>? deletedDimensionIdsOverride = null,
        string? reasonOverride = null)
    {
        var result = new CombineDimensionCandidateResult
        {
            ViewId = candidate.ViewId,
            ViewType = candidate.ViewType,
            DimensionType = candidate.DimensionType,
            PacketIndex = candidate.PacketIndex,
            BaseDimensionId = candidate.BaseDimensionId,
            ConnectivityMode = candidate.ConnectivityMode,
            PreviewOnly = previewOnly,
            Combined = combined,
            CreatedDimensionId = createdDimensionId,
            Distance = candidate.Preview?.Distance ?? 0,
            Reason = reasonOverride ?? candidate.Reason
        };

        result.DimensionIds.AddRange(candidate.DimensionIds);
        result.BlockingReasons.AddRange(candidate.BlockingReasons);
        if (deletedDimensionIdsOverride != null)
            result.DeletedDimensionIds.AddRange(deletedDimensionIdsOverride);

        if (candidate.Preview != null)
        {
            foreach (var point in candidate.Preview.PointList.OrderBy(static point => point.Order))
            {
                result.PointList.Add(new DrawingPointInfo
                {
                    X = point.X,
                    Y = point.Y,
                    Order = point.Order
                });
            }
        }

        return result;
    }

    private (bool Success, int? CreatedDimensionId, string Reason) TryApplyCombineCandidate(
        Tekla.Structures.Drawing.Drawing activeDrawing,
        DimensionCombineActionCandidate candidate)
    {
        if (candidate.Preview == null)
            return (false, null, "combine_preview_unavailable");

        var sourceDimensions = FindDimensionSetsById(activeDrawing, candidate.DimensionIds);
        if (sourceDimensions.Count != candidate.DimensionIds.Count)
        {
            var missing = candidate.DimensionIds.Where(id => !sourceDimensions.ContainsKey(id)).OrderBy(static id => id);
            return (false, null, $"source_dimensions_not_found:{string.Join(",", missing)}");
        }

        if (!sourceDimensions.TryGetValue(candidate.BaseDimensionId, out var baseDimensionSet))
            return (false, null, "base_dimension_not_found");

        if (baseDimensionSet.GetView() is not Tekla.Structures.Drawing.View view)
            return (false, null, "base_view_not_found");

        if (!TryResolveCombineOffsetVector(baseDimensionSet, out var offsetVector))
            return (false, null, "combine_offset_vector_unavailable");

        var attributes = TryGetCombineAttributes(baseDimensionSet);
        var pointList = CreateCombinePointList(candidate.Preview);
        if (pointList.Count < 2)
            return (false, null, "combine_preview_has_too_few_points");

        StraightDimensionSet? created = null;
        try
        {
            created = new StraightDimensionSetHandler().CreateDimensionSet(
                view,
                pointList,
                offsetVector,
                candidate.Preview.Distance,
                attributes);

            if (created == null)
                return (false, null, "CreateDimensionSet returned null");

            foreach (var dimensionSet in sourceDimensions.Values)
                dimensionSet.Delete();

            activeDrawing.CommitChanges("(MCP) CombineDimensions");
            return (true, created.GetIdentifier().ID, string.Empty);
        }
        catch (System.Exception ex)
        {
            if (created != null)
            {
                try
                {
                    created.Delete();
                    activeDrawing.CommitChanges("(MCP) RollbackCombineDimensions");
                }
                catch
                {
                }
            }

            return (false, null, ex.Message);
        }
    }

    private static Dictionary<int, StraightDimensionSet> FindDimensionSetsById(
        Tekla.Structures.Drawing.Drawing activeDrawing,
        IReadOnlyCollection<int> dimensionIds)
    {
        var result = new Dictionary<int, StraightDimensionSet>();
        if (dimensionIds.Count == 0)
            return result;

        var idSet = new HashSet<int>(dimensionIds);
        var allDims = activeDrawing.GetSheet().GetAllObjects(typeof(StraightDimensionSet));
        while (allDims.MoveNext())
        {
            if (allDims.Current is not StraightDimensionSet ds)
                continue;

            var id = ds.GetIdentifier().ID;
            if (!idSet.Contains(id))
                continue;

            result[id] = ds;
        }

        return result;
    }

    private static PointList CreateCombinePointList(DimensionCombinePreviewDebugInfo preview)
    {
        var pointList = new PointList();
        foreach (var point in preview.PointList.OrderBy(static point => point.Order))
            pointList.Add(new Point(point.X, point.Y, 0.0));

        return pointList;
    }

    private static StraightDimensionSet.StraightDimensionSetAttributes TryGetCombineAttributes(StraightDimensionSet baseDimensionSet)
    {
        try
        {
            if (baseDimensionSet.Attributes is StraightDimensionSet.StraightDimensionSetAttributes attributes)
                return attributes;
        }
        catch
        {
        }

        return DimensionCreatePlacementHelper.CreateAttributes(null);
    }

    private bool TryResolveCombineOffsetVector(StraightDimensionSet baseDimensionSet, out Vector vector)
    {
        vector = default!;

        try
        {
            var info = BuildDimensionInfo(baseDimensionSet);
            if (TryNormalizeDirection(info.DirectionX, info.DirectionY, out var direction) &&
                info.TopDirection != 0)
            {
                var upX = -direction.Y * info.TopDirection;
                var upY = direction.X * info.TopDirection;
                vector = new Vector(upX, upY, 0.0);
                return true;
            }
        }
        catch
        {
        }

        var firstSegment = EnumerateSegments(baseDimensionSet).FirstOrDefault();
        if (firstSegment == null)
            return false;

        if (!TryGetUpDirection(firstSegment, out var upDirection))
            return false;

        vector = new Vector(upDirection.X, upDirection.Y, 0.0);
        return true;
    }

    public PlaceControlDiagonalsResult PlaceControlDiagonals(int? viewId, double distance, string attributesFile, int[] includeMaterialTypes)
    {
        var total = Stopwatch.StartNew();
        var result = new PlaceControlDiagonalsResult();
        var previousAutoFetch = DrawingEnumeratorBase.AutoFetch;

        try
        {
            var drawingHandler = new DrawingHandler();
            var activeDrawing = drawingHandler.GetActiveDrawing();
            if (activeDrawing == null)
                throw new DrawingNotOpenException();

            var selectViewSw = Stopwatch.StartNew();
            var targetView = ResolveTargetView(activeDrawing, viewId);
            selectViewSw.Stop();

            result.ViewId = targetView.GetIdentifier().ID;
            result.ViewType = targetView.ViewType.ToString();
            result.SelectViewMs = selectViewSw.ElapsedMilliseconds;

            // Collect part geometry before disabling AutoFetch — view.GetObjects() requires it enabled
            var readGeometrySw = Stopwatch.StartNew();
            var partGeometryApi = new TeklaDrawingPartGeometryApi(_model);
            var parts = partGeometryApi.GetAllPartsGeometryInView(result.ViewId);
            var filteredParts = includeMaterialTypes.Length == 0
                ? parts
                : parts.Where(p => System.Array.IndexOf(includeMaterialTypes, p.MaterialType) >= 0).ToList();
            var sourcePoints = filteredParts
                .SelectMany(p => p.SolidVertices)
                .Where(v => v.Length >= 2)
                .Select(v => new Point(v[0], v[1], v.Length > 2 ? v[2] : 0.0))
                .ToList();
            readGeometrySw.Stop();

            DrawingEnumeratorBase.AutoFetch = false;
            result.ReadGeometryMs = readGeometrySw.ElapsedMilliseconds;
            result.PartsScanned = filteredParts.Count;
            result.SourceDimensionsScanned = filteredParts.Count;
            result.CandidatePoints = sourcePoints.Count;

            if (sourcePoints.Count < 2)
            {
                result.Error = $"Not enough geometry points (found {sourcePoints.Count} from {filteredParts.Count} structural parts).";
                result.TotalMs = total.ElapsedMilliseconds;
                return result;
            }

            var findExtremesSw = Stopwatch.StartNew();
            var hull = SimplifyHull(ConvexHull.Compute(sourcePoints).ToList());
            if (hull.Count < 2)
            {
                findExtremesSw.Stop();
                result.FindExtremesMs = findExtremesSw.ElapsedMilliseconds;
                result.Error = "Convex hull has fewer than 2 points.";
                result.TotalMs = total.ElapsedMilliseconds;
                return result;
            }

            var primary = FarthestPointPair.Find(hull);
            var rectangleLike = IsRectangleLikeHull(hull);
            var requestedDiagonalCount = rectangleLike ? 1 : 2;

            var pairs = new List<(Point Start, Point End)>
            {
                (primary.First, primary.Second)
            };

            if (requestedDiagonalCount > 1
                && TryFindSecondaryDiagonal(hull, primary.First, primary.Second, out var secondary))
            {
                pairs.Add(secondary);
            }

            // Normalize direction: always bottom (lower Y) → top (higher Y) in view coordinates
            for (var i = 0; i < pairs.Count; i++)
            {
                pairs[i] = DimensionDiagonalPlacementHelper.NormalizeBottomToTop(pairs[i]);
            }

            findExtremesSw.Stop();
            result.FindExtremesMs = findExtremesSw.ElapsedMilliseconds;
            result.RectangleLike = rectangleLike;
            result.RequestedDiagonalCount = requestedDiagonalCount;

            var start = primary.First;
            var end = primary.Second;
            result.StartPoint = [start.X, start.Y, start.Z];
            result.EndPoint = [end.X, end.Y, end.Z];
            result.FarthestDistance = System.Math.Round(System.Math.Sqrt(primary.DistanceSquared), 3);

            var createSw = Stopwatch.StartNew();
            var attributes = DimensionDiagonalPlacementHelper.CreateAttributes(attributesFile);

            var diagonalsIntersect = pairs.Count == 2
                && SegmentsProperlyIntersect(pairs[0].Start, pairs[0].End, pairs[1].Start, pairs[1].End);

            var dimIds = new List<int>();
            for (var i = 0; i < pairs.Count; i++)
            {
                var pair = pairs[i];
                var pointList = new PointList { pair.Start, pair.End };
                var direction = BuildDiagonalOffsetDirection(pair.Start, pair.End);
                var actualDistance = DimensionDiagonalPlacementHelper.ResolveDistance(distance, i, diagonalsIntersect);

                var dim = new StraightDimensionSetHandler().CreateDimensionSet(
                    targetView,
                    pointList,
                    direction,
                    actualDistance,
                    attributes);
                if (dim == null)
                    continue;

                dimIds.Add(dim.GetIdentifier().ID);
            }

            createSw.Stop();
            result.CreateMs = createSw.ElapsedMilliseconds;

            if (dimIds.Count == 0)
            {
                result.Error = "CreateDimensionSet returned null for all requested diagonals.";
                result.TotalMs = total.ElapsedMilliseconds;
                return result;
            }

            var commitSw = Stopwatch.StartNew();
            activeDrawing.CommitChanges("(MCP) PlaceControlDiagonals");
            commitSw.Stop();

            result.Created = true;
            result.CreatedCount = dimIds.Count;
            result.DimensionId = dimIds[0];
            result.DimensionIds = dimIds.ToArray();
            result.CommitMs = commitSw.ElapsedMilliseconds;
            result.TotalMs = total.ElapsedMilliseconds;
            return result;
        }
        finally
        {
            DrawingEnumeratorBase.AutoFetch = previousAutoFetch;
        }
    }
}
