using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
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
        var dimensions = DimensionStableReadHelper.ReadStable(
            () => ReadDimensionSourceDebugInfosCore(viewId, dimensionId),
            BuildDimensionSourceDebugFingerprint);

        var result = new DimensionSourceDebugResult
        {
            ViewId = viewId,
            Total = dimensions.Count
        };
        result.Dimensions.AddRange(dimensions);
        return result;
    }

    private List<DimensionSourceDebugInfo> ReadDimensionSourceDebugInfosCore(int? viewId, int? dimensionId)
    {
        var activeDrawing = new DrawingHandler().GetActiveDrawing();
        if (activeDrawing == null)
            throw new DrawingNotOpenException();

        var previousAutoFetch = DrawingEnumeratorBase.AutoFetch;
        DrawingEnumeratorBase.AutoFetch = false;
        try
        {
            var partPointApi = new TeklaDrawingPartPointApi(_model);
            var associationResolver = new DimensionSourceAssociationResolver(_model, partPointApi);
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

            var result = new List<DimensionSourceDebugInfo>();

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
                var dimensionSnapshot = BuildDimensionSnapshot(dimSet);
                var association = associationResolver.Resolve(dimSet, dimensionSnapshot);
                info.MeasuredPoints.AddRange(association.MeasuredPoints.Select(static point => new DrawingPointInfo
                {
                    X = point.X,
                    Y = point.Y,
                    Order = point.Order
                }));
                info.Candidates.AddRange(association.Candidates);
                info.PointMappings.AddRange(association.PointMappings.Select(static mapping => new DimensionPointObjectMappingInfo
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

                result.Add(info);
            }

            return result;
        }
        finally
        {
            DrawingEnumeratorBase.AutoFetch = previousAutoFetch;
        }
    }

    public DimensionTextPlacementDebugResult GetDimensionTextPlacementDebug(int? viewId, int? dimensionId)
    {
        var dimensions = DimensionStableReadHelper.ReadStable(
            () => ReadDimensionTextPlacementDebugInfosCore(viewId, dimensionId),
            BuildDimensionTextPlacementDebugFingerprint);

        var result = new DimensionTextPlacementDebugResult
        {
            ViewId = viewId,
            Total = dimensions.Count
        };
        result.Dimensions.AddRange(dimensions);
        return result;
    }

    private List<DimensionTextPlacementDebugInfo> ReadDimensionTextPlacementDebugInfosCore(int? viewId, int? dimensionId)
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

            var result = new List<DimensionTextPlacementDebugInfo>();
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
                    var segmentSnapshot = BuildDimensionSegmentSnapshot(segment, dimSet, dimSet.Distance, lineContext);
                    var expectedText = string.Empty;
                    if (segment.GetView() is Tekla.Structures.Drawing.View ownerView)
                        expectedText = TryGetMeasuredValueText(segment, dimSet, ownerView) ?? string.Empty;

                    var candidateList = new List<RelatedTextCandidateDebugInfo>();
                    var runtimeTextBoxes = DimensionTextBoxCollector.Collect(segment, dimSet, FrameTypes.None);
                    CollectRuntimeTextDebug(candidateList, runtimeTextBoxes, expectedText, segmentSnapshot.DimensionLine);
                    CollectPresentationTextDebug(candidateList, presentationConnection, segment.GetIdentifier().ID, "presentation:segment", expectedText, segmentSnapshot.DimensionLine);
                    CollectPresentationTextDebug(candidateList, presentationConnection, currentDimensionId, "presentation:dimensionSet", expectedText, segmentSnapshot.DimensionLine);

                    var selectedSource = candidateList.Any(static candidate => candidate.MatchesExpected)
                        ? "runtime"
                        : "fallback";

                    info.Segments.Add(new DimensionSegmentTextPlacementDebugInfo
                    {
                        SegmentId = segment.GetIdentifier().ID,
                        ExpectedText = expectedText,
                        DimensionLine = segmentSnapshot.DimensionLine,
                        SelectedSource = selectedSource,
                        RelatedTextCandidates = candidateList
                            .OrderBy(static candidate => candidate.Score)
                            .ToList()
                    });
                }

                result.Add(info);
            }

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
            var normalizedGroup = string.IsNullOrWhiteSpace(group) ? "dimension-text-boxes" : group.Trim();
            var normalizedColor = string.IsNullOrWhiteSpace(color) ? "Yellow" : color.Trim();
            var request = new DrawingDebugOverlayRequest
            {
                Group = normalizedGroup,
                ClearGroupFirst = true
            };

            var dimensionIds = new HashSet<int>();
            var segmentCount = 0;
            var presentationTextBoxDiagnostics = new List<DimensionPresentationTextBoxDebugInfo>();
            var presentationDiagnostics = new List<string>();
            using var presentationConnection = TryCreatePresentationConnection();
            presentationDiagnostics.Add(presentationConnection == null ? "presentation=null" : "presentation=connected");

            Tekla.Structures.Drawing.View? targetView = null;
            if (viewId.HasValue)
                targetView = EnumerateViews(activeDrawing).FirstOrDefault(v => v.GetIdentifier().ID == viewId.Value)
                    ?? throw new ViewNotFoundException(viewId.Value);

            // StraightDimensionSet
            var straightDimObjects = viewId.HasValue
                ? targetView!.GetAllObjects(typeof(StraightDimensionSet))
                : activeDrawing.GetSheet().GetAllObjects(typeof(StraightDimensionSet));

            while (straightDimObjects.MoveNext())
            {
                if (straightDimObjects.Current is not StraightDimensionSet dimSet)
                    continue;

                var currentDimensionId = dimSet.GetIdentifier().ID;
                if (dimensionId.HasValue && currentDimensionId != dimensionId.Value)
                    continue;

                var ownerView = dimSet.GetView();
                var ownerViewId = ownerView?.GetIdentifier().ID;
                var segments = EnumerateSegments(dimSet);
                if (ownerView is Tekla.Structures.Drawing.View straightDrawingView)
                {
                    CollectObjectPresentationDiagnostics(
                        presentationConnection,
                        currentDimensionId,
                        $"straightDimensionSet={currentDimensionId}",
                        TryGetViewScale(straightDrawingView),
                        presentationDiagnostics);

                    foreach (var segment in segments)
                    {
                        var segmentId = segment.GetIdentifier().ID;
                        CollectObjectPresentationDiagnostics(
                            presentationConnection,
                            segmentId,
                            $"straightSegment={segmentId}, parentSet={currentDimensionId}",
                            TryGetViewScale(straightDrawingView),
                            presentationDiagnostics);
                    }
                }

                var presentationBoxes =
                    ownerView is Tekla.Structures.Drawing.View drawingView
                        ? CollectStraightDimensionPresentationTextBoxes(
                            presentationConnection,
                            currentDimensionId,
                            segments,
                            drawingView)
                        : [];
                if (presentationBoxes.Count > 0)
                {
                    presentationTextBoxDiagnostics.AddRange(presentationBoxes.Select(textBox =>
                        CreatePresentationTextBoxDebugInfo(currentDimensionId, textBox)));

                    foreach (var textBox in presentationBoxes)
                    {
                        request.Shapes.Add(new DrawingDebugShape
                        {
                            Kind = "polygon",
                            ViewId = ownerViewId,
                            Points = textBox.Polygon,
                            Color = normalizedColor,
                            LineType = "DashDot"
                        });
                        segmentCount++;
                    }

                    dimensionIds.Add(currentDimensionId);
                    continue;
                }

                var lineContext = TryCreateDimensionLineContext(segments, dimSet.Distance);
                foreach (var segment in segments)
                {
                    var segmentSnapshot = BuildDimensionSegmentSnapshot(segment, dimSet, dimSet.Distance, lineContext);
                    var polygons = DimensionTextBoxCollector.Collect(segment, dimSet, FrameTypes.None)
                        .Select(static candidate => candidate.Polygon)
                        .Where(static polygon => polygon.Count >= 4)
                        .ToList();
                    if (polygons.Count == 0)
                    {
                        var fallback = TryCreateTextPolygon(segment, dimSet, segmentSnapshot.DimensionLine);
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

            var debugChildTypes = new List<string>();

            // AngleDimension has no child text objects; text OBB is reconstructed from
            // presentation primitives (primary) with analytical fallback.
            // GetAllObjects requires AutoFetch = true to enumerate AngleDimension objects.
            DrawingEnumeratorBase.AutoFetch = true;
            var angleDimObjects = viewId.HasValue
                ? targetView!.GetAllObjects(typeof(AngleDimension))
                : activeDrawing.GetSheet().GetAllObjects(typeof(AngleDimension));
            DrawingEnumeratorBase.AutoFetch = false;

            while (angleDimObjects.MoveNext())
            {
                if (angleDimObjects.Current is not AngleDimension dim)
                    continue;

                var currentDimensionId = dim.GetIdentifier().ID;
                if (dimensionId.HasValue && currentDimensionId != dimensionId.Value)
                    continue;

                var ownerView = dim.GetView();
                var ownerViewId = ownerView?.GetIdentifier().ID;
                if (ownerView is not Tekla.Structures.Drawing.View drawingView)
                    continue;

                var polygon = DimensionAngleTextPolygonHelper.TryCreateTextPolygon(
                    dim,
                    drawingView,
                    currentDimensionId,
                    presentationConnection,
                    presentationDiagnostics);
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

            // RadiusDimension — text OBB from presentation primitives with analytical fallback.
            DrawingEnumeratorBase.AutoFetch = true;
            var radiusDimObjects = viewId.HasValue
                ? targetView!.GetAllObjects(typeof(RadiusDimension))
                : activeDrawing.GetSheet().GetAllObjects(typeof(RadiusDimension));
            DrawingEnumeratorBase.AutoFetch = false;

            while (radiusDimObjects.MoveNext())
            {
                if (radiusDimObjects.Current is not RadiusDimension radiusDim)
                    continue;

                var currentDimensionId = radiusDim.GetIdentifier().ID;
                if (dimensionId.HasValue && currentDimensionId != dimensionId.Value)
                    continue;

                var ownerView = radiusDim.GetView();
                var ownerViewId = ownerView?.GetIdentifier().ID;
                if (ownerView is not Tekla.Structures.Drawing.View radiusDrawingView)
                    continue;

                var polygon = DimensionRadiusTextPolygonHelper.TryCreateTextPolygon(
                    radiusDim,
                    radiusDrawingView,
                    currentDimensionId,
                    presentationConnection);
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
                    SegmentCount = 0,
                    DebugChildTypes = debugChildTypes,
                    PresentationTextBoxes = presentationTextBoxDiagnostics,
                    PresentationDiagnostics = presentationDiagnostics
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
                SegmentCount = segmentCount,
                DebugChildTypes = debugChildTypes,
                PresentationTextBoxes = presentationTextBoxDiagnostics,
                PresentationDiagnostics = presentationDiagnostics
            };
        }
        finally
        {
            DrawingEnumeratorBase.AutoFetch = previousAutoFetch;
        }
    }

    private static List<DimensionPresentationTextBox> CollectStraightDimensionPresentationTextBoxes(
        PresentationConnection? presentationConnection,
        int dimensionSetId,
        IReadOnlyList<StraightDimension> segments,
        Tekla.Structures.Drawing.View view)
    {
        var boxes = new List<DimensionPresentationTextBox>();
        boxes.AddRange(DimensionPresentationTextBoxCollector.Collect(
            presentationConnection,
            dimensionSetId,
            "dimensionSet",
            view));

        foreach (var segment in segments)
        {
            boxes.AddRange(DimensionPresentationTextBoxCollector.Collect(
                presentationConnection,
                segment.GetIdentifier().ID,
                "segment",
                view));
        }

        return DimensionPresentationTextBoxCollector.DistinctByGeometry(boxes);
    }

    private static void CollectObjectPresentationDiagnostics(
        PresentationConnection? presentationConnection,
        int objectId,
        string label,
        double scale,
        List<string> diagnostics)
    {
        if (presentationConnection == null)
            return;

        try
        {
            var segment = presentationConnection.Service.GetObjectPresentation(objectId);
            if (segment == null)
            {
                diagnostics.Add($"{label}: presentation segment=null");
                return;
            }

            var primitiveCount = segment.Primitives?.Count ?? 0;
            var primitiveTypes = segment.Primitives == null
                ? string.Empty
                : string.Join(",", segment.Primitives.Select(static primitive => primitive?.GetType().Name ?? "null"));
            diagnostics.Add($"{label}: primitives.Count={primitiveCount}, types=[{primitiveTypes}]");

            if (segment.Primitives != null)
                CollectPrimitivesDiagnostics(segment.Primitives, diagnostics, depth: 1, scale);
        }
        catch (System.Exception ex)
        {
            diagnostics.Add($"{label}: exception={ex.Message}");
        }
    }

    private static DimensionPresentationTextBoxDebugInfo CreatePresentationTextBoxDebugInfo(
        int dimensionId,
        DimensionPresentationTextBox textBox)
    {
        return new DimensionPresentationTextBoxDebugInfo
        {
            DimensionId = dimensionId,
            SourceObjectId = textBox.SourceObjectId,
            SourceObjectKind = textBox.SourceObjectKind,
            Text = textBox.Text,
            PositionX = System.Math.Round(textBox.PositionX, 3),
            PositionY = System.Math.Round(textBox.PositionY, 3),
            Angle = System.Math.Round(textBox.Angle, 6),
            Height = System.Math.Round(textBox.Height, 3),
            Proportion = System.Math.Round(textBox.Proportion, 6),
            ViewScale = System.Math.Round(textBox.ViewScale, 3),
            ViewPositionX = System.Math.Round(textBox.ViewPositionX, 3),
            ViewPositionY = System.Math.Round(textBox.ViewPositionY, 3),
            ViewHeight = System.Math.Round(textBox.ViewHeight, 3),
            ViewWidthFromProportion = System.Math.Round(textBox.ViewWidthFromProportion, 3)
        };
    }

    public AngleDimensionDebugResult GetAngleDimensionDebug(int? viewId, int? dimensionId)
    {
        var result = new AngleDimensionDebugResult
        {
            ViewId = viewId
        };

        var activeDrawing = new DrawingHandler().GetActiveDrawing();
        if (activeDrawing == null)
            throw new DrawingNotOpenException();

        var targetView = viewId.HasValue ? ResolveTargetView(activeDrawing, viewId) : null;
        var previousAutoFetch = DrawingEnumeratorBase.AutoFetch;

        try
        {
            DrawingEnumeratorBase.AutoFetch = true;
            var angleDimensions = viewId.HasValue
                ? targetView!.GetAllObjects(typeof(AngleDimension))
                : activeDrawing.GetSheet().GetAllObjects(typeof(AngleDimension));
            DrawingEnumeratorBase.AutoFetch = false;

            while (angleDimensions.MoveNext())
            {
                if (angleDimensions.Current is not AngleDimension dim)
                    continue;

                var currentId = dim.GetIdentifier().ID;
                if (dimensionId.HasValue && currentId != dimensionId.Value)
                    continue;

                dim.Select();
                result.Dimensions.Add(CreateAngleDimensionDebugInfo(dim));
            }

            result.Total = result.Dimensions.Count;
            return result;
        }
        finally
        {
            DrawingEnumeratorBase.AutoFetch = previousAutoFetch;
        }
    }

    public DrawAngleDimensionDebugGeometryResult DrawAngleDimensionDebugGeometry(int? viewId, int? dimensionId, string group)
    {
        var drawingHandler = new DrawingHandler();
        var activeDrawing = drawingHandler.GetActiveDrawing();
        if (activeDrawing == null)
            throw new DrawingNotOpenException();

        var normalizedGroup = string.IsNullOrWhiteSpace(group) ? "angle-dimension-debug" : group.Trim();
        var request = new DrawingDebugOverlayRequest
        {
            Group = normalizedGroup,
            ClearGroupFirst = true
        };

        var dimensionIds = new HashSet<int>();
        var presentationDiagnostics = new List<string>();
        var targetView = viewId.HasValue ? ResolveTargetView(activeDrawing, viewId) : null;
        var selectedAngleDimensions = new List<AngleDimension>();
        using var presentationConnection = TryCreatePresentationConnection();
        presentationDiagnostics.Add(presentationConnection == null ? "presentation=null" : "presentation=connected");
        var previousAutoFetch = DrawingEnumeratorBase.AutoFetch;

        try
        {
            if (!dimensionId.HasValue)
            {
                var selected = drawingHandler.GetDrawingObjectSelector().GetSelected();
                while (selected.MoveNext())
                {
                    if (selected.Current is not AngleDimension selectedDim)
                        continue;

                    if (viewId.HasValue && selectedDim.GetView()?.GetIdentifier().ID != viewId.Value)
                        continue;

                    selectedAngleDimensions.Add(selectedDim);
                }
            }

            if (selectedAngleDimensions.Count > 0)
            {
                foreach (var dim in selectedAngleDimensions)
                    AddAngleDimensionDebugShapes(dim);
            }
            else
            {
                DrawingEnumeratorBase.AutoFetch = true;
                var angleDimensions = viewId.HasValue
                    ? targetView!.GetAllObjects(typeof(AngleDimension))
                    : activeDrawing.GetSheet().GetAllObjects(typeof(AngleDimension));
                DrawingEnumeratorBase.AutoFetch = false;

                while (angleDimensions.MoveNext())
                {
                    if (angleDimensions.Current is AngleDimension dim)
                        AddAngleDimensionDebugShapes(dim);
                }
            }

            var overlayApi = new TeklaDrawingDebugOverlayApi();
            if (request.Shapes.Count == 0)
            {
                var cleared = overlayApi.ClearOverlay(normalizedGroup);
                return new DrawAngleDimensionDebugGeometryResult
                {
                    Group = normalizedGroup,
                    ClearedCount = cleared.ClearedCount,
                    PresentationDiagnostics = presentationDiagnostics
                };
            }

            var overlayResult = overlayApi.DrawOverlay(JsonSerializer.Serialize(request));
            return new DrawAngleDimensionDebugGeometryResult
            {
                Group = overlayResult.Group,
                ClearedCount = overlayResult.ClearedCount,
                CreatedCount = overlayResult.CreatedCount,
                CreatedIds = overlayResult.CreatedIds,
                DimensionCount = dimensionIds.Count,
                ShapeCount = request.Shapes.Count,
                PresentationDiagnostics = presentationDiagnostics
            };
        }
        finally
        {
            DrawingEnumeratorBase.AutoFetch = previousAutoFetch;
        }

        void AddAngleDimensionDebugShapes(AngleDimension dim)
        {
            var currentId = dim.GetIdentifier().ID;
            if (dimensionId.HasValue && currentId != dimensionId.Value)
                return;

            dim.Select();
            if (dim.GetView() is not Tekla.Structures.Drawing.View view)
                return;

            presentationDiagnostics.Add($"dim={dim.GetIdentifier().ID}: view.Origin=({view.Origin.X:0.###},{view.Origin.Y:0.###}), view.Scale={TryGetViewScale(view)}");
            var shapes = CreateAngleDimensionPresentationShapes(dim, view);
            if (shapes.Count == 0)
                shapes = DimensionAngleDebugOverlayBuilder.CreateShapes(dim, view);
            if (shapes.Count == 0)
                return;

            request.Shapes.AddRange(shapes);
            dimensionIds.Add(currentId);
        }

        List<DrawingDebugShape> CreateAngleDimensionPresentationShapes(AngleDimension dim, Tekla.Structures.Drawing.View view)
        {
            if (presentationConnection == null)
            {
                presentationDiagnostics.Add($"dim={dim.GetIdentifier().ID}: no presentation connection");
                return new List<DrawingDebugShape>();
            }

            try
            {
                var segment = presentationConnection.Service.GetObjectPresentation(dim.GetIdentifier().ID);
                if (segment == null)
                {
                    presentationDiagnostics.Add($"dim={dim.GetIdentifier().ID}: segment=null");
                    return new List<DrawingDebugShape>();
                }

                var primitiveCount = segment.Primitives?.Count ?? 0;
                var primitiveTypes = segment.Primitives == null ? "" : string.Join(",", System.Linq.Enumerable.Select(segment.Primitives, p => p?.GetType().Name ?? "null"));
                presentationDiagnostics.Add($"dim={dim.GetIdentifier().ID}: segment.Primitives.Count={primitiveCount}, types=[{primitiveTypes}]");
                if (segment.Primitives != null)
                    CollectPrimitivesDiagnostics(segment.Primitives, presentationDiagnostics, depth: 0, scale: TryGetViewScale(view));
                var shapes = DimensionAngleDebugOverlayBuilder.CreatePresentationShapes(dim, view, segment);
                presentationDiagnostics.Add($"dim={dim.GetIdentifier().ID}: presentation shapes={shapes.Count}");
                return shapes;
            }
            catch (System.Exception ex)
            {
                presentationDiagnostics.Add($"dim={dim.GetIdentifier().ID}: exception={ex.Message}");
                return new List<DrawingDebugShape>();
            }
        }
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

    internal static string BuildDimensionSourceDebugFingerprint(IReadOnlyList<DimensionSourceDebugInfo> dimensions)
    {
        var builder = new StringBuilder();
        foreach (var dimension in dimensions.OrderBy(static item => item.DimensionId))
        {
            builder.Append(dimension.DimensionId).Append('|');
            builder.Append(dimension.MeasuredPoints.Count).Append('|');
            builder.Append(dimension.PointMappings.Count).Append('|');
            builder.Append(dimension.Candidates.Count).Append('|');
            foreach (var candidate in dimension.Candidates
                         .OrderBy(static item => item.DrawingObjectId ?? int.MaxValue)
                         .ThenBy(static item => item.ModelId ?? int.MaxValue)
                         .ThenBy(static item => item.Owner, System.StringComparer.Ordinal))
            {
                builder.Append(candidate.Owner).Append(',');
                builder.Append(candidate.DrawingObjectId).Append(',');
                builder.Append(candidate.ModelId).Append(',');
                builder.Append(candidate.GeometryPointCount).Append(';');
            }

            builder.Append('#');
        }

        return builder.ToString();
    }

    internal static string BuildDimensionTextPlacementDebugFingerprint(IReadOnlyList<DimensionTextPlacementDebugInfo> dimensions)
    {
        var builder = new StringBuilder();
        foreach (var dimension in dimensions.OrderBy(static item => item.DimensionId))
        {
            builder.Append(dimension.DimensionId).Append('|');
            builder.Append(dimension.Segments.Count).Append('|');
            foreach (var segment in dimension.Segments.OrderBy(static item => item.SegmentId))
            {
                builder.Append(segment.SegmentId).Append(',');
                builder.Append(segment.RelatedTextCandidates.Count).Append(',');
                builder.Append(segment.SelectedSource).Append(';');
            }

            builder.Append('#');
        }

        return builder.ToString();
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

    private static void CollectPrimitivesDiagnostics(
        System.Collections.Generic.IList<PrimitiveBase> primitives,
        List<string> diag,
        int depth,
        double scale = 1.0)
    {
        if (depth > 4) return;
        var indent = new string(' ', depth * 2);
        foreach (var prim in primitives)
        {
            if (prim is LinePrimitive linePrim)
            {
                diag.Add($"{indent}LinePrimitive: start=({linePrim.StartPoint.X:0.###},{linePrim.StartPoint.Y:0.###}), end=({linePrim.EndPoint.X:0.###},{linePrim.EndPoint.Y:0.###})");
            }
            else if (prim is ArcPrimitive arcPrim)
            {
                try
                {
                    var g = arcPrim.GetArc();
                    diag.Add($"{indent}ArcPrimitive: center=({g.Circle.Center.X:0.###},{g.Circle.Center.Y:0.###}), r={g.Circle.Radius:0.###}");
                }
                catch (System.Exception ex)
                {
                    diag.Add($"{indent}ArcPrimitive: GetArc() failed: {ex.Message}");
                }
            }
            else if (prim is TextPrimitive txtPrim)
            {
                diag.Add($"{indent}TextPrimitive: pos=({txtPrim.Position.X:0.###},{txtPrim.Position.Y:0.###}), angle={txtPrim.Angle:0.###}rad, height_paper={txtPrim.Height:0.###}, height_view={txtPrim.Height * scale:0.###}, proportion={txtPrim.Proportion:0.###}, font=\"{txtPrim.Font}\", text=\"{txtPrim.Text}\"");
            }
            else if (prim is CirclePrimitive circlePrim)
            {
                diag.Add($"{indent}CirclePrimitive: center=({circlePrim.CenterPoint.X:0.###},{circlePrim.CenterPoint.Y:0.###}), r={circlePrim.Radius:0.###}");
            }
            else if (prim is PointPrimitive pointPrim)
            {
                diag.Add($"{indent}PointPrimitive: pos=({pointPrim.Position.X:0.###},{pointPrim.Position.Y:0.###})");
            }
            else if (prim is PathPrimitive)
            {
                diag.Add($"{indent}PathPrimitive");
            }
            else if (prim is LoopPrimitive)
            {
                diag.Add($"{indent}LoopPrimitive");
            }
            else if (prim is PolygonPrimitive polygonPrim)
            {
                diag.Add($"{indent}PolygonPrimitive: innerLoops={polygonPrim.InnerLoops?.Count ?? 0}");
                CollectPrimitivesDiagnostics([polygonPrim.OuterLoop], diag, depth + 1, scale);
                if (polygonPrim.InnerLoops != null)
                    foreach (var innerLoop in polygonPrim.InnerLoops)
                        CollectPrimitivesDiagnostics([innerLoop], diag, depth + 1, scale);
            }
            else if (prim is BitmapPrimitive bitmapPrim)
            {
                diag.Add($"{indent}BitmapPrimitive: pos=({bitmapPrim.Position.X:0.###},{bitmapPrim.Position.Y:0.###}), w={bitmapPrim.Width:0.###}, h={bitmapPrim.Height:0.###}");
            }
            else if (prim is SymbolPrimitive symbolPrim)
            {
                diag.Add($"{indent}SymbolPrimitive: pos=({symbolPrim.Position.X:0.###},{symbolPrim.Position.Y:0.###}), w={symbolPrim.Width:0.###}, h={symbolPrim.Height:0.###}");
            }
            else if (prim is PrimitiveGroup grp)
            {
                diag.Add($"{indent}PrimitiveGroup: count={grp.Primitives?.Count ?? 0}");
                if (grp.Primitives != null)
                    CollectPrimitivesDiagnostics(grp.Primitives, diag, depth + 1, scale);
            }
            else if (prim is Segment seg)
            {
                diag.Add($"{indent}Segment: count={seg.Primitives?.Count ?? 0}");
                if (seg.Primitives != null)
                    CollectPrimitivesDiagnostics(seg.Primitives, diag, depth + 1, scale);
            }
            else
            {
                diag.Add($"{indent}{prim?.GetType().Name ?? "null"}");
            }
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
}
