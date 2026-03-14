using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using TeklaMcpServer.Api.Algorithms.Geometry;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;
using ModelPart = Tekla.Structures.Model.Part;

namespace TeklaMcpServer.Api.Drawing;

public sealed class TeklaDrawingDimensionsApi : IDrawingDimensionsApi
{
    private readonly Model _model;

    public TeklaDrawingDimensionsApi(Model model) => _model = model;

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
                if (dimObjects.Current is not StraightDimensionSet dimSet) continue;

                var info = new DrawingDimensionInfo
                {
                    Id       = dimSet.GetIdentifier().ID,
                    Type     = dimSet.GetType().Name,
                    Distance = dimSet.Distance
                };

                // Iterate individual StraightDimension segments within this set
                var segEnum = dimSet.GetObjects();
                while (segEnum.MoveNext())
                {
                    if (segEnum.Current is not StraightDimension seg) continue;

                    var start = seg.StartPoint;
                    var end   = seg.EndPoint;

                    info.Segments.Add(new DimensionSegmentInfo
                    {
                        Id     = seg.GetIdentifier().ID,
                        StartX = Math.Round(start.X, 1),
                        StartY = Math.Round(start.Y, 1),
                        EndX   = Math.Round(end.X, 1),
                        EndY   = Math.Round(end.Y, 1)
                    });
                }

                dimensions.Add(info);
            }

            return new GetDimensionsResult { Total = dimensions.Count, Dimensions = dimensions };
        }
        finally
        {
            DrawingEnumeratorBase.AutoFetch = previousAutoFetch;
        }
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

            // Find the StraightDimensionSet by ID across all sheet objects
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

        // Build point list — points array is flat [x0,y0,z0, x1,y1,z1, ...]
        if (points == null || points.Length < 6 || points.Length % 3 != 0)
            return new CreateDimensionResult { Error = "points must be a flat array [x0,y0,z0, x1,y1,z1, ...] with at least 2 points" };

        var pointList = new PointList();
        for (int i = 0; i + 2 < points.Length; i += 3)
            pointList.Add(new Point(points[i], points[i + 1], points[i + 2]));

        // Direction vector perpendicular to the dimension line
        // horizontal → line goes left-right → offset vector points up (0,1,0)
        // vertical   → line goes up-down   → offset vector points right (1,0,0)
        Vector dirVector = (direction ?? "horizontal").ToLowerInvariant() switch
        {
            "vertical" or "v"   => new Vector(1, 0, 0),
            "horizontal" or "h" => new Vector(0, 1, 0),
            _ => TryParseVector(direction) ?? new Vector(0, 1, 0)
        };

#pragma warning disable CS0618 // Tekla 2021 API still uses this constructor in current workflow.
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
            Created     = true,
            DimensionId = dim.GetIdentifier().ID,
            ViewId      = viewId,
            PointCount  = pointList.Count
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
            if (viewEnum.Current is not View view)
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
        var points = new List<Point>();
        var partsScanned = 0;

        var workPlaneHandler = _model.GetWorkPlaneHandler();
        var originalPlane = workPlaneHandler.GetCurrentTransformationPlane();
        workPlaneHandler.SetCurrentTransformationPlane(new TransformationPlane(targetView.DisplayCoordinateSystem));
        try
        {
            var identifiers = drawingHandler.GetModelObjectIdentifiers(activeDrawing);
            foreach (Identifier id in identifiers)
            {
                var modelObject = _model.SelectModelObject(id);
                if (modelObject is not ModelPart part)
                    continue;

                Solid? solid = null;
                try
                {
                    solid = part.GetSolid();
                }
                catch
                {
                    continue;
                }

                if (solid == null)
                    continue;

                var min = solid.MinimumPoint;
                var max = solid.MaximumPoint;
                if (min == null || max == null)
                    continue;

                partsScanned++;
                var z = (min.Z + max.Z) / 2.0;
                points.Add(new Point(min.X, min.Y, z));
                points.Add(new Point(min.X, max.Y, z));
                points.Add(new Point(max.X, min.Y, z));
                points.Add(new Point(max.X, max.Y, z));
            }
        }
        finally
        {
            workPlaneHandler.SetCurrentTransformationPlane(originalPlane);
        }

        readGeometrySw.Stop();
        result.ReadGeometryMs = readGeometrySw.ElapsedMilliseconds;
        result.PartsScanned = partsScanned;
        result.CandidatePoints = points.Count;

        if (points.Count < 2)
        {
            result.Error = "Not enough geometry points to place control diagonal dimension.";
            result.TotalMs = total.ElapsedMilliseconds;
            return result;
        }

        var findExtremesSw = Stopwatch.StartNew();
        var farthest = FarthestPointPair.Find(points);
        findExtremesSw.Stop();
        result.FindExtremesMs = findExtremesSw.ElapsedMilliseconds;

        var start = farthest.First;
        var end = farthest.Second;
        result.StartPoint = [start.X, start.Y, start.Z];
        result.EndPoint = [end.X, end.Y, end.Z];
        result.FarthestDistance = System.Math.Round(System.Math.Sqrt(farthest.DistanceSquared), 3);

        var createSw = Stopwatch.StartNew();
        var pointList = new PointList { start, end };

        Vector direction = BuildDiagonalOffsetDirection(start, end);

