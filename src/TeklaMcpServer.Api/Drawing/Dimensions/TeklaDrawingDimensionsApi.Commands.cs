using System.Collections.Generic;
using System.Linq;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Geometry3d;

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

        // Callers address dimensions by the id reported for a set (get_drawing_dimensions /
        // get_dimension_contexts). Enumerating DimensionBase yields the individual
        // StraightDimension segments instead, whose ids are set id + 1, + 2, ... and therefore
        // never match — so try the set type first and only then fall back to the base type
        // for dimension kinds that are not straight sets.
        var deleted = TryDeleteById(activeDrawing, typeof(StraightDimensionSet), dimensionId)
                   || TryDeleteById(activeDrawing, typeof(DimensionBase), dimensionId);

        return new DeleteDimensionResult
        {
            HasActiveDrawing = true,
            Deleted = deleted,
            DimensionId = dimensionId
        };
    }

    /// <summary>
    /// Merges extra points into an existing straight dimension set.
    ///
    /// Nothing is destroyed: the target set keeps its id, its style attributes and its offset,
    /// so the sheet is not rebuilt. This is the cheap, safe way to extend a chain.
    ///
    /// Mechanics: Tekla has no "add point" call, so a throwaway set is built from the supplied
    /// points and handed to <c>DimensionSetBase.AddToDimensionSet</c>.
    ///
    /// KNOWN NOT TO WORK on TS2025: that call returns true but does not merge the points — the
    /// target keeps its original count and the throwaway set survives as a separate dimension on
    /// the sheet. The method detects this, removes the throwaway set and reports an error rather
    /// than a false success, so the drawing is left unchanged. Kept in the tree while the correct
    /// use of AddToDimensionSet is being researched; until then adding a point requires
    /// <see cref="RecreateDimension"/>.
    ///
    /// Because a dimension set cannot exist with fewer than two points, at least two points
    /// must be supplied. To add a single new point, pass it together with a point the target
    /// already has.
    /// </summary>
    /// <param name="direction">
    /// Offset direction for the throwaway set: 'horizontal', 'vertical' or a 'dx,dy,dz' vector.
    /// Must match the target chain, otherwise the two sets are built along different axes.
    /// </param>
    public AddDimensionPointsResult AddDimensionPoints(int dimensionId, double[] points, string direction)
    {
        // Argument check first: a malformed point array is the caller's mistake and needs no
        // round trip into Tekla to diagnose.
        if (points == null || points.Length < 6 || points.Length % 3 != 0)
            return new AddDimensionPointsResult
            {
                DimensionId = dimensionId,
                Error = "points must be a flat array [x0,y0,z0, x1,y1,z1, ...] with at least 2 points"
            };

        var activeDrawing = new DrawingHandler().GetActiveDrawing();
        if (activeDrawing == null)
            throw new DrawingNotOpenException();

        var previousAutoFetch = DrawingEnumeratorBase.AutoFetch;
        DrawingEnumeratorBase.AutoFetch = false;
        try
        {
            var target = FindDimensionSet(activeDrawing, dimensionId);
            if (target == null)
                return new AddDimensionPointsResult
                {
                    DimensionId = dimensionId,
                    Error = $"DimensionSet {dimensionId} not found"
                };

            var view = target.GetView() as ViewBase;
            if (view == null)
                return new AddDimensionPointsResult
                {
                    DimensionId = dimensionId,
                    Error = $"DimensionSet {dimensionId} has no owning view"
                };

            var pointList = ToPointList(points);
            var addition = new StraightDimensionSetHandler().CreateDimensionSet(
                view, pointList, DimensionCreatePlacementHelper.ResolveDirection(direction),
                target.Distance, target.Attributes);

            if (addition == null)
                return new AddDimensionPointsResult
                {
                    DimensionId = dimensionId,
                    Error = "CreateDimensionSet returned null while building the points to merge"
                };

            bool merged;
            try
            {
                merged = target.AddToDimensionSet(addition);
            }
            catch
            {
                DiscardAddition(activeDrawing, addition);
                throw;
            }

            if (!merged)
            {
                // The throwaway set is a real object on the sheet. If the merge did not consume it,
                // leaving it behind adds a stray dimension the caller never asked for.
                DiscardAddition(activeDrawing, addition);
                return new AddDimensionPointsResult
                {
                    DimensionId = dimensionId,
                    AddedPointCount = pointList.Count,
                    Error = "AddToDimensionSet returned false; the temporary set was removed"
                };
            }

            activeDrawing.CommitChanges("(MCP) AddDimensionPoints");

            var after = FindDimensionSet(activeDrawing, dimensionId);
            var pointCountAfter = after == null ? 0 : CountPoints(after);

            // Measured on TS2025: AddToDimensionSet reports success but does NOT merge the points —
            // the target keeps its count and the throwaway set survives as a separate dimension.
            // Detect that instead of reporting a success that did not happen.
            var strayAddition = FindDimensionSet(activeDrawing, addition.GetIdentifier().ID);
            if (strayAddition != null)
            {
                DiscardAddition(activeDrawing, strayAddition);
                return new AddDimensionPointsResult
                {
                    DimensionId = dimensionId,
                    AddedPointCount = pointList.Count,
                    PointCountAfter = pointCountAfter,
                    Error = "AddToDimensionSet reported success but did not merge the points; " +
                            "the temporary set was removed and the target is unchanged"
                };
            }

            return new AddDimensionPointsResult
            {
                Added = true,
                DimensionId = dimensionId,
                AddedPointCount = pointList.Count,
                PointCountAfter = pointCountAfter
            };
        }
        finally
        {
            DrawingEnumeratorBase.AutoFetch = previousAutoFetch;
        }
    }

    /// <summary>
    /// Rebuilds a straight dimension set from a new point list.
    ///
    /// The original set is DELETED and a new one is created, so <c>NewDimensionId</c> differs
    /// from <c>OldDimensionId</c> — any id held by the caller becomes stale. Style attributes
    /// and the offset are copied over, so the drawing keeps its look.
    ///
    /// This exists because Tekla Open API cannot remove a point from a set:
    /// <c>StraightDimensionSet</c> exposes only Attributes, Distance and the tag line offsets,
    /// with no point collection. Deleting and rebuilding is the only way to drop points.
    /// When points are only being ADDED, prefer <see cref="AddDimensionPoints"/> — it keeps
    /// the id and touches nothing else.
    ///
    /// NOT ATOMIC once the replacement exists. The order is deliberate — create, then delete —
    /// so a failure while building the new set leaves the original intact. But if
    /// <c>Delete()</c> or the commit after it throws, the drawing can end up holding BOTH the
    /// old and the new set. Tekla offers no transaction around this, so the caller should
    /// re-read the view after an error and remove whichever set is left over.
    /// </summary>
    /// <param name="distance">
    /// Signed offset from the points to the dimension line. Pass <c>null</c> to reuse the
    /// original set's <c>Distance</c> — but note that Tekla stores it WITHOUT a sign, and the
    /// side is decided by the direction vector given at creation, which the API does not expose.
    /// So reusing it can flip the dimension to the opposite side of the part. When the original
    /// line's position is known (get_dimension_contexts reports ReferenceLine), pass the signed
    /// value explicitly: negative puts the line left of / below the points.
    /// </param>
    public RecreateDimensionResult RecreateDimension(int dimensionId, double[] points, string direction, double? distance = null)
    {
        // Argument check first: a malformed point array is the caller's mistake and needs no
        // round trip into Tekla to diagnose. It also matters here because the original set is
        // only deleted much later — validation must never get far enough to touch the drawing.
        if (points == null || points.Length < 6 || points.Length % 3 != 0)
            return new RecreateDimensionResult
            {
                OldDimensionId = dimensionId,
                Error = "points must be a flat array [x0,y0,z0, x1,y1,z1, ...] with at least 2 points"
            };

        var activeDrawing = new DrawingHandler().GetActiveDrawing();
        if (activeDrawing == null)
            throw new DrawingNotOpenException();

        var previousAutoFetch = DrawingEnumeratorBase.AutoFetch;
        DrawingEnumeratorBase.AutoFetch = false;
        try
        {
            var original = FindDimensionSet(activeDrawing, dimensionId);
            if (original == null)
                return new RecreateDimensionResult
                {
                    OldDimensionId = dimensionId,
                    Error = $"DimensionSet {dimensionId} not found"
                };

            var view = original.GetView() as ViewBase;
            if (view == null)
                return new RecreateDimensionResult
                {
                    OldDimensionId = dimensionId,
                    Error = $"DimensionSet {dimensionId} has no owning view"
                };

            var viewId = view.GetIdentifier().ID;
            var originalDistance = original.Distance;
            var effectiveDistance = distance ?? originalDistance;

            StraightDimensionSet.StraightDimensionSetAttributes? attributes = null;
            try { attributes = original.Attributes; } catch { }

            var pointList = ToPointList(points);
            var dirVector = DimensionCreatePlacementHelper.ResolveDirection(direction);

            var handler = new StraightDimensionSetHandler();
            var created = attributes != null
                ? handler.CreateDimensionSet(view, pointList, dirVector, effectiveDistance, attributes)
                : handler.CreateDimensionSet(view, pointList, dirVector, effectiveDistance);

            if (created == null)
                return new RecreateDimensionResult
                {
                    OldDimensionId = dimensionId,
                    ViewId = viewId,
                    Error = "CreateDimensionSet returned null; original left untouched"
                };

            // Only drop the original once the replacement exists, so a failure above cannot
            // leave the drawing without the dimension.
            original.Delete();
            activeDrawing.CommitChanges("(MCP) RecreateDimension");

            // The offset is deliberately NOT corrected here. Measured behaviour, TS2025:
            //   * attributes copied from the original carry their own offset, which Tekla adds on
            //     top of the distance passed in — identical points and distance -200 put the line
            //     at X=-200 with "standard" attributes but at X=-604 with copied ones;
            //   * Distance is measured from the EXTREME point along the offset direction, and
            //     flipping its sign switches which extreme is used;
            //   * that direction is X for vertical dimensions, Y for horizontal ones and the
            //     perpendicular to the chain axis for inclined ones.
            // Forcing Distance to the requested value therefore moves the line somewhere else
            // again (verified: setting -200 landed the line at X=23, because the extreme point
            // sat at X=223). Until that convention is pinned down, the honest contract is to pass
            // the caller's distance through, report what Tekla actually applied, and let the
            // caller nudge the line with move_dimension, which changes Distance in place and is
            // exact.
            var appliedDistance = created.Distance;

            return new RecreateDimensionResult
            {
                Recreated = true,
                OldDimensionId = dimensionId,
                NewDimensionId = created.GetIdentifier().ID,
                ViewId = viewId,
                PointCount = pointList.Count,
                AttributesKept = attributes != null,
                Distance = appliedDistance,
                RequestedDistance = effectiveDistance,
                DistanceCorrection = appliedDistance - effectiveDistance
            };
        }
        finally
        {
            DrawingEnumeratorBase.AutoFetch = previousAutoFetch;
        }
    }

    private static void DiscardAddition(Tekla.Structures.Drawing.Drawing activeDrawing, StraightDimensionSet addition)
    {
        try
        {
            addition.Delete();
            activeDrawing.CommitChanges("(MCP) AddDimensionPoints cleanup");
        }
        catch
        {
            // Cleanup is best effort: the caller is already being told the merge failed.
        }
    }

    private static PointList ToPointList(double[] points)
    {
        var pointList = new PointList();
        for (int i = 0; i + 2 < points.Length; i += 3)
            pointList.Add(new Point(points[i], points[i + 1], points[i + 2]));
        return pointList;
    }

    private static StraightDimensionSet? FindDimensionSet(Tekla.Structures.Drawing.Drawing activeDrawing, int dimensionId)
    {
        var all = activeDrawing.GetSheet().GetAllObjects(typeof(StraightDimensionSet));
        while (all.MoveNext())
        {
            if (all.Current is StraightDimensionSet set && set.GetIdentifier().ID == dimensionId)
                return set;
        }

        return null;
    }

    private static int CountPoints(StraightDimensionSet set)
    {
        var count = 0;
        var objects = set.GetObjects();
        while (objects.MoveNext())
            count++;

        // A set of N points renders as N-1 segments.
        return count == 0 ? 0 : count + 1;
    }

    private static bool TryDeleteById(Tekla.Structures.Drawing.Drawing activeDrawing, Type objectType, int dimensionId)
    {
        var dimEnum = activeDrawing.GetSheet().GetAllObjects(objectType);
        while (dimEnum.MoveNext())
        {
            if (dimEnum.Current is not DrawingObject drawingObject)
                continue;
            if (drawingObject.GetIdentifier().ID != dimensionId)
                continue;

            drawingObject.Delete();
            activeDrawing.CommitChanges();
            return true;
        }

        return false;
    }
}
