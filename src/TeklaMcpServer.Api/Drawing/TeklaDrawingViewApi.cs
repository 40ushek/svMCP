using System.Collections.Generic;
using System.Linq;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Model;

namespace TeklaMcpServer.Api.Drawing;

public sealed class TeklaDrawingViewApi : IDrawingViewApi
{
    private readonly Model _model;

    public TeklaDrawingViewApi(Model model) => _model = model;

    // ── GetViews ──────────────────────────────────────────────────────────

    public DrawingViewsResult GetViews()
    {
        var activeDrawing = new DrawingHandler().GetActiveDrawing();
        if (activeDrawing == null)
            throw new DrawingNotOpenException();

        double sheetW = 0, sheetH = 0;
        try { var ss = activeDrawing.Layout.SheetSize; sheetW = ss.Width; sheetH = ss.Height; } catch { }

        var result = new DrawingViewsResult { SheetWidth = sheetW, SheetHeight = sheetH };
        foreach (var v in EnumerateViews(activeDrawing))
            result.Views.Add(ToInfo(v));

        return result;
    }

    // ── MoveView ──────────────────────────────────────────────────────────

    public MoveViewResult MoveView(int viewId, double dx, double dy, bool absolute)
    {
        var activeDrawing = new DrawingHandler().GetActiveDrawing();
        if (activeDrawing == null)
            throw new DrawingNotOpenException();

        var view = EnumerateViews(activeDrawing).FirstOrDefault(v => v.GetIdentifier().ID == viewId)
            ?? throw new ViewNotFoundException(viewId);

        var oldX = view.Origin.X;
        var oldY = view.Origin.Y;
        var origin = view.Origin;

        if (absolute) { origin.X = dx;      origin.Y = dy;      }
        else          { origin.X += dx;     origin.Y += dy;     }

        view.Origin = origin;
        view.Modify();
        activeDrawing.CommitChanges();

        return new MoveViewResult
        {
            Moved      = true,
            ViewId     = viewId,
            OldOriginX = oldX,
            OldOriginY = oldY,
            NewOriginX = origin.X,
            NewOriginY = origin.Y
        };
    }

    // ── SetViewScale ──────────────────────────────────────────────────────

    public SetViewScaleResult SetViewScale(IEnumerable<int> viewIds, double scale)
    {
        var activeDrawing = new DrawingHandler().GetActiveDrawing();
        if (activeDrawing == null)
            throw new DrawingNotOpenException();

        var targetIds = new HashSet<int>(viewIds);
        var updated   = new List<int>();

        foreach (var v in EnumerateViews(activeDrawing))
        {
            var id = v.GetIdentifier().ID;
            if (!targetIds.Contains(id)) continue;
            v.Attributes.Scale = scale;
            v.Modify();
            updated.Add(id);
        }

        if (updated.Count > 0)
            activeDrawing.CommitChanges();

        return new SetViewScaleResult { UpdatedCount = updated.Count, UpdatedIds = updated, Scale = scale };
    }

    // ── FitViewsToSheet ───────────────────────────────────────────────────