#pragma warning disable CS0618 // Tekla 2021 API constructor is required in current workflow.
        var attributes = new StraightDimensionSet.StraightDimensionSetAttributes();
#pragma warning restore CS0618
        var normalizedAttributes = string.IsNullOrWhiteSpace(attributesFile) ? "standard" : attributesFile.Trim();
        attributes.LoadAttributes(normalizedAttributes);

        var dim = new StraightDimensionSetHandler().CreateDimensionSet(
            targetView,
            pointList,
            direction,
            distance,
            attributes);
        createSw.Stop();
        result.CreateMs = createSw.ElapsedMilliseconds;

        if (dim == null)
        {
            result.Error = "CreateDimensionSet returned null.";
            result.TotalMs = total.ElapsedMilliseconds;
            return result;
        }

        var commitSw = Stopwatch.StartNew();
        activeDrawing.CommitChanges("(MCP) PlaceControlDiagonals");
        commitSw.Stop();

        result.Created = true;
        result.DimensionId = dim.GetIdentifier().ID;
        result.CommitMs = commitSw.ElapsedMilliseconds;
        result.TotalMs = total.ElapsedMilliseconds;
        return result;
    }

    private static Vector? TryParseVector(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return null;

        var value = s!.Trim();
        var parts = value.Split(',');
        if (parts.Length == 3 &&
            double.TryParse(parts[0], out var x) &&
            double.TryParse(parts[1], out var y) &&
            double.TryParse(parts[2], out var z))
            return new Vector(x, y, z);
        return null;
    }

    private static IEnumerable<View> EnumerateViews(Tekla.Structures.Drawing.Drawing drawing)
    {
        var enumerator = drawing.GetSheet().GetViews();
        while (enumerator.MoveNext())
            if (enumerator.Current is View v)
                yield return v;
    }

    private static View ResolveTargetView(Tekla.Structures.Drawing.Drawing drawing, int? viewId)
    {
        var views = EnumerateViews(drawing).ToList();
        if (views.Count == 0)
            throw new System.InvalidOperationException("No views found in active drawing.");

        if (viewId.HasValue)
            return views.FirstOrDefault(v => v.GetIdentifier().ID == viewId.Value)
                ?? throw new ViewNotFoundException(viewId.Value);

        var frontView = views.FirstOrDefault(v =>
            string.Equals(v.ViewType.ToString(), "FrontView", System.StringComparison.OrdinalIgnoreCase));
        if (frontView != null)
            return frontView;

        return views
            .OrderByDescending(v => v.Width * v.Height)
            .First();
    }

    private static Vector BuildDiagonalOffsetDirection(Point start, Point end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var len = System.Math.Sqrt((dx * dx) + (dy * dy));
        if (len < 1e-6)
            return new Vector(0, 1, 0);

        return new Vector(-dy / len, dx / len, 0);
    }
}
