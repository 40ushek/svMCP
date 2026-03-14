using System;
using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
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
    public static List<TeklaDrawingMarkLayoutEntry> CollectEntries(View view)
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

        while (markEnum.MoveNext())
        {
            if (markEnum.Current is not Mark mark)
                continue;

            var bbox = mark.GetAxisAlignedBoundingBox();
            var centerLocalX = (bbox.MinPoint.X + bbox.MaxPoint.X) / 2.0;
            var centerLocalY = (bbox.MinPoint.Y + bbox.MaxPoint.Y) / 2.0;
            var widthLocal = bbox.MaxPoint.X - bbox.MinPoint.X;
            var heightLocal = bbox.MaxPoint.Y - bbox.MinPoint.Y;
            var anchorLocalX = centerLocalX;
            var anchorLocalY = centerLocalY;
            var hasLeaderLine = false;
            var hasAxis = false;
            var axisDx = 0.0;
            var axisDy = 0.0;

            if (mark.Placing is LeaderLinePlacing leaderLinePlacing)
            {
                anchorLocalX = leaderLinePlacing.StartPoint.X;
                anchorLocalY = leaderLinePlacing.StartPoint.Y;
                hasLeaderLine = true;
            }
            else if (mark.Placing is BaseLinePlacing baseLinePlacing)
            {
                axisDx = baseLinePlacing.EndPoint.X - baseLinePlacing.StartPoint.X;
                axisDy = baseLinePlacing.EndPoint.Y - baseLinePlacing.StartPoint.Y;
                var axisLength = Math.Sqrt((axisDx * axisDx) + (axisDy * axisDy));
                if (axisLength >= 0.001)
                {
                    axisDx /= axisLength;
                    axisDy /= axisLength;
                    hasAxis = true;
                }
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

            var insertionPoint = entry.Mark.InsertionPoint;
            insertionPoint.X += dx;
            insertionPoint.Y += dy;
            entry.Mark.InsertionPoint = insertionPoint;
            entry.Mark.Modify();
            movedIds.Add(id);
        }

        return movedIds;
    }
}
