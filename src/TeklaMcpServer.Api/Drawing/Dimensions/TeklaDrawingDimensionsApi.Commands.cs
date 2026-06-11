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

        var dimIds = new List<int>();

        // Contour source selection:
        // - plain ContourPlate WITHOUT booleans -> GetContourPolycurve() (polycurve), which
        //   expands chamfers/roundings into real segments. Angle is placed only at LINE-LINE
        //   joins (arc joins are skipped — those are chamfer/rounding, handled by radius dims).
        //   GetContourPolycurve() returns WORLD coords, but AngleDimension expects view-local
        //   points, so polycurve endpoints are explicitly transformed to the view CS below.
        // - otherwise (booleans present, or any non-plate part like a beam) -> solid section
        //   under the view CS work plane, which reflects boolean cuts and works for any part.
        if (_model.SelectModelObject(partIdentifier) is Tekla.Structures.Model.ContourPlate plateForPolycurve
            && !HasBooleans(plateForPolycurve))
        {
            // Get the polycurve in the ORIGINAL (world) work plane, exactly like radius dimensions.
            // It always returns world coords; unlike RadiusDimension, AngleDimension needs
            // view-local vertex/leg points, so transform segment endpoints explicitly.
            var segments = GetPolycurveSegments(plateForPolycurve);
            result.ContourPointCount = segments.Count;
            if (segments.Count < 3)
            {
                result.Error = $"Contour has too few segments ({segments.Count}); need at least 3.";
                return result;
            }

            var workPlaneHandler = _model.GetWorkPlaneHandler();
            var originalPlane = workPlaneHandler.GetCurrentTransformationPlane();
            var worldToView = new Tekla.Structures.Model.TransformationPlane(viewCs).TransformationMatrixToLocal;
            workPlaneHandler.SetCurrentTransformationPlane(new Tekla.Structures.Model.TransformationPlane(viewCs));
            try
            {
                // Vertex i is the join between segment[i-1] and segment[i]; place an angle only
                // when both adjacent segments are straight lines (skip arc joins = chamfer/rounding).
                var m = segments.Count;
                for (var i = 0; i < m; i++)
                {
                    var prevSeg = segments[(i - 1 + m) % m];
                    var curSeg = segments[i];
                    if (!prevSeg.IsLine || !curSeg.IsLine)
                        continue;

                    var vertex = FlattenZ(worldToView.Transform(curSeg.Start)); // join point of prevSeg.End == curSeg.Start
                    var first = FlattenZ(worldToView.Transform(prevSeg.Start));  // away along previous line
                    var second = FlattenZ(worldToView.Transform(curSeg.End));    // away along current line

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
        }
        else
        {
            // Solid section path: work plane in view CS so the section contour is in view coords.
            var workPlaneHandler = _model.GetWorkPlaneHandler();
            var originalPlane = workPlaneHandler.GetCurrentTransformationPlane();
            workPlaneHandler.SetCurrentTransformationPlane(new Tekla.Structures.Model.TransformationPlane(viewCs));
            try
            {
                var viewPart = (Tekla.Structures.Model.Part)_model.SelectModelObject(partIdentifier);
                var (sectionContour, _) = SolidSectionContourHelper.GetViewPlaneSectionPolygons(viewPart);
                var contourPoints = sectionContour.Select(FlattenZ).ToList();

                var n = contourPoints.Count;
                result.ContourPointCount = n;
                if (n < 3)
                {
                    result.Error = $"Section contour has too few points ({n}); need at least 3.";
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

    // One segment of a plate contour polycurve: its endpoints and whether it is a straight line.
    private readonly struct ContourSegment
    {
        public ContourSegment(Point start, Point end, bool isLine)
        {
            Start = start;
            End = end;
            IsLine = isLine;
        }

        public Point Start { get; }
        public Point End { get; }
        public bool IsLine { get; }
    }

    // Ordered segments of a ContourPlate contour via GetContourPolycurve(), which expands
    // chamfers/roundings into real segments (unlike Contour.ContourPoints). Arc segments are
    // kept (flagged IsLine=false) so callers can skip angle dimensions at arc joins.
    // GetContourPolycurve() always returns WORLD coordinates regardless of the work plane.
    // Select() is required before the call, otherwise it returns null. Keep full 3D points here;
    // callers that feed AngleDimension must transform them to view-local coordinates first.
    private List<ContourSegment> GetPolycurveSegments(Tekla.Structures.Model.ContourPlate plate)
    {
        var segments = new List<ContourSegment>();
        plate.Select();
        var polycurve = plate.GetContourPolycurve();
        if (polycurve == null)
            return segments;

        foreach (var curve in polycurve)
        {
            switch (curve)
            {
                case Tekla.Structures.Geometry3d.Arc arc:
                    segments.Add(new ContourSegment(arc.StartPoint, arc.EndPoint, false));
                    break;
                case Tekla.Structures.Geometry3d.LineSegment seg:
                    segments.Add(new ContourSegment(seg.StartPoint, seg.EndPoint, true));
                    break;
            }
        }

        return segments;
    }

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
