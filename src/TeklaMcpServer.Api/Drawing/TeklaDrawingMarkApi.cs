using System;
using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Model;

namespace TeklaMcpServer.Api.Drawing;

public sealed class TeklaDrawingMarkApi : IDrawingMarkApi
{
    private readonly Model _model;

    public TeklaDrawingMarkApi(Model model) => _model = model;

    public GetMarksResult GetMarks(int? viewId)
    {
        var activeDrawing = new DrawingHandler().GetActiveDrawing();
        if (activeDrawing == null)
            throw new DrawingNotOpenException();

        // Performance: disable auto-fetch during enumeration
        DrawingEnumeratorBase.AutoFetch = false;

        DrawingObjectEnumerator markObjects;
        if (viewId.HasValue)
        {
            var view = EnumerateViews(activeDrawing).FirstOrDefault(v => v.GetIdentifier().ID == viewId.Value)
                ?? throw new ViewNotFoundException(viewId.Value);
            markObjects = view.GetAllObjects(typeof(Mark));
        }
        else
        {
            markObjects = activeDrawing.GetSheet().GetAllObjects(typeof(Mark));
        }

        var marks = new List<DrawingMarkInfo>();

        while (markObjects.MoveNext())
        {
            if (markObjects.Current is not Mark mark) continue;

            var bbox = mark.GetAxisAlignedBoundingBox();
            var ins  = mark.InsertionPoint;
            var info = new DrawingMarkInfo
            {
                Id         = mark.GetIdentifier().ID,
                InsertionX = Math.Round(ins.X, 1),
                InsertionY = Math.Round(ins.Y, 1),
                BboxMinX   = Math.Round(bbox.MinPoint.X, 1),
                BboxMinY   = Math.Round(bbox.MinPoint.Y, 1),
                BboxMaxX    = Math.Round(bbox.MaxPoint.X, 1),
                BboxMaxY    = Math.Round(bbox.MaxPoint.Y, 1),
                PlacingType = mark.Placing?.GetType().Name ?? "null",
                PlacingX    = mark.Placing is LeaderLinePlacing lp2 ? Math.Round(lp2.StartPoint.X, 2) : 0,
                PlacingY    = mark.Placing is LeaderLinePlacing lp3 ? Math.Round(lp3.StartPoint.Y, 2) : 0
            };

            // Resolve model object ID from first related drawing object
            var related = mark.GetRelatedObjects();
            while (related.MoveNext())
            {
                if (related.Current is Tekla.Structures.Drawing.ModelObject mo)
                {
                    info.ModelId = mo.ModelIdentifier.ID;
                    break;
                }
            }

            // Read property element names and their computed values
            var contentEnum = mark.Attributes.Content.GetEnumerator();
            while (contentEnum.MoveNext())
            {
                if (contentEnum.Current is PropertyElement prop)
                    info.Properties.Add(new MarkPropertyValue { Name = prop.Name, Value = prop.Value });
            }

            marks.Add(info);
        }

        // Detect pairwise AABB overlaps
        var overlaps = new List<MarkOverlap>();
        for (int i = 0; i < marks.Count; i++)
        for (int j = i + 1; j < marks.Count; j++)
        {
            var a = marks[i]; var b = marks[j];
            if (a.BboxMaxX > b.BboxMinX && b.BboxMaxX > a.BboxMinX &&
                a.BboxMaxY > b.BboxMinY && b.BboxMaxY > a.BboxMinY)
                overlaps.Add(new MarkOverlap { IdA = a.Id, IdB = b.Id });
        }

        return new GetMarksResult { Total = marks.Count, Marks = marks, Overlaps = overlaps };
    }