    public FitViewsResult FitViewsToSheet(double margin, double gap, double titleBlockHeight)
    {
        var activeDrawing = new DrawingHandler().GetActiveDrawing();
        if (activeDrawing == null)
            throw new DrawingNotOpenException();

        var views = EnumerateViews(activeDrawing).ToList();
        if (views.Count == 0)
            throw new System.InvalidOperationException("No views found in active drawing.");

        double sheetW = 0, sheetH = 0;
        try { var ss = activeDrawing.Layout.SheetSize; sheetW = ss.Width; sheetH = ss.Height; } catch { }

        double availW = sheetW - 2 * margin;
        double availH = sheetH - 2 * margin - titleBlockHeight;

        // Find optimal scale using current sizes scaled by ratio
        double currentScale = views[0].Attributes.Scale;
        var standardScales  = new[] { 1.0, 2, 5, 10, 15, 20, 25, 50, 100, 200, 250, 500, 1000 };
        var optimalScale    = standardScales.Last();
        foreach (var s in standardScales)
        {
            double ratio = currentScale / s;
            var frames   = views.Select(v => (w: v.Width * ratio, h: v.Height * ratio)).ToList();
            if (FitsShelfPacking(frames, availW, availH, gap)) { optimalScale = s; break; }
        }

        // Pass 1: apply scale
        foreach (var v in views) { v.Attributes.Scale = optimalScale; v.Modify(); }
        activeDrawing.CommitChanges();

        // Pass 2: re-read actual sizes and arrange
        var viewsAfterScale = EnumerateViews(activeDrawing).ToList();
        var arranged        = ArrangeViews(viewsAfterScale, sheetW, sheetH, margin, gap);
        activeDrawing.CommitChanges();

        return new FitViewsResult
        {
            OptimalScale = optimalScale,
            SheetWidth   = sheetW,
            SheetHeight  = sheetH,
            Arranged     = arranged.Count,
            Views        = arranged
        };
    }

    // ── Layout helpers ────────────────────────────────────────────────────

    private static List<ArrangedView> ArrangeViews(List<View> views, double sheetW, double sheetH, double margin, double gap)
    {
        var arranged = new List<ArrangedView>();

        var front    = views.FirstOrDefault(v => v.ViewType == View.ViewTypes.FrontView);
        var top      = views.FirstOrDefault(v => v.ViewType == View.ViewTypes.TopView);
        var bottom   = views.FirstOrDefault(v => v.ViewType == View.ViewTypes.BottomView);
        var back     = views.FirstOrDefault(v => v.ViewType == View.ViewTypes.BackView);
        var sections = views.Where(v => v.ViewType == View.ViewTypes.SectionView).ToList();
        var view3d   = views.FirstOrDefault(v => v.ViewType.ToString().Contains("3D") || v.ViewType.ToString().Contains("Iso"));
        var others   = views.Where(v => v != front && v != top && v != bottom && v != back
                                     && !sections.Contains(v) && v != view3d).ToList();

        if (front != null)
        {
            double leftW   = back    != null ? back.Width   + gap : 0;
            double rightW  = sections.Sum(s => s.Width + gap);
            double topH    = top     != null ? top.Height   + gap : 0;
            double bottomH = bottom  != null ? bottom.Height + gap : 0;

            double groupW  = leftW + front.Width + rightW;
            double groupH  = topH  + front.Height + bottomH;

            double frontCX = margin + (sheetW - 2 * margin - groupW) / 2 + leftW + front.Width / 2;
            double frontCY = margin + (sheetH - 2 * margin - groupH) / 2 + bottomH + front.Height / 2;

            void Place(View v, double cx, double cy)
            {
                var o = v.Origin; o.X = cx; o.Y = cy; v.Origin = o; v.Modify();
                arranged.Add(new ArrangedView { Id = v.GetIdentifier().ID, ViewType = v.ViewType.ToString(), OriginX = cx, OriginY = cy });
            }

            Place(front, frontCX, frontCY);
            if (top    != null) Place(top,    frontCX, frontCY + front.Height / 2 + gap + top.Height    / 2);
            if (bottom != null) Place(bottom, frontCX, frontCY - front.Height / 2 - gap - bottom.Height / 2);
            if (back   != null) Place(back,   frontCX - front.Width / 2 - gap - back.Width / 2, frontCY);

            double rightX = frontCX + front.Width / 2 + gap;
            foreach (var s in sections) { Place(s, rightX + s.Width / 2, frontCY); rightX += s.Width + gap; }

            if (view3d != null)
            {
                double opt1X = rightX + view3d.Width / 2;
                bool opt1Fits = opt1X + view3d.Width / 2 <= sheetW - margin;

                double topEdge = frontCY + front.Height / 2 + gap + (top != null ? top.Height + gap : 0);
                double opt2X = frontCX, opt2Y = topEdge + view3d.Height / 2;
                bool opt2Fits = opt2Y + view3d.Height / 2 <= sheetH - margin
                             && opt2X + view3d.Width  / 2 <= sheetW - margin
                             && opt2X - view3d.Width  / 2 >= margin;

                double botEdge = frontCY - front.Height / 2 - (bottom != null ? bottom.Height + gap : 0) - gap;
                double opt3X = frontCX, opt3Y = botEdge - view3d.Height / 2;
                bool opt3Fits = opt3Y - view3d.Height / 2 >= margin;

                double finalX, finalY;
                if (opt1Fits)      { finalX = opt1X; finalY = frontCY; }
                else if (opt2Fits) { finalX = opt2X; finalY = opt2Y;  }
                else if (opt3Fits) { finalX = opt3X; finalY = opt3Y;  }
                else               { finalX = margin + view3d.Width / 2; finalY = margin + view3d.Height / 2; }

                Place(view3d, finalX, finalY);
            }

            double curX = margin, curY = frontCY - front.Height / 2 - bottomH - gap * 2, rowH = 0;
            foreach (var v in others)
            {
                if (curX + v.Width > sheetW - margin && curX > margin) { curX = margin; curY -= rowH + gap; rowH = 0; }
                var o = v.Origin; o.X = curX + v.Width / 2; o.Y = curY - v.Height / 2; v.Origin = o; v.Modify();
                arranged.Add(new ArrangedView { Id = v.GetIdentifier().ID, ViewType = v.ViewType.ToString(), OriginX = o.X, OriginY = o.Y });
                curX += v.Width + gap;
                if (v.Height > rowH) rowH = v.Height;
            }
        }
        else
        {
            // Fallback: shelf packing (GA drawings, etc.)
            double curX = margin, curY = sheetH - margin, rowH = 0;
            foreach (var v in views.OrderByDescending(v => v.Height))
            {
                if (curX + v.Width > sheetW - margin && curX > margin) { curX = margin; curY -= rowH + gap; rowH = 0; }
                var o = v.Origin; o.X = curX + v.Width / 2; o.Y = curY - v.Height / 2; v.Origin = o; v.Modify();
                arranged.Add(new ArrangedView { Id = v.GetIdentifier().ID, ViewType = v.ViewType.ToString(), OriginX = o.X, OriginY = o.Y });
                curX += v.Width + gap;
                if (v.Height > rowH) rowH = v.Height;
            }
        }

        return arranged;
    }

