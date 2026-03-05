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

        // Load marks per view so we can compute anchor (LeaderLinePlacing.StartPoint) in sheet coords.
        // Sheet coords: sheetPt = viewOrigin + viewPt / scale
        var entries = new List<(Mark mark, double minX, double minY, double maxX, double maxY, double anchorX, double anchorY)>();
        foreach (var view in EnumerateViews(activeDrawing))
        {
            double vox = view.Origin.X, voy = view.Origin.Y;
            double scale = view.Attributes.Scale;
            var markEnum = view.GetAllObjects(typeof(Mark));
            while (markEnum.MoveNext())
            {
                if (markEnum.Current is not Mark mark) continue;
                var bb = mark.GetAxisAlignedBoundingBox();
                double ax = double.NaN, ay = double.NaN;
                if (mark.Placing is LeaderLinePlacing lp && scale > 0)
                {
                    ax = vox + lp.StartPoint.X / scale;
                    ay = voy + lp.StartPoint.Y / scale;
                }
                entries.Add((mark, bb.MinPoint.X, bb.MinPoint.Y, bb.MaxPoint.X, bb.MaxPoint.Y, ax, ay));
            }
        }

        var totalDx = new Dictionary<int, double>();
        var totalDy = new Dictionary<int, double>();

        const int MAX_ITER = 30;
        int iter = 0;
        bool anyOverlap = true;

        while (anyOverlap && iter < MAX_ITER)
        {
            anyOverlap = false;
            iter++;

            // Mark-mark push-apart: both marks move, biased by distance to anchor
            // (mark closer to its anchor moves less — preserve good positions)
            for (int i = 0; i < entries.Count; i++)
            for (int j = i + 1; j < entries.Count; j++)
            {
                var (markA, minXa, minYa, maxXa, maxYa, axA, ayA) = entries[i];
                var (markB, minXb, minYb, maxXb, maxYb, axB, ayB) = entries[j];

                double ox = Math.Min(maxXa, maxXb) - Math.Max(minXa, minXb);
                double oy = Math.Min(maxYa, maxYb) - Math.Max(minYa, minYb);
                if (ox <= 0 || oy <= 0) continue;

                anyOverlap = true;
                double cxa = (minXa + maxXa) / 2, cya = (minYa + maxYa) / 2;
                double cxb = (minXb + maxXb) / 2, cyb = (minYb + maxYb) / 2;
                ComputeSplit(cxa, cya, axA, ayA, cxb, cyb, axB, ayB, out double ra, out double rb);

                double dxA, dyA, dxB, dyB;
                if (ox <= oy)
                {
                    double push = ox + margin;
                    bool bRight = cxb >= cxa;
                    dxA = (bRight ? -push : push) * ra;
                    dxB = (bRight ? push : -push) * rb;
                    dyA = dyB = 0;
                }
                else
                {
                    double push = oy + margin;
                    bool bAbove = cyb >= cya;
                    dxA = dxB = 0;
                    dyA = (bAbove ? -push : push) * ra;
                    dyB = (bAbove ? push : -push) * rb;
                }

                entries[i] = (markA, minXa + dxA, minYa + dyA, maxXa + dxA, maxYa + dyA, axA, ayA);
                var idA = markA.GetIdentifier().ID;
                totalDx[idA] = (totalDx.TryGetValue(idA, out var ta) ? ta : 0) + dxA;
                totalDy[idA] = (totalDy.TryGetValue(idA, out var tb) ? tb : 0) + dyA;

                entries[j] = (markB, minXb + dxB, minYb + dyB, maxXb + dxB, maxYb + dyB, axB, ayB);
                var idB = markB.GetIdentifier().ID;
                totalDx[idB] = (totalDx.TryGetValue(idB, out var tc) ? tc : 0) + dxB;
                totalDy[idB] = (totalDy.TryGetValue(idB, out var td) ? td : 0) + dyB;
            }

        }

        // Count remaining mark-mark overlaps
        int remaining = 0;
        for (int i = 0; i < entries.Count; i++)
        for (int j = i + 1; j < entries.Count; j++)
        {
            var (_, minXa, minYa, maxXa, maxYa, _, _) = entries[i];
            var (_, minXb, minYb, maxXb, maxYb, _, _) = entries[j];
            if (Math.Min(maxXa, maxXb) - Math.Max(minXa, minXb) > 0 &&
                Math.Min(maxYa, maxYb) - Math.Max(minYa, minYb) > 0)
                remaining++;
        }

        // Apply: only InsertionPoint (sheet coords); do NOT touch LeaderLinePlacing.StartPoint
        foreach (var (mark, _, _, _, _, _, _) in entries)
        {
            var id = mark.GetIdentifier().ID;
            if (!totalDx.TryGetValue(id, out var dx)) continue;
            if (!totalDy.TryGetValue(id, out var dy)) dy = 0;
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

    // Marks closer to their anchor (the model object they annotate) should move less,
    // preserving short leader lines. Mark farther from anchor does more of the work.
    private static void ComputeSplit(double cxa, double cya, double axA, double ayA,
                                     double cxb, double cyb, double axB, double ayB,
                                     out double ratioA, out double ratioB)
    {
        bool hasA = !double.IsNaN(axA) && !double.IsNaN(ayA);
        bool hasB = !double.IsNaN(axB) && !double.IsNaN(ayB);

        if (!hasA || !hasB) { ratioA = ratioB = 0.5; return; }

        double distA = Math.Sqrt((cxa - axA) * (cxa - axA) + (cya - ayA) * (cya - ayA));
        double distB = Math.Sqrt((cxb - axB) * (cxb - axB) + (cyb - ayB) * (cyb - ayB));

        if (Math.Abs(distA - distB) < 2.0) { ratioA = ratioB = 0.5; return; }

        // The mark farther from anchor is already more displaced — let it do more of the move
        if (distA > distB) { ratioA = 0.7; ratioB = 0.3; }
        else               { ratioA = 0.3; ratioB = 0.7; }
    }

    private static IEnumerable<View> EnumerateViews(Tekla.Structures.Drawing.Drawing drawing)
    {
        var enumerator = drawing.GetSheet().GetViews();
        while (enumerator.MoveNext())
            if (enumerator.Current is View v)
                yield return v;
    }
}
