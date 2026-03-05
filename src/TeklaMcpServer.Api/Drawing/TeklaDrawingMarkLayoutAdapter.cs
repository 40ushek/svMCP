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

    public MarkLayoutItem Item { get; set; } = null!;

    public double CenterX { get; set; }

    public double CenterY { get; set; }
}

internal static class TeklaDrawingMarkLayoutAdapter
{
    public static List<TeklaDrawingMarkLayoutEntry> CollectEntries(IEnumerable<View> views)
    {
        var entries = new List<TeklaDrawingMarkLayoutEntry>();

        foreach (var view in views)
        {
            var viewOriginX = view.Origin.X;
            var viewOriginY = view.Origin.Y;
            var scale = view.Attributes.Scale;
            var markEnum = view.GetAllObjects(typeof(Mark));

            while (markEnum.MoveNext())
            {
                if (markEnum.Current is not Mark mark)
                    continue;

                var bbox = mark.GetAxisAlignedBoundingBox();
                var centerX = (bbox.MinPoint.X + bbox.MaxPoint.X) / 2.0;
                var centerY = (bbox.MinPoint.Y + bbox.MaxPoint.Y) / 2.0;
                var width = bbox.MaxPoint.X - bbox.MinPoint.X;
                var height = bbox.MaxPoint.Y - bbox.MinPoint.Y;
                var anchorX = centerX;
                var anchorY = centerY;
                var hasLeaderLine = false;

                if (mark.Placing is LeaderLinePlacing leaderLinePlacing && scale > 0)
                {
                    anchorX = viewOriginX + (leaderLinePlacing.StartPoint.X / scale);
                    anchorY = viewOriginY + (leaderLinePlacing.StartPoint.Y / scale);
                    hasLeaderLine = true;
                }

                entries.Add(new TeklaDrawingMarkLayoutEntry
                {
                    Mark = mark,
                    CenterX = centerX,
                    CenterY = centerY,
                    Item = new MarkLayoutItem
                    {
                        Id = mark.GetIdentifier().ID,
                        AnchorX = anchorX,
                        AnchorY = anchorY,
                        CurrentX = centerX,
                        CurrentY = centerY,
                        Width = width,
                        Height = height,
                        HasLeaderLine = hasLeaderLine
                    }
                });
            }
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