    private static bool FitsShelfPacking(List<(double w, double h)> frames, double availW, double availH, double gap)
    {
        double curX = 0, curY = availH, rowH = 0;
        foreach (var (w, h) in frames.OrderByDescending(f => f.h))
        {
            if (curX + w > availW && curX > 0) { curX = 0; curY -= rowH + gap; rowH = 0; }
            if (w > availW || curY - h < 0) return false;
            curX += w + gap;
            if (h > rowH) rowH = h;
        }
        return true;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static IEnumerable<View> EnumerateViews(Tekla.Structures.Drawing.Drawing drawing)
    {
        var enumerator = drawing.GetSheet().GetViews();
        while (enumerator.MoveNext())
            if (enumerator.Current is View v)
                yield return v;
    }

    private static DrawingViewInfo ToInfo(View v) => new()
    {
        Id       = v.GetIdentifier().ID,
        ViewType = v.ViewType.ToString(),
        Name     = v.Name ?? string.Empty,
        OriginX  = v.Origin?.X ?? 0,
        OriginY  = v.Origin?.Y ?? 0,
        Scale    = v.Attributes.Scale,
        Width    = v.Width,
        Height   = v.Height
    };
}

public sealed class DrawingNotOpenException : System.Exception
{
    public DrawingNotOpenException() : base("No drawing is currently open.") { }
}

public sealed class ViewNotFoundException : System.Exception
{
    public ViewNotFoundException(int id) : base($"View with ID {id} not found in active drawing.") { }
}
