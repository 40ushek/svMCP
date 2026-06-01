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

    public MoveDimensionResult MoveAngleDimension(int dimensionId, double delta)
    {
        var activeDrawing = new DrawingHandler().GetActiveDrawing();
        if (activeDrawing == null)
            throw new DrawingNotOpenException();

        var previousAutoFetch = DrawingEnumeratorBase.AutoFetch;
        DrawingEnumeratorBase.AutoFetch = true;
        try
        {
            AngleDimension? angleDimension = null;
            var allDims = activeDrawing.GetSheet().GetAllObjects(typeof(AngleDimension));
            while (allDims.MoveNext())
            {
                if (allDims.Current is AngleDimension dim && dim.GetIdentifier().ID == dimensionId)
                {
                    angleDimension = dim;
                    break;
                }
            }

            if (angleDimension == null)
                throw new System.Exception($"AngleDimension {dimensionId} not found");

            var angleType = angleDimension.Attributes.Type;
            if (angleType == AngleTypes.AngleAtVertex || angleType == AngleTypes.AngleAtVertexGradian)
            {
                return new MoveDimensionResult
                {
                    Moved = false,
                    DimensionId = dimensionId,
                    NewDistance = angleDimension.Distance,
                    Reason = "AngleAtVertex distance is saved by Tekla API but does not move the dimension visually."
                };
            }

            angleDimension.Distance += delta;
            angleDimension.Modify();
            activeDrawing.CommitChanges();
            return new MoveDimensionResult { Moved = true, DimensionId = dimensionId, NewDistance = angleDimension.Distance };
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
        var dimEnum = activeDrawing.GetSheet().GetAllObjects(typeof(DimensionBase));
        while (dimEnum.MoveNext())
        {
            if (dimEnum.Current is not DimensionBase dimension)
                continue;
            if (dimension.GetIdentifier().ID != dimensionId)
                continue;

            dimension.Delete();
            activeDrawing.CommitChanges();
            deleted = true;
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
                result.Skipped.Add(CreateCombineCandidateResult(
                    candidate,
                    previewOnly: false,
                    combined: false,
                    createdDimensionId: null,
                    reasonOverride: applyResult.Reason,
                    rollbackAttempted: applyResult.RollbackAttempted,
                    rollbackSucceeded: applyResult.RollbackSucceeded,
                    rollbackReason: applyResult.RollbackReason));
                continue;
            }

            var handoffResult = ResolveArrangeHandoffResult(activeDrawing, candidate, applyResult.CreatedDimensionId);

            result.Combined.Add(CreateCombineCandidateResult(
                candidate,
                previewOnly: false,
                combined: true,
                createdDimensionId: applyResult.CreatedDimensionId,
                deletedDimensionIdsOverride: candidate.DimensionIds,
                arrangeHandoffAttempted: handoffResult.Attempted,
                arrangeHandoffSucceeded: handoffResult.Succeeded,
                arrangeHandoffReason: handoffResult.Reason,
                arrangeHandoffAppliedDimensionIds: handoffResult.AppliedDimensionIds));
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
        string? reasonOverride = null,
        bool rollbackAttempted = false,
        bool rollbackSucceeded = false,
        string? rollbackReason = null,
        bool arrangeHandoffAttempted = false,
        bool arrangeHandoffSucceeded = false,
        string? arrangeHandoffReason = null,
        IReadOnlyList<int>? arrangeHandoffAppliedDimensionIds = null)
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
            RollbackAttempted = rollbackAttempted,
            RollbackSucceeded = rollbackSucceeded,
            RollbackReason = rollbackReason ?? string.Empty,
            ArrangeHandoffAttempted = arrangeHandoffAttempted,
            ArrangeHandoffSucceeded = arrangeHandoffSucceeded,
            ArrangeHandoffReason = arrangeHandoffReason ?? string.Empty,
            Distance = candidate.Preview?.Distance ?? 0,
            Reason = reasonOverride ?? candidate.Reason
        };

        result.DimensionIds.AddRange(candidate.DimensionIds);
        result.BlockingReasons.AddRange(candidate.BlockingReasons);
        if (deletedDimensionIdsOverride != null)
            result.DeletedDimensionIds.AddRange(deletedDimensionIdsOverride);
        if (arrangeHandoffAppliedDimensionIds != null)
            result.ArrangeHandoffAppliedDimensionIds.AddRange(arrangeHandoffAppliedDimensionIds);

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

    private DimensionArrangeHandoffResult ResolveArrangeHandoffResult(
        Tekla.Structures.Drawing.Drawing activeDrawing,
        DimensionCombineActionCandidate candidate,
        int? createdDimensionId)
    {
        if (!createdDimensionId.HasValue)
        {
            return new DimensionArrangeHandoffResult
            {
                Reason = "created_dimension_not_available"
            };
        }

        if (!candidate.ViewId.HasValue)
        {
            return new DimensionArrangeHandoffResult
            {
                Reason = "view_id_unavailable"
            };
        }

        return DimensionCombineArrangeHandoffExecutor.Execute(
            previewOnly: false,
            applyHandoff: () => TryApplyLocalArrangeHandoff(
                activeDrawing,
                candidate.ViewId.Value,
                createdDimensionId.Value));
    }

    private DimensionCombineApplyResult TryApplyCombineCandidate(
        Tekla.Structures.Drawing.Drawing activeDrawing,
        DimensionCombineActionCandidate candidate)
    {
        if (candidate.Preview == null)
        {
            return new DimensionCombineApplyResult
            {
                Success = false,
                Reason = "combine_preview_unavailable"
            };
        }

        var sourceDimensions = FindDimensionSetsById(activeDrawing, candidate.DimensionIds);
        if (sourceDimensions.Count != candidate.DimensionIds.Count)
        {
            var missing = candidate.DimensionIds.Where(id => !sourceDimensions.ContainsKey(id)).OrderBy(static id => id);
            return new DimensionCombineApplyResult
            {
                Success = false,
                Reason = $"source_dimensions_not_found:{string.Join(",", missing)}"
            };
        }

        if (!sourceDimensions.TryGetValue(candidate.BaseDimensionId, out var baseDimensionSet))
        {
            return new DimensionCombineApplyResult
            {
                Success = false,
                Reason = "base_dimension_not_found"
            };
        }

        if (baseDimensionSet.GetView() is not Tekla.Structures.Drawing.View view)
        {
            return new DimensionCombineApplyResult
            {
                Success = false,
                Reason = "base_view_not_found"
            };
        }

        if (!TryResolveCombineOffsetVector(baseDimensionSet, out var offsetVector))
        {
            return new DimensionCombineApplyResult
            {
                Success = false,
                Reason = "combine_offset_vector_unavailable"
            };
        }

        var attributes = TryGetCombineAttributes(baseDimensionSet);
        var pointList = CreateCombinePointList(candidate.Preview);
        if (pointList.Count < 2)
        {
            return new DimensionCombineApplyResult
            {
                Success = false,
                Reason = "combine_preview_has_too_few_points"
            };
        }

        StraightDimensionSet? created = null;
        var orderedSourceDimensions = candidate.DimensionIds
            .Where(sourceDimensions.ContainsKey)
            .Select(dimensionId => sourceDimensions[dimensionId])
            .ToList();

        return DimensionCombineApplyExecutor.Execute(
            createDimension: () =>
            {
                created = new StraightDimensionSetHandler().CreateDimensionSet(
                    view,
                    pointList,
                    offsetVector,
                    candidate.Preview.Distance,
                    attributes);

                return created?.GetIdentifier().ID;
            },
            deleteSourceDimensions: orderedSourceDimensions
                .Select(static dimensionSet => (System.Action)(() => dimensionSet.Delete()))
                .ToList(),
            commitCombine: () => activeDrawing.CommitChanges("(MCP) CombineDimensions"),
            rollbackDeleteCreatedDimension: () =>
            {
                if (created == null)
                    throw new System.InvalidOperationException("rollback_created_dimension_missing");

                created.Delete();
            },
            commitRollback: () => activeDrawing.CommitChanges("(MCP) RollbackCombineDimensions"));
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
            var snapshot = BuildDimensionSnapshot(baseDimensionSet);
            if (TryNormalizeDirection(snapshot.DirectionX, snapshot.DirectionY, out var direction) &&
                snapshot.TopDirection != 0)
            {
                var upX = -direction.Y * snapshot.TopDirection;
                var upY = direction.X * snapshot.TopDirection;
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

    public PlaceContourAngleDimensionsResult PlaceContourAngleDimensions(int? viewId, double distance, string attributesFile, bool skipRightAngles)
    {
        var result = new PlaceContourAngleDimensionsResult();

        var activeDrawing = new DrawingHandler().GetActiveDrawing();
        if (activeDrawing == null)
            throw new DrawingNotOpenException();

        var targetView = ResolveTargetView(activeDrawing, viewId);
        result.ViewId = targetView.GetIdentifier().ID;
        result.ViewType = targetView.ViewType.ToString();

        // Make the command idempotent: a re-run must not duplicate angle dimensions.
        // Delete any existing AngleDimension objects in the target view before placing new ones.
        var existingAngleDims = targetView.GetAllObjects(typeof(AngleDimension));
        var toDelete = new List<AngleDimension>();
        while (existingAngleDims.MoveNext())
        {
            if (existingAngleDims.Current is AngleDimension existing)
                toDelete.Add(existing);
        }
        foreach (var dim in toDelete)
            dim.Delete();

        // Find the first model Part in the view (ContourPlate, Beam, etc.).
        // The contour is taken from the solid section (below), so any part type works.
        Tekla.Structures.Identifier? partIdentifier = null;
        Tekla.Structures.Model.Part? worldPart = null;
        var partObjects = targetView.GetAllObjects(typeof(Tekla.Structures.Drawing.Part));
        while (partObjects.MoveNext())
        {
            if (partObjects.Current is not Tekla.Structures.Drawing.Part drawingPart)
                continue;

            if (_model.SelectModelObject(drawingPart.ModelIdentifier) is Tekla.Structures.Model.Part modelPart)
            {
                partIdentifier = drawingPart.ModelIdentifier;
                worldPart = modelPart;
                result.ModelId = drawingPart.ModelIdentifier.ID;
                break;
            }
        }

        if (partIdentifier == null || worldPart == null)
        {
            result.Error = "No model Part found in the target view.";
            return result;
        }

        // Flip detection (world-space normals) applies to ContourPlate, whose own plane
        // matches the contour. For other parts (beams) the contour comes from the view-plane
        // section and is already oriented in the view plane, so flip is not applied.
        // Must be read before the work plane is switched.
        var viewCs = targetView.ViewCoordinateSystem;
        var flipped = false;
        if (worldPart is Tekla.Structures.Model.ContourPlate worldPlate)
        {
            var plateCs = worldPlate.GetCoordinateSystem();
            var plateNormal = plateCs.AxisX.Cross(plateCs.AxisY);
            var viewNormal = viewCs.AxisX.Cross(viewCs.AxisY);
            flipped = DimensionAnglePlacementHelper.IsContourFlipped(plateNormal, viewNormal);
        }
        result.Flipped = flipped;

        var attributes = new AngleDimensionAttributes();
        try
        {
            attributes.LoadAttributes(DimensionAnglePlacementHelper.NormalizeAttributesFile(attributesFile));
        }
        catch
        {
        }

        attributes.Type = AngleTypes.AngleAtVertex;

        // AngleDimension expects points in the view plane. Set the work plane to the view CS
        // so the solid (and the section contour) come back in view-local coordinates.
        var workPlaneHandler = _model.GetWorkPlaneHandler();
        var originalPlane = workPlaneHandler.GetCurrentTransformationPlane();
        workPlaneHandler.SetCurrentTransformationPlane(new Tekla.Structures.Model.TransformationPlane(viewCs));
        var dimIds = new List<int>();
        try
        {
            // Re-select the part under the active work plane (view CS).
            var viewPart = (Tekla.Structures.Model.Part)_model.SelectModelObject(partIdentifier);

            // Contour source selection:
            // - plain ContourPlate WITHOUT booleans -> fast path via Contour.ContourPoints (polycurve);
            // - otherwise (booleans present, or any non-plate part like a beam) -> solid section,
            //   which reflects boolean cuts and works for any part type.
            List<Point> contourPoints;
            if (viewPart is Tekla.Structures.Model.ContourPlate viewPlate && !HasBooleans(viewPlate))
            {
                contourPoints = new List<Point>();
                foreach (Point cp in viewPlate.Contour.ContourPoints)
                    contourPoints.Add(FlattenZ(cp));
            }
            else
            {
                var (sectionContour, _) = SolidSectionContourHelper.GetViewPlaneSectionPolygons(viewPart);
                contourPoints = sectionContour.Select(FlattenZ).ToList();
            }

            var n = contourPoints.Count;
            result.ContourPointCount = n;
            if (n < 3)
            {
                result.Error = $"Contour has too few points ({n}); need at least 3.";
                return result;
            }

            for (var i = 0; i < n; i++)
            {
                var (firstIndex, secondIndex) = DimensionAnglePlacementHelper.ResolveNeighbors(i, n, flipped);
                var vertex = contourPoints[i];
                var first = contourPoints[firstIndex];
                var second = contourPoints[secondIndex];

                if (skipRightAngles &&
                    DimensionAnglePlacementHelper.IsRightAngle(
                        DimensionAnglePlacementHelper.LegAngleDegrees(vertex, first, second)))
                {
                    result.SkippedRightAngleCount++;
                    continue;
                }

                var angleDim = new AngleDimension(targetView, vertex, first, second, distance, attributes);
                if (angleDim.Insert())
                    dimIds.Add(angleDim.GetIdentifier().ID);
            }
        }
        finally
        {
            workPlaneHandler.SetCurrentTransformationPlane(originalPlane);
        }

        if (dimIds.Count == 0)
        {
            result.Error = result.SkippedRightAngleCount > 0
                ? $"All {result.SkippedRightAngleCount} contour vertices were right angles and skipped (skipRightAngles=true)."
                : "AngleDimension.Insert returned false for all contour vertices.";
            return result;
        }

        activeDrawing.CommitChanges("(MCP) PlaceContourAngleDimensions");

        result.Created = true;
        result.CreatedCount = dimIds.Count;
        result.DimensionIds = dimIds.ToArray();
        return result;
    }

    public PlaceContourRadiusDimensionsResult PlaceContourRadiusDimensions(int? viewId, double distance, string attributesFile)
    {
        var result = new PlaceContourRadiusDimensionsResult();

        var activeDrawing = new DrawingHandler().GetActiveDrawing();
        if (activeDrawing == null)
            throw new DrawingNotOpenException();

        var targetView = ResolveTargetView(activeDrawing, viewId);
        result.ViewId = targetView.GetIdentifier().ID;
        result.ViewType = targetView.ViewType.ToString();

        Tekla.Structures.Identifier? plateIdentifier = null;
        var partObjects = targetView.GetAllObjects(typeof(Tekla.Structures.Drawing.Part));
        while (partObjects.MoveNext())
        {
            if (partObjects.Current is not Tekla.Structures.Drawing.Part drawingPart)
                continue;
            if (_model.SelectModelObject(drawingPart.ModelIdentifier) is Tekla.Structures.Model.ContourPlate)
            {
                plateIdentifier = drawingPart.ModelIdentifier;
                result.ModelId = drawingPart.ModelIdentifier.ID;
                break;
            }
        }

        if (plateIdentifier == null)
        {
            result.Error = "No ContourPlate found in the target view.";
            return result;
        }

        var viewCs = targetView.ViewCoordinateSystem;
        var workPlaneHandler = _model.GetWorkPlaneHandler();
        var originalPlane = workPlaneHandler.GetCurrentTransformationPlane();

        var attributes = new RadiusDimensionAttributes();
        try { attributes.LoadAttributes(DimensionAnglePlacementHelper.NormalizeAttributesFile(attributesFile)); }
        catch { }

        // GetContourPolycurve() always returns world-space coordinates regardless of the current
        // TransformationPlane. RadiusDimension accepts world contour points when the targetView
        // is passed - verified at runtime for both CHAMFER_ROUNDING and CHAMFER_ARC_POINT plates.
        // Select() is required before the call; without it the method returns null.
        var worldPlateForPolycurve = (Tekla.Structures.Model.ContourPlate)_model.SelectModelObject(plateIdentifier);
        worldPlateForPolycurve.Select();
        var polycurve = worldPlateForPolycurve.GetContourPolycurve();

        var dimIds = new List<int>();
        try
        {
            workPlaneHandler.SetCurrentTransformationPlane(new Tekla.Structures.Model.TransformationPlane(viewCs));

            if (polycurve != null)
            {
                foreach (var curve in polycurve)
                {
                    if (curve is not Tekla.Structures.Geometry3d.Arc arc)
                        continue;

                    result.ArcCount++;
                    TryInsertRadiusDimension(
                        targetView,
                        FlattenZ(arc.StartPoint),
                        FlattenZ(arc.ArcMiddlePoint),
                        FlattenZ(arc.EndPoint),
                        distance,
                        attributes,
                        dimIds);
                }

                if (result.ArcCount > 0 && dimIds.Count == 0)
                {
                    result.Error = $"Found {result.ArcCount} arc(s) via polycurve but RadiusDimension.Insert failed for all.";
                    return result;
                }
            }

            // Analytical fallback for plates where GetContourPolycurve returned null or no arcs.
            // Handles CHAMFER_ROUNDING by computing arc geometry from the chamfer radius and neighbors.
            if (result.ArcCount == 0)
            {
                var viewPlateMain = (Tekla.Structures.Model.ContourPlate)_model.SelectModelObject(plateIdentifier);
                var contourPoints = viewPlateMain.Contour.ContourPoints;

                var n = contourPoints.Count;
                if (n < 3)
                {
                    result.Error = $"Contour has too few points ({n}).";
                    return result;
                }

                for (var i = 0; i < n; i++)
                {
                    var cp = (Tekla.Structures.Model.ContourPoint)contourPoints[i];
                    var chamfer = cp.Chamfer;
                    if (chamfer.Type != Tekla.Structures.Model.Chamfer.ChamferTypeEnum.CHAMFER_ROUNDING)
                        continue;

                    var radius = chamfer.X;
                    if (radius <= 0)
                        continue;

                    result.ArcCount++;

                    var vertex = FlattenZ((Point)cp);
                    var prevPt = FlattenZ((Point)contourPoints[((i - 1) % n + n) % n]);
                    var nextPt = FlattenZ((Point)contourPoints[(i + 1) % n]);

                    var up = new Vector(prevPt.X - vertex.X, prevPt.Y - vertex.Y, 0);
                    var un = new Vector(nextPt.X - vertex.X, nextPt.Y - vertex.Y, 0);
                    up.Normalize();
                    un.Normalize();

                    var cosHalf = System.Math.Sqrt((1.0 + up.Dot(un)) / 2.0);
                    var sinHalf = System.Math.Sqrt((1.0 - up.Dot(un)) / 2.0);
                    if (sinHalf < 1e-9) continue;

                    var t = radius * cosHalf / sinHalf;
                    var centerDist = radius / sinHalf;

                    var bx = up.X + un.X;
                    var by = up.Y + un.Y;
                    var blen = System.Math.Sqrt(bx * bx + by * by);
                    if (blen < 1e-9) continue;
                    bx /= blen; by /= blen;

                    var p1 = new Point(vertex.X + t * up.X, vertex.Y + t * up.Y, 0);
                    var p3 = new Point(vertex.X + t * un.X, vertex.Y + t * un.Y, 0);
                    var p2 = new Point(vertex.X + (centerDist - radius) * bx, vertex.Y + (centerDist - radius) * by, 0);

                    TryInsertRadiusDimension(targetView, p1, p2, p3, distance, attributes, dimIds);
                }
            }
        }
        finally
        {
            workPlaneHandler.SetCurrentTransformationPlane(originalPlane);
        }

        if (result.ArcCount == 0)
        {
            result.Error = "No arc segments found: polycurve returned no arcs and no CHAMFER_ROUNDING vertices in contour.";
            return result;
        }

        if (dimIds.Count == 0)
        {
            result.Error = $"Found {result.ArcCount} arc(s) via analytical fallback but RadiusDimension.Insert failed for all.";
            return result;
        }

        activeDrawing.CommitChanges("(MCP) PlaceContourRadiusDimensions");
        result.Created = true;
        result.CreatedCount = dimIds.Count;
        result.DimensionIds = dimIds.ToArray();
        return result;
    }

    private static AngleDimensionDebugInfo CreateAngleDimensionDebugInfo(AngleDimension dim)
    {
        var ownerView = dim.GetView();
        var view = ownerView as Tekla.Structures.Drawing.View;
        var viewScale = view?.Attributes?.Scale > 0 ? view.Attributes.Scale : 1.0;
        var origin = dim.Origin;
        var point1 = dim.Point1;
        var point2 = dim.Point2;

        TryNormalizeDirection(point1.X - origin.X, point1.Y - origin.Y, out var firstUnit);
        TryNormalizeDirection(point2.X - origin.X, point2.Y - origin.Y, out var secondUnit);
        var bisectorUnit = ResolveAngleBisector(firstUnit, secondUnit);
        var firstLength = System.Math.Sqrt(System.Math.Pow(point1.X - origin.X, 2) + System.Math.Pow(point1.Y - origin.Y, 2));
        var secondLength = System.Math.Sqrt(System.Math.Pow(point2.X - origin.X, 2) + System.Math.Pow(point2.Y - origin.Y, 2));

        var info = new AngleDimensionDebugInfo
        {
            DimensionId = dim.GetIdentifier().ID,
            ViewId = ownerView?.GetIdentifier().ID,
            ViewType = view?.ViewType.ToString() ?? ownerView?.GetType().Name ?? string.Empty,
            ViewScale = RoundDebug(viewScale),
            AngleType = SafeToString(() => dim.Attributes.Type),
            TextPlacing = SafeToString(() => dim.Attributes.Text.TextPlacing),
            DimensionPlacing = SafeToString(() => dim.Attributes.Placing.Placing),
            AngleDegrees = RoundDebug(SafeDouble(dim.GetAngle)),
            Distance = RoundDebug(dim.Distance),
            DistanceTimesScale = RoundDebug(dim.Distance * viewScale),
            DistanceDivScale = RoundDebug(viewScale > 1e-9 ? dim.Distance / viewScale : dim.Distance),
            Origin = CreateDebugPoint(origin),
            Point1 = CreateDebugPoint(point1),
            Point2 = CreateDebugPoint(point2),
            FirstUnit = CreateDebugVector(firstUnit),
            SecondUnit = CreateDebugVector(secondUnit),
            BisectorUnit = CreateDebugVector(bisectorUnit)
        };

        AddAngleRadiusCandidate(info, "distance", dim.Distance, origin, firstUnit, secondUnit, bisectorUnit);
        AddAngleRadiusCandidate(info, "distance_times_scale", dim.Distance * viewScale, origin, firstUnit, secondUnit, bisectorUnit);
        if (viewScale > 1e-9)
            AddAngleRadiusCandidate(info, "distance_div_scale", dim.Distance / viewScale, origin, firstUnit, secondUnit, bisectorUnit);
        var averagePointRadius = (firstLength + secondLength) / 2.0;
        AddAngleRadiusCandidate(info, "origin_point1", firstLength, origin, firstUnit, secondUnit, bisectorUnit);
        AddAngleRadiusCandidate(info, "origin_point2", secondLength, origin, firstUnit, secondUnit, bisectorUnit);
        AddAngleRadiusCandidate(info, "point_average", averagePointRadius, origin, firstUnit, secondUnit, bisectorUnit);
        AddAngleRadiusCandidate(info, "point_average_times_scale", averagePointRadius * viewScale, origin, firstUnit, secondUnit, bisectorUnit);
        if (viewScale > 1e-9)
            AddAngleRadiusCandidate(info, "point_average_div_scale", averagePointRadius / viewScale, origin, firstUnit, secondUnit, bisectorUnit);

        return info;
    }

    private static (double X, double Y) ResolveAngleBisector((double X, double Y) firstUnit, (double X, double Y) secondUnit)
    {
        if (TryNormalizeDirection(firstUnit.X + secondUnit.X, firstUnit.Y + secondUnit.Y, out var bisector))
            return bisector;

        var perpendicular = (-firstUnit.Y, firstUnit.X);
        var dot = (perpendicular.Item1 * secondUnit.X) + (perpendicular.Item2 * secondUnit.Y);
        return dot >= 0.0
            ? perpendicular
            : (-perpendicular.Item1, -perpendicular.Item2);
    }

    private static void AddAngleRadiusCandidate(
        AngleDimensionDebugInfo info,
        string name,
        double radius,
        Point origin,
        (double X, double Y) firstUnit,
        (double X, double Y) secondUnit,
        (double X, double Y) bisectorUnit)
    {
        info.RadiusCandidates.Add(new AngleDimensionRadiusCandidateInfo
        {
            Name = name,
            Radius = RoundDebug(radius),
            FirstRayPoint = CreateDebugPoint(origin.X + (firstUnit.X * radius), origin.Y + (firstUnit.Y * radius)),
            SecondRayPoint = CreateDebugPoint(origin.X + (secondUnit.X * radius), origin.Y + (secondUnit.Y * radius)),
            BisectorPoint = CreateDebugPoint(origin.X + (bisectorUnit.X * radius), origin.Y + (bisectorUnit.Y * radius))
        });
    }

    private static DrawingPointInfo CreateDebugPoint(Point point) =>
        CreateDebugPoint(point.X, point.Y);

    private static DrawingPointInfo CreateDebugPoint(double x, double y) =>
        new()
        {
            X = RoundDebug(x),
            Y = RoundDebug(y)
        };

    private static DrawingVectorInfo CreateDebugVector((double X, double Y) vector) =>
        new()
        {
            X = RoundDebug(vector.X),
            Y = RoundDebug(vector.Y)
        };

    private static double SafeDouble(System.Func<double> valueFactory)
    {
        try
        {
            return valueFactory();
        }
        catch
        {
            return 0.0;
        }
    }

    private static string SafeToString<T>(System.Func<T> valueFactory)
    {
        try
        {
            return valueFactory()?.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static double RoundDebug(double value) => System.Math.Round(value, 6);

    private static Point FlattenZ(Point p) => new(p.X, p.Y, 0.0);

    // True if the part has any boolean operations (cut/add) attached. Used to decide whether the
    // original contour (Contour.ContourPoints) is trustworthy or the solid section is required.
    private static bool HasBooleans(Tekla.Structures.Model.Part part)
    {
        try
        {
            var booleans = part.GetBooleans();
            return booleans != null && booleans.MoveNext();
        }
        catch
        {
            // If booleans cannot be queried, fall back to the solid path (safer for geometry).
            return true;
        }
    }

    private static void TryInsertRadiusDimension(
        ViewBase view, Point p1, Point p2, Point p3,
        double distance, RadiusDimensionAttributes attributes, List<int> dimIds)
    {
        var dim = new RadiusDimension(view, p1, p2, p3, distance, attributes);
        if (dim.Insert())
            dimIds.Add(dim.GetIdentifier().ID);
    }
}
