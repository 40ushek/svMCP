using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;

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

        var attr = new StraightDimensionSet.StraightDimensionSetAttributes();
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

    private static Vector? TryParseVector(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var parts = s.Split(',');
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
}
