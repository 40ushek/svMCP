using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;
using TeklaMcpServer.Api.Algorithms.Marks;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class TeklaDrawingMarkLayoutEntry
{
    public Mark Mark { get; set; } = null!;

    public int ViewId { get; set; }

    public MarkLayoutItem Item { get; set; } = null!;

    public double CenterX { get; set; }

    public double CenterY { get; set; }
}

internal static class TeklaDrawingMarkLayoutAdapter
{
    private const double MovementVerificationEpsilon = 0.05;

    public static List<TeklaDrawingMarkLayoutEntry> CollectEntries(View view, Model model)
    {
        var entries = new List<TeklaDrawingMarkLayoutEntry>();
        var viewId = view.GetIdentifier().ID;
        var viewWidth = view.Width;
        var viewHeight = view.Height;

        // Layout runs in view-local coordinates.
        var boundsMinX = -(viewWidth * 0.5) - 10.0;
        var boundsMaxX = +(viewWidth * 0.5) + 10.0;
        var boundsMinY = -(viewHeight * 0.5) - 10.0;
        var boundsMaxY = +(viewHeight * 0.5) + 10.0;
        var markEnum = view.GetAllObjects(typeof(Mark));

        // NOTE: Do NOT set the work plane to view.DisplayCoordinateSystem here.
        // That 3D transformation shifts mark.GetAxisAlignedBoundingBox() into model-space
        // coordinates, producing a center that is offset from the actual view-local 2D position
        // (mark.InsertionPoint). ApplyPlacements moves marks by (placement - center), so a
        // wrong center causes wrong displacement. TryGetRelatedPartAxisInView uses
        // GetPartGeometryInView, which already returns view-local coordinates without a
        // work-plane override. Leave the work plane at its default.
        while (markEnum.MoveNext())
        {
            if (markEnum.Current is not Mark mark)
                continue;

                // CRITICAL: All mark polygon geometry for layout and collision detection
                // MUST come from MarkGeometryResolver.Build(). Do NOT use Tekla's raw
                // GetAxisAlignedBoundingBox() or GetObjectAlignedBoundingBox() here —
                // they return AABB/OBB that ignore part axis orientation for BaseLinePlacing marks.
                // MarkGeometryResolver resolves the true axis from the related part, the baseline
                // placing line, or mark.Attributes.Angle as fallback, producing correctly oriented
                // OBB corners. These corners are what gets drawn by draw_debug_overlay.
                var geometry = MarkGeometryResolver.Build(mark, model, viewId);
                var centerLocalX = geometry.CenterX;
                var centerLocalY = geometry.CenterY;
                var widthLocal = geometry.MaxX - geometry.MinX;
                var heightLocal = geometry.MaxY - geometry.MinY;

                // Skip marks with zero-size geometry — these are "ghost" marks that Tekla
                // created but cannot render (empty content, part not visible in view, etc.).
                // Their bbox and corners are degenerate; including them causes layout crashes.
                if (widthLocal < 0.1 && heightLocal < 0.1)
                    continue;
                // LocalCorners are geometry.Corners expressed relative to the mark center.
                // They are the authoritative collision shape used by MarkLayoutEngine.Intersects.
                var localCorners = geometry.Corners
                    .Select(c => new[] { c[0] - centerLocalX, c[1] - centerLocalY })
                    .ToList();
                var anchorLocalX = centerLocalX;
                var anchorLocalY = centerLocalY;
                var hasLeaderLine = false;

                // Canonical baseline movement axis comes from MarkGeometryResolver so
                // collision geometry and movement stay aligned. Only fall back to
                // mark angle when geometry could not resolve a usable axis.
                var hasAxis = false;
                var axisDx = 0.0;
                var axisDy = 0.0;
                if (mark.Placing is BaseLinePlacing)
                {
                    // Use resolved geometry axis (from related part → placing line → mark angle fallback).
                    // This MUST match the polygon orientation used for collision detection.
                    // mark.Attributes.Angle is the text rotation angle and may differ from the part axis
                    // (e.g. 90° text on a horizontal beam), causing marks to move perpendicular to the overlap.
                    if (geometry.HasAxis && (Math.Abs(geometry.AxisDx) >= 0.001 || Math.Abs(geometry.AxisDy) >= 0.001))
                    {
                        axisDx = geometry.AxisDx;
                        axisDy = geometry.AxisDy;
                        hasAxis = true;
                    }
                    else
                    {
                        var rad = mark.Attributes.Angle * Math.PI / 180.0;
                        var angleDx = Math.Cos(rad);
                        var angleDy = Math.Sin(rad);
                        if (Math.Abs(angleDx) >= 0.001 || Math.Abs(angleDy) >= 0.001)
                        {
                            axisDx = angleDx;
                            axisDy = angleDy;
                            hasAxis = true;
                        }
                    }
                }

                if (mark.Placing is LeaderLinePlacing leaderLinePlacing)
                {
                    anchorLocalX = leaderLinePlacing.StartPoint.X;
                    anchorLocalY = leaderLinePlacing.StartPoint.Y;
                    hasLeaderLine = true;
                }

                entries.Add(new TeklaDrawingMarkLayoutEntry
                {
                    Mark = mark,
                    ViewId = viewId,
                    CenterX = centerLocalX,
                    CenterY = centerLocalY,
                    Item = new MarkLayoutItem
                    {
                        Id            = mark.GetIdentifier().ID,
                        AnchorX       = anchorLocalX,
                        AnchorY       = anchorLocalY,
                        CurrentX      = centerLocalX,
                        CurrentY      = centerLocalY,
                        Width         = widthLocal,
                        Height        = heightLocal,
                        HasLeaderLine = hasLeaderLine,
                        HasAxis       = hasAxis,
                        AxisDx        = axisDx,
                        AxisDy        = axisDy,
                        LocalCorners  = localCorners,
                        BoundsMinX    = boundsMinX,
                        BoundsMaxX    = boundsMaxX,
                        BoundsMinY    = boundsMinY,
                        BoundsMaxY    = boundsMaxY,
                    }
                });
        }

        return entries;
    }

