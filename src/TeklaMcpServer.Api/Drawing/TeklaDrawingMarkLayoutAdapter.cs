using System;
using System.Collections.Generic;
using System.Linq;
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
        var workPlaneHandler = model.GetWorkPlaneHandler();
        var originalPlane = workPlaneHandler.GetCurrentTransformationPlane();
        workPlaneHandler.SetCurrentTransformationPlane(new TransformationPlane(view.DisplayCoordinateSystem));

        try
        {
            while (markEnum.MoveNext())
            {
                if (markEnum.Current is not Mark mark)
                    continue;

                var geometry = MarkGeometryHelper.Build(mark, model, viewId);
                var centerLocalX = geometry.CenterX;
                var centerLocalY = geometry.CenterY;
                var widthLocal = geometry.MaxX - geometry.MinX;
                var heightLocal = geometry.MaxY - geometry.MinY;
                var localCorners = geometry.Corners
                    .Select(c => new[] { c[0] - centerLocalX, c[1] - centerLocalY })
                    .ToList();
                var anchorLocalX = centerLocalX;
                var anchorLocalY = centerLocalY;
                var hasLeaderLine = false;
                var hasAxis = geometry.HasAxis;
                var axisDx = geometry.AxisDx;
                var axisDy = geometry.AxisDy;

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
        }
        finally
        {
            workPlaneHandler.SetCurrentTransformationPlane(originalPlane);
        }

        return entries;
    }

    public static List<int> ApplyPlacements(
        IReadOnlyList<TeklaDrawingMarkLayoutEntry> entries,
        IReadOnlyDictionary<int, MarkLayoutPlacement> placementsById)
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

            if (!TryReloadMarkState(entry.Mark, entry.ViewId, out var actualInsertion, out var actualCenterX, out var actualCenterY))
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
        out Point insertionPoint,
        out double centerX,
        out double centerY)
    {
        insertionPoint = mark.InsertionPoint;
        centerX = 0.0;
        centerY = 0.0;

        var ownerView = mark.GetView();
        if (ownerView == null)
            return false;

        var targetId = mark.GetIdentifier().ID;
        var previousAutoFetch = DrawingEnumeratorBase.AutoFetch;
        DrawingEnumeratorBase.AutoFetch = true;
        try
        {
            var marks = ownerView.GetAllObjects(typeof(Mark));
            while (marks.MoveNext())
            {
                if (marks.Current is not Mark currentMark)
                    continue;

                if (currentMark.GetIdentifier().ID != targetId)
                    continue;

                insertionPoint = currentMark.InsertionPoint;
                var bbox = currentMark.GetAxisAlignedBoundingBox();
                centerX = (bbox.MinPoint.X + bbox.MaxPoint.X) / 2.0;
                centerY = (bbox.MinPoint.Y + bbox.MaxPoint.Y) / 2.0;
                return true;
            }
        }
        finally
        {
            DrawingEnumeratorBase.AutoFetch = previousAutoFetch;
        }

        return false;
    }
}
