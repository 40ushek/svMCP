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

                CollectDimensionSourceCandidates(info.Candidates, dimSet.GetRelatedObjects(), "dimensionSet");
                foreach (var segment in EnumerateSegments(dimSet))
                    CollectDimensionSourceCandidates(info.Candidates, segment.GetRelatedObjects(), $"segment:{segment.GetIdentifier().ID}");

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
                    CollectRelatedTextDebug(candidateList, segment.GetRelatedObjects(), "segment", expectedText, segmentInfo.DimensionLine);
                    CollectRelatedTextDebug(candidateList, dimSet.GetRelatedObjects(), "dimensionSet", expectedText, segmentInfo.DimensionLine);
                    CollectNestedTextDebug(candidateList, segment, "segment.objects", expectedText, segmentInfo.DimensionLine);
                    CollectNestedTextDebug(candidateList, dimSet, "dimensionSet.objects", expectedText, segmentInfo.DimensionLine);
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
                    var polygon = TryCreateTextPolygon(segment, dimSet, info.DimensionLine);
                    if (polygon == null || polygon.Count < 4)
                        continue;

                    request.Shapes.Add(new DrawingDebugShape
                    {
                        Kind = "polygon",
                        ViewId = ownerViewId,
                        Points = polygon,
                        Color = normalizedColor,
                        LineType = "DashDot"
                    });

                    segmentCount++;
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

    private static void CollectRelatedTextDebug(
        List<RelatedTextCandidateDebugInfo> target,
        DrawingObjectEnumerator relatedObjects,
        string owner,
        string expectedText,
        DrawingLineInfo? dimensionLine)
    {
        while (relatedObjects.MoveNext())
        {
            if (!TryCreateRelatedTextCandidate(relatedObjects.Current, FrameTypes.None, out var candidateText, out var polygon))
                continue;

            var center = GetPolygonCenter(polygon);
            target.Add(new RelatedTextCandidateDebugInfo
            {
                Owner = owner,
                Type = relatedObjects.Current.GetType().FullName ?? relatedObjects.Current.GetType().Name,
                Text = candidateText ?? string.Empty,
                MatchesExpected = MatchesDimensionText(candidateText, expectedText),
                Score = dimensionLine == null ? double.MaxValue : ScoreTextPolygonAgainstDimensionLine(polygon, dimensionLine),
                CenterX = System.Math.Round(center.X, 3),
                CenterY = System.Math.Round(center.Y, 3)
            });
        }
    }

    private void CollectDimensionSourceCandidates(
        List<DimensionSourceCandidateInfo> target,
        DrawingObjectEnumerator? relatedObjects,
        string owner)
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

            target.Add(candidate);
        }
    }

    private static void CollectNestedTextDebug(
        List<RelatedTextCandidateDebugInfo> target,
        object ownerObject,
        string owner,
        string expectedText,
        DrawingLineInfo? dimensionLine)
    {
        foreach (var candidate in EnumerateNestedDrawingObjects(ownerObject))
        {
            if (!TryCreateRelatedTextCandidate(candidate, FrameTypes.None, out var candidateText, out var polygon))
                continue;

            var center = GetPolygonCenter(polygon);
            target.Add(new RelatedTextCandidateDebugInfo
            {
                Owner = owner,
                Type = candidate?.GetType().FullName ?? candidate?.GetType().Name ?? string.Empty,
                Text = candidateText ?? string.Empty,
                MatchesExpected = MatchesDimensionText(candidateText, expectedText),
                Score = dimensionLine == null ? double.MaxValue : ScoreTextPolygonAgainstDimensionLine(polygon, dimensionLine),
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

        Vector dirVector = (direction ?? "horizontal").ToLowerInvariant() switch
        {
            "vertical" or "v" => new Vector(1, 0, 0),
            "horizontal" or "h" => new Vector(0, 1, 0),
            _ => TryParseVector(direction) ?? new Vector(0, 1, 0)
        };

#pragma warning disable CS0618
        var attr = new StraightDimensionSet.StraightDimensionSetAttributes();
#pragma warning restore CS0618
        if (!string.IsNullOrWhiteSpace(attributesFile))
            attr.LoadAttributes(attributesFile);

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

    public PlaceControlDiagonalsResult PlaceControlDiagonals(int? viewId, double distance, string attributesFile)
    {
        var total = Stopwatch.StartNew();
        var result = new PlaceControlDiagonalsResult();
        var previousAutoFetch = DrawingEnumeratorBase.AutoFetch;
        DrawingEnumeratorBase.AutoFetch = false;

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

            var readGeometrySw = Stopwatch.StartNew();
            var sourcePoints = CollectDimensionSegmentPoints(targetView, out var dimensionsScanned);
            readGeometrySw.Stop();
            result.ReadGeometryMs = readGeometrySw.ElapsedMilliseconds;
            result.PartsScanned = dimensionsScanned;
            result.SourceDimensionsScanned = dimensionsScanned;
            result.CandidatePoints = sourcePoints.Count;

            if (sourcePoints.Count < 2)
            {
                result.Error = "Not enough dimension points. Add dimensions on the target view first.";
                result.TotalMs = total.ElapsedMilliseconds;
                return result;
            }

            var findExtremesSw = Stopwatch.StartNew();
            var hull = ConvexHull.Compute(sourcePoints).ToList();
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
#pragma warning disable CS0618
            var attributes = new StraightDimensionSet.StraightDimensionSetAttributes();
#pragma warning restore CS0618
            var normalizedAttributes = string.IsNullOrWhiteSpace(attributesFile) ? "standard" : attributesFile.Trim();
            attributes.LoadAttributes(normalizedAttributes);

            var diagonalsIntersect = pairs.Count == 2
                && SegmentsProperlyIntersect(pairs[0].Start, pairs[0].End, pairs[1].Start, pairs[1].End);

            var dimIds = new List<int>();
            for (var i = 0; i < pairs.Count; i++)
            {
                var pair = pairs[i];
                var pointList = new PointList { pair.Start, pair.End };
                var direction = BuildDiagonalOffsetDirection(pair.Start, pair.End);
                var actualDistance = (i == 1 && diagonalsIntersect) ? distance * 2.0 : distance;

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
