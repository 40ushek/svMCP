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
    /// Nothing is destroyed: the style attributes and offset carry over and the sheet is not
    /// rebuilt. This is the cheap way to extend a chain. The set is RENUMBERED though — see below.
    ///
    /// Mechanics: Tekla has no "add point" call, so a throwaway set is built from the supplied
    /// points and handed to <c>DimensionSetBase.AddToDimensionSet</c>, which is documented as
    /// "adds a dimension set to the current dimension set" — it takes a whole set, never a single
    /// point, and does not expose the point list.
    ///
    /// The sequence is the one recommended on the Tekla forum thread "Add dimension points by
    /// API": Select() on the target, then AddToDimensionSet, then Modify(), then CommitChanges.
    ///
    /// The result is RENUMBERED — the merged set appears under a new identifier and the id passed
    /// in stops resolving. That is what makes a working merge look broken: reading the result back
    /// by the original id finds nothing. The merged set is therefore located by geometry, as the
    /// one carrying every point the target had before, and its new id comes back in
    /// MergedDimensionId. Success is judged by the point count growing, never by the return value,
    /// which only means the call was accepted.
    ///
    /// The source set is a throwaway and is removed if it survives the merge; the method makes no
    /// assumption either way, since AddToDimensionSet does not promise to consume it. When nothing
    /// is gained the throwaway is dropped — but the merge call itself was already committed by
    /// then, and Tekla offers no transaction to undo it, so the caller should re-read the view
    /// rather than assume the drawing is untouched.
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

            var viewId = view.GetIdentifier().ID;

            // Snapshot the target's geometry: after the merge it is renumbered, and these points
            // are the only way left to recognise it.
            var pointsBefore = CollectSetPoints(target);
            var pointCountBefore = pointsBefore.Count;

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

            var additionId = addition.GetIdentifier().ID;

            bool accepted;
            try
            {
                // Select() then Modify() around the merge, per the Tekla forum thread
                // "Add dimension points by API": the target has to be the selected object for the
                // change to be applied to it, and Modify() is what writes it back.
                target.Select();
                accepted = target.AddToDimensionSet(addition);
                target.Modify();
            }
            catch
            {
                DiscardAddition(activeDrawing, addition);
                throw;
            }

            activeDrawing.CommitChanges("(MCP) AddDimensionPoints");

            // The merged set carries a NEW id, so it has to be found by geometry — the one holding
            // both the target's original points and the ones just added. Looking it up by
            // dimensionId finds nothing and makes a successful merge look like a failure.
            var addedPoints = new List<(double X, double Y)>();
            for (var i = 0; i + 2 < points.Length; i += 3)
                addedPoints.Add((points[i], points[i + 1]));

            var (merged, matchCount) = FindMergedSet(activeDrawing, viewId, pointsBefore, addedPoints, additionId);
            var pointCountAfter = merged == null ? pointCountBefore : CountPoints(merged);

            if (merged == null || pointCountAfter <= pointCountBefore)
            {
                // Nothing was gained: drop the throwaway set so the drawing is left as it was.
                var stray = FindDimensionSet(activeDrawing, additionId);
                if (stray != null)
                    DiscardAddition(activeDrawing, stray);

                return new AddDimensionPointsResult
                {
                    DimensionId = dimensionId,
                    AddedPointCount = pointList.Count,
                    PointCountAfter = pointCountAfter,
                    // Deliberately not claiming the drawing is untouched: the merge was already
                    // committed by this point and Tekla offers no transaction to undo it. All that
                    // is certain is that no set carrying both the old and the new points was found
                    // and that the temporary set is gone. Re-read the view before continuing.
                    Error = (matchCount > 1
                        ? $"{matchCount} sets in the view carry both the original and the new points, so the merged one cannot be identified; "
                        : accepted
                            ? $"AddToDimensionSet was accepted but no set carries both the original and the new points (count {pointCountAfter}); "
                            : "AddToDimensionSet returned false; ") +
                        "the temporary set was removed, but the merge was already committed — re-read the view to confirm its state"
                };
            }

            var mergedId = merged.GetIdentifier().ID;

            // If the source survived the merge as a separate object, it is now a duplicate of
            // points that already live in the merged set.
            if (mergedId != additionId)
            {
                var leftover = FindDimensionSet(activeDrawing, additionId);
                if (leftover != null)
                    DiscardAddition(activeDrawing, leftover);
            }

            return new AddDimensionPointsResult
            {
                Added = true,
                DimensionId = dimensionId,
                MergedDimensionId = mergedId,
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
    /// This exists because Tekla Open API cannot CHANGE THE NUMBER of points in a set:
    /// <c>StraightDimensionSet</c> exposes only Attributes, Distance and the tag line offsets,
    /// with no point collection, so dropping a point means rebuilding.
    ///
    /// Repositioning MAY not need this method. The child <c>StraightDimension</c> segments —
    /// reached with <c>set.GetObjects(new[] { typeof(StraightDimension) })</c> — declare writable
    /// <c>StartPoint</c>, <c>EndPoint</c>, <c>Distance</c> and <c>UpDirection</c>; the setters are
    /// there in the installed 2025 assembly and the approach is suggested on the Tekla forum.
    ///
    /// NOT VERIFIED on a real drawing. A setter existing proves nothing about the effect —
    /// AddToDimensionSet also returns true while appearing to do nothing. Until someone assigns a
    /// StartPoint on a live drawing and re-reads it, rebuilding through this method is the only
    /// way to change points that is actually known to work.
    /// When points are only being ADDED, prefer <see cref="AddDimensionPoints"/> — it does not
    /// tear the chain down, though it renumbers it just the same.
    ///
    /// NOT ATOMIC once the replacement exists. The order is deliberate — create, then delete —
    /// so a failure while building the new set leaves the original intact. But if
    /// <c>Delete()</c> or the commit after it throws, the drawing can end up holding BOTH the
    /// old and the new set. Tekla offers no transaction around this, so the caller should
    /// re-read the view after an error and remove whichever set is left over.
    /// </summary>
    /// <param name="direction">
    /// Sets the axis AND the side the dimension line sits on: <c>(0,1,0)</c> / <c>(0,-1,0)</c> for a
    /// horizontal dimension, <c>(1,0,0)</c> / <c>(-1,0,0)</c> for a vertical one.
    ///
    /// <c>points[0]</c> DOES become the true start — for both axes. Confirmed on a live drawing
    /// (view 1262, both a horizontal and a vertical chain) by flipping the input order back and
    /// forth and checking the visible start point in Tekla itself each time, not just the
    /// read-back. get_drawing_dimensions/get_dimension_contexts normalise point order for display
    /// and therefore do not show which point was passed first. For a single simple chain, the
    /// true start can be reconstructed from segment Start/End connectivity for both axes; do not
    /// infer it for branching, disconnected or otherwise ambiguous segments.
    /// </param>
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

            // Creation does not honour the requested offset: attributes copied from the original
            // carry their own, which Tekla adds on top — the same points and distance land the
            // line in different places with "standard" attributes and with copied ones. Passing a
            // corrected value into CreateDimensionSet does not help either, because Distance is
            // measured from the extreme point along the offset direction and the sign of that
            // direction decides which extreme.
            //
            // Assigning Distance on the already-created set instead is exact. This is NOT what
            // move_dimension does: that one shifts Distance by a delta, this sets it to a value.
            // Correcting by hand after every recreate landed the line correctly every time over a
            // day of use, so do it here instead of making every caller repeat it.
            var createdDistance = created.Distance;
            var newDimensionId = created.GetIdentifier().ID;
            var appliedDistance = createdDistance;
            string? correctionError = null;

            if (System.Math.Abs(createdDistance - effectiveDistance) > 1e-6)
            {
                try
                {
                    created.Distance = effectiveDistance;
                    created.Modify();
                    activeDrawing.CommitChanges("(MCP) RecreateDimension offset");

                    // Read the offset back off a freshly fetched set rather than off `created`.
                    // That instance holds the value just assigned to it whether or not Tekla
                    // accepted it, so reporting from it would claim a correction the sheet does
                    // not show.
                    var reread = FindDimensionSet(activeDrawing, newDimensionId);
                    if (reread != null)
                        appliedDistance = reread.Distance;
                    else
                        correctionError =
                            $"offset correction was committed but set {newDimensionId} could not be found to verify it";
                }
                catch (System.Exception ex)
                {
                    // The recreate itself succeeded and must stand — only the line sits at the
                    // wrong offset. Report that precisely instead of failing the whole call, and
                    // leave appliedDistance at the pre-correction value so the result cannot
                    // claim a move that did not happen.
                    correctionError = $"offset correction failed: {ex.Message}";
                }
            }

            return new RecreateDimensionResult
            {
                Recreated = true,
                OldDimensionId = dimensionId,
                NewDimensionId = newDimensionId,
                ViewId = viewId,
                PointCount = pointList.Count,
                AttributesKept = attributes != null,
                Distance = appliedDistance,
                RequestedDistance = effectiveDistance,
                DistanceCorrection = appliedDistance - createdDistance,
                DistanceCorrectionError = correctionError
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

    private const double PointMatchTolerance = 0.5;

    /// <summary>Distinct snap points of a set, read off its segment endpoints.</summary>
    private static List<(double X, double Y)> CollectSetPoints(StraightDimensionSet set)
    {
        var points = new List<(double X, double Y)>();
        var objects = set.GetObjects();
        while (objects.MoveNext())
        {
            if (objects.Current is not StraightDimension segment)
                continue;

            foreach (var candidate in new[]
                     {
                         (segment.StartPoint.X, segment.StartPoint.Y),
                         (segment.EndPoint.X, segment.EndPoint.Y)
                     })
            {
                if (!points.Any(p => Near(p, candidate)))
                    points.Add(candidate);
            }
        }

        return points;
    }

    private static bool Near((double X, double Y) a, (double X, double Y) b)
        => System.Math.Abs(a.X - b.X) <= PointMatchTolerance
        && System.Math.Abs(a.Y - b.Y) <= PointMatchTolerance;

    /// <summary>
    /// Finds the merged set: the one in the same view that carries BOTH every point the target
    /// had and every point that was added.
    ///
    /// Needed because merging renumbers the target — the combined set appears under a new
    /// identifier, so looking it up by the id passed in finds nothing and a working merge looks
    /// like a failure. Matching on geometry survives the renumbering.
    ///
    /// Requiring the added points as well as the old ones is what keeps this honest: matching on
    /// the old points alone would happily return an untouched duplicate chain that overlaps the
    /// target, and report someone else's id as the result. The view is checked too, since two
    /// views of the same part carry the same coordinates.
    ///
    /// If more than one set still qualifies, the answer is refused rather than guessed. Picking
    /// the richest candidate would hand back an id the caller then edits or deletes, and on a busy
    /// sheet with overlapping chains that is a silent way to damage the wrong dimension.
    /// </summary>
    private static (StraightDimensionSet? Set, int MatchCount) FindMergedSet(
        Tekla.Structures.Drawing.Drawing activeDrawing,
        int viewId,
        IReadOnlyList<(double X, double Y)> pointsBefore,
        IReadOnlyList<(double X, double Y)> pointsAdded,
        int excludeId)
    {
        StraightDimensionSet? found = null;
        var matches = 0;

        var all = activeDrawing.GetSheet().GetAllObjects(typeof(StraightDimensionSet));
        while (all.MoveNext())
        {
            if (all.Current is not StraightDimensionSet candidate)
                continue;
            if (candidate.GetIdentifier().ID == excludeId)
                continue;
            if (candidate.GetView() is not ViewBase candidateView || candidateView.GetIdentifier().ID != viewId)
                continue;

            var candidatePoints = CollectSetPoints(candidate);
            if (!pointsBefore.All(point => candidatePoints.Any(p => Near(p, point))))
                continue;
            if (!pointsAdded.All(point => candidatePoints.Any(p => Near(p, point))))
                continue;

            matches++;
            found ??= candidate;
        }

        return matches == 1 ? (found, 1) : (null, matches);
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
