using System.Collections.Generic;
using System.Linq;
using TeklaMcpServer.Api.Algorithms.Geometry;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;

namespace TeklaMcpServer.Api.Drawing;

public sealed partial class TeklaDrawingDimensionsApi : IDrawingDimensionsApi
{
    private readonly Model _model;

    public TeklaDrawingDimensionsApi(Model model) => _model = model;

    internal static DrawingBoundsInfo CreateBoundsInfo(double minX, double minY, double maxX, double maxY) => new()
    {
        MinX = System.Math.Round(minX, 3),
        MinY = System.Math.Round(minY, 3),
        MaxX = System.Math.Round(maxX, 3),
        MaxY = System.Math.Round(maxY, 3)
    };

    internal static DrawingBoundsInfo? CombineBounds(IEnumerable<DrawingBoundsInfo?> bounds)
    {
        var present = bounds.Where(static b => b != null).Cast<DrawingBoundsInfo>().ToList();
        if (present.Count == 0)
            return null;

        return CreateBoundsInfo(
            present.Min(static b => b.MinX),
            present.Min(static b => b.MinY),
            present.Max(static b => b.MaxX),
            present.Max(static b => b.MaxY));
    }

    internal static string DetermineDimensionOrientation(IReadOnlyList<DimensionSegmentInfo> segments)
    {
        var hasHorizontal = false;
        var hasVertical = false;
        var hasAngled = false;

        foreach (var segment in segments)
        {
            var dx = System.Math.Abs(segment.EndX - segment.StartX);
            var dy = System.Math.Abs(segment.EndY - segment.StartY);
            if (dx <= 1e-6 && dy <= 1e-6)
                continue;

            if (dy <= dx * 0.01)
            {
                hasHorizontal = true;
                continue;
            }

            if (dx <= dy * 0.01)
            {
                hasVertical = true;
                continue;
            }

            hasAngled = true;
        }

        if (hasAngled || (hasHorizontal && hasVertical))
            return "angled";

        if (hasHorizontal)
            return "horizontal";

        if (hasVertical)
            return "vertical";

        return string.Empty;
    }

    private static DrawingBoundsInfo? TryGetBounds(DrawingObject drawingObject)
    {
        if (drawingObject is not IAxisAlignedBoundingBox bounded)
            return null;

        var box = bounded.GetAxisAlignedBoundingBox();
        if (box == null)
            return null;

        return CreateBoundsInfo(box.MinPoint.X, box.MinPoint.Y, box.MaxPoint.X, box.MaxPoint.Y);
    }

    private static DrawingBoundsInfo CreateBoundsFromSegmentPoints(Point start, Point end) => CreateBoundsInfo(
        System.Math.Min(start.X, end.X),
        System.Math.Min(start.Y, end.Y),
        System.Math.Max(start.X, end.X),
        System.Math.Max(start.Y, end.Y));

    private static DrawingBoundsInfo? TryGetTextBounds(StraightDimension segment)
    {
        _ = segment;
        // Phase 1 keeps text geometry explicit but conservative:
        // do not fabricate text boxes until Tekla exposes them reliably.
        return null;
    }

    private static (int? ViewId, string ViewType) GetOwnerViewInfo(DrawingObject drawingObject)
    {
        var ownerView = drawingObject.GetView();
        if (ownerView == null)
            return (null, string.Empty);

        return ownerView is View view
            ? (view.GetIdentifier().ID, view.ViewType.ToString())
            : (ownerView.GetIdentifier().ID, ownerView.GetType().Name);
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
        {
            return new Vector(x, y, z);
        }

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
        var o1 = GeometricOrientation(a1, a2, b1);
        var o2 = GeometricOrientation(a1, a2, b2);
        var o3 = GeometricOrientation(b1, b2, a1);
        var o4 = GeometricOrientation(b1, b2, a2);
        return o1 * o2 < 0 && o3 * o4 < 0;
    }

    private static double GeometricOrientation(Point a, Point b, Point c)
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
        return System.Math.Abs(left.X - right.X) <= 1.0
            && System.Math.Abs(left.Y - right.Y) <= 1.0;
    }
}
