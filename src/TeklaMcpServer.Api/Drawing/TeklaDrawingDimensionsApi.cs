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
#pragma warning disable CS0618 // Tekla 2021 API constructor is required in current workflow.
            var attributes = new StraightDimensionSet.StraightDimensionSetAttributes();
#pragma warning restore CS0618
            var normalizedAttributes = string.IsNullOrWhiteSpace(attributesFile) ? "standard" : attributesFile.Trim();
            attributes.LoadAttributes(normalizedAttributes);

            // When both diagonals intersect their texts land at almost the same
            // point (near the assembly centre). In that case use a larger offset
            // for the second diagonal so the lines sit at different distances
            // and the texts are clearly separated.
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

    private static List<Point> CollectDimensionSegmentPoints(View targetView, out int dimensionsScanned)
    {
        var points = new List<Point>();
        dimensionsScanned = 0;

        var segmentObjects = targetView.GetAllObjects(typeof(StraightDimension));
        while (segmentObjects.MoveNext())
        {
            if (segmentObjects.Current is not StraightDimension segment)
                continue;

            dimensionsScanned++;
            points.Add(new Point(segment.StartPoint.X, segment.StartPoint.Y, segment.StartPoint.Z));
            points.Add(new Point(segment.EndPoint.X, segment.EndPoint.Y, segment.EndPoint.Z));
        }

        if (points.Count >= 2)
            return points;

        points.Clear();
        dimensionsScanned = 0;

        var dimObjects = targetView.GetAllObjects(typeof(StraightDimensionSet));
        while (dimObjects.MoveNext())
        {
            if (dimObjects.Current is not StraightDimensionSet dimSet)
                continue;

            dimensionsScanned++;
            var segEnum = dimSet.GetObjects();
            while (segEnum.MoveNext())
            {
                if (segEnum.Current is not StraightDimension segment)
                    continue;

                points.Add(new Point(segment.StartPoint.X, segment.StartPoint.Y, segment.StartPoint.Z));
                points.Add(new Point(segment.EndPoint.X, segment.EndPoint.Y, segment.EndPoint.Z));
            }
        }

        return points;
    }

    private static bool TryFindSecondaryDiagonal(
        IReadOnlyList<Point> hull,
        Point firstA,
        Point firstB,
        out (Point Start, Point End) pair)
    {
        if (TryFindSecondaryDiagonalInternal(hull, firstA, firstB, requireDistinctEndpoints: true, out pair))
            return true;

        return TryFindSecondaryDiagonalInternal(hull, firstA, firstB, requireDistinctEndpoints: false, out pair);
    }

    private static bool TryFindSecondaryDiagonalInternal(
        IReadOnlyList<Point> hull,
        Point firstA,
        Point firstB,
        bool requireDistinctEndpoints,
        out (Point Start, Point End) pair)
    {
        pair = default;
        var firstLen = System.Math.Sqrt(ConvexHull.DistanceSquared(firstA, firstB));
        if (firstLen < 1e-9)
            return false;

        var hasCandidate = false;
        var bestScore = double.MinValue;
        for (var i = 0; i < hull.Count; i++)
        {
            for (var j = i + 1; j < hull.Count; j++)
            {
                var a = hull[i];
                var b = hull[j];

                if (IsSamePair(a, b, firstA, firstB))
                    continue;

                if (requireDistinctEndpoints
                    && (SamePointXY(a, firstA) || SamePointXY(a, firstB) || SamePointXY(b, firstA) || SamePointXY(b, firstB)))
                {
                    continue;
                }

                var lenSquared = ConvexHull.DistanceSquared(a, b);
                if (lenSquared < 1e-9)
                    continue;

                var len = System.Math.Sqrt(lenSquared);
                var dotAbs = System.Math.Abs(((firstB.X - firstA.X) * (b.X - a.X) + (firstB.Y - firstA.Y) * (b.Y - a.Y)) / (firstLen * len));
                if (dotAbs > 0.98)
                    continue;

                var crossingBonus = SegmentsProperlyIntersect(firstA, firstB, a, b) ? 1_000_000_000d : 0d;
                var score = crossingBonus + lenSquared;
                if (!hasCandidate || score > bestScore)
                {
                    hasCandidate = true;
                    bestScore = score;
                    pair = (a, b);
                }
            }
        }

        return hasCandidate;
    }

    private static bool IsRectangleLikeHull(IReadOnlyList<Point> hull)
    {
        if (hull.Count != 4)
            return false;

        for (var i = 0; i < 4; i++)
        {
            var prev = hull[(i + 3) % 4];
            var curr = hull[i];
            var next = hull[(i + 1) % 4];

            var vx1 = curr.X - prev.X;
            var vy1 = curr.Y - prev.Y;
            var vx2 = next.X - curr.X;
            var vy2 = next.Y - curr.Y;

            var len1 = System.Math.Sqrt(vx1 * vx1 + vy1 * vy1);
            var len2 = System.Math.Sqrt(vx2 * vx2 + vy2 * vy2);
            if (len1 < 1e-9 || len2 < 1e-9)
                return false;

            var cos = System.Math.Abs((vx1 * vx2 + vy1 * vy2) / (len1 * len2));
            if (cos > 0.2)
                return false;
        }

        return true;
    }

    private static bool SegmentsProperlyIntersect(Point a1, Point a2, Point b1, Point b2)
    {
        var o1 = Orientation(a1, a2, b1);
        var o2 = Orientation(a1, a2, b2);
        var o3 = Orientation(b1, b2, a1);
        var o4 = Orientation(b1, b2, a2);
        return o1 * o2 < 0 && o3 * o4 < 0;
    }

    private static double Orientation(Point a, Point b, Point c)
    {
        return (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
    }

    private static bool IsSamePair(Point a1, Point a2, Point b1, Point b2)
    {
        return (SamePointXY(a1, b1) && SamePointXY(a2, b2))
            || (SamePointXY(a1, b2) && SamePointXY(a2, b1));
    }

    private static bool SamePointXY(Point left, Point right)
    {
        return left.X.Equals(right.X) && left.Y.Equals(right.Y);
    }
}