    public static List<int> ApplyPlacements(
        IReadOnlyList<TeklaDrawingMarkLayoutEntry> entries,
        IReadOnlyDictionary<int, MarkLayoutPlacement> placementsById,
        Model model)
    {
        var movedIds = new List<int>();

        foreach (var entry in entries)
        {
            var id = entry.Mark.GetIdentifier().ID;
            if (!placementsById.TryGetValue(id, out var placement))
                continue;

            var dx = placement.X - entry.CenterX;
            var dy = placement.Y - entry.CenterY;
            if (entry.Item.HasAxis && !entry.Item.HasLeaderLine)
            {
                var distanceAlongAxis = (dx * entry.Item.AxisDx) + (dy * entry.Item.AxisDy);
                dx = entry.Item.AxisDx * distanceAlongAxis;
                dy = entry.Item.AxisDy * distanceAlongAxis;
            }

            if (Math.Abs(dx) < 0.001 && Math.Abs(dy) < 0.001)
                continue;

            var beforeInsertion = entry.Mark.InsertionPoint;
            var beforeCenterX = entry.CenterX;
            var beforeCenterY = entry.CenterY;
            var insertionPoint = entry.Mark.InsertionPoint;
            insertionPoint.X += dx;
            insertionPoint.Y += dy;
            entry.Mark.InsertionPoint = insertionPoint;
            if (!entry.Mark.Modify())
                continue;

            if (!TryReloadMarkState(entry.Mark, entry.ViewId, model, out var actualInsertion, out var actualCenterX, out var actualCenterY))
                continue;

            var insertionChanged =
                Math.Abs(actualInsertion.X - beforeInsertion.X) > MovementVerificationEpsilon ||
                Math.Abs(actualInsertion.Y - beforeInsertion.Y) > MovementVerificationEpsilon;
            var centerChanged =
                Math.Abs(actualCenterX - beforeCenterX) > MovementVerificationEpsilon ||
                Math.Abs(actualCenterY - beforeCenterY) > MovementVerificationEpsilon;

            if (!insertionChanged && !centerChanged)
                continue;

            entry.CenterX = actualCenterX;
            entry.CenterY = actualCenterY;
            movedIds.Add(id);
        }

        return movedIds;
    }

    private static bool TryReloadMarkState(
        Mark mark,
        int viewId,
        Model model,
        out Point insertionPoint,
        out double centerX,
        out double centerY)
    {
        insertionPoint = mark.InsertionPoint;
        centerX = 0.0;
        centerY = 0.0;
        try
        {
            var geometry = MarkGeometryResolver.Build(mark, model, viewId);
            insertionPoint = mark.InsertionPoint;
            centerX = geometry.CenterX;
            centerY = geometry.CenterY;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