    public ResolveMarksResult ResolveMarkOverlaps(double margin)
    {
        var activeDrawing = new DrawingHandler().GetActiveDrawing();
        if (activeDrawing == null)
            throw new DrawingNotOpenException();

        DrawingEnumeratorBase.AutoFetch = false;

        // Load all marks with live objects + mutable bbox
        var entries = new List<(Mark mark, double minX, double minY, double maxX, double maxY)>();
        var markObjects = activeDrawing.GetSheet().GetAllObjects(typeof(Mark));
        while (markObjects.MoveNext())
        {
            if (markObjects.Current is not Mark mark) continue;
            var bb = mark.GetAxisAlignedBoundingBox();
            entries.Add((mark, bb.MinPoint.X, bb.MinPoint.Y, bb.MaxPoint.X, bb.MaxPoint.Y));
        }

        // Accumulate total displacement per mark ID
        var totalDx = new Dictionary<int, double>();
        var totalDy = new Dictionary<int, double>();

        const int MAX_ITER = 30;
        int iter = 0;
        bool anyOverlap = true;

        while (anyOverlap && iter < MAX_ITER)
        {
            anyOverlap = false;
            iter++;

            for (int i = 0; i < entries.Count; i++)
            for (int j = i + 1; j < entries.Count; j++)
            {
                var (_, minXa, minYa, maxXa, maxYa) = entries[i];
                var (_, minXb, minYb, maxXb, maxYb) = entries[j];

                double ox = Math.Min(maxXa, maxXb) - Math.Max(minXa, minXb);
                double oy = Math.Min(maxYa, maxYb) - Math.Max(minYa, minYb);
                if (ox <= 0 || oy <= 0) continue;

                anyOverlap = true;

                double cxa = (minXa + maxXa) / 2, cya = (minYa + maxYa) / 2;
                double cxb = (minXb + maxXb) / 2, cyb = (minYb + maxYb) / 2;

                double dx = 0, dy = 0;
                if (ox <= oy)
                {
                    double push = ox / 2 + margin / 2;
                    dx = cxb >= cxa ? push : -push;
                }
                else
                {
                    double push = oy / 2 + margin / 2;
                    dy = cyb >= cya ? push : -push;
                }

                // Update B's bbox in memory
                var eb = entries[j];
                entries[j] = (eb.mark, eb.minX + dx, eb.minY + dy, eb.maxX + dx, eb.maxY + dy);

                var id = eb.mark.GetIdentifier().ID;
                totalDx[id] = (totalDx.TryGetValue(id, out var ax) ? ax : 0) + dx;
                totalDy[id] = (totalDy.TryGetValue(id, out var ay) ? ay : 0) + dy;
            }
        }

        // Count remaining overlaps
        int remaining = 0;
        for (int i = 0; i < entries.Count; i++)
        for (int j = i + 1; j < entries.Count; j++)
        {
            var (_, minXa, minYa, maxXa, maxYa) = entries[i];
            var (_, minXb, minYb, maxXb, maxYb) = entries[j];
            if (Math.Min(maxXa, maxXb) - Math.Max(minXa, minXb) > 0 &&
                Math.Min(maxYa, maxYb) - Math.Max(minYa, minYb) > 0)
                remaining++;
        }

        // Move only the text box — InsertionPoint is in sheet coordinates
        // LeaderLinePlacing.StartPoint is the arrow attachment on the model object — do NOT touch it
        foreach (var (mark, _, _, _, _) in entries)
        {
            var id = mark.GetIdentifier().ID;
            if (!totalDx.ContainsKey(id)) continue;
            double dx = totalDx[id], dy = totalDy[id];

            var ins = mark.InsertionPoint;
            ins.X += dx; ins.Y += dy;
            mark.InsertionPoint = ins;
            mark.Modify();
        }

        activeDrawing.CommitChanges();

        return new ResolveMarksResult
        {
            MarksMovedCount   = totalDx.Count,
            MovedIds          = totalDx.Keys.ToList(),
            Iterations        = iter,
            RemainingOverlaps = remaining
        };
    }

    private static IEnumerable<View> EnumerateViews(Tekla.Structures.Drawing.Drawing drawing)
    {
        var enumerator = drawing.GetSheet().GetViews();
        while (enumerator.MoveNext())
            if (enumerator.Current is View v)
                yield return v;
    }
}
