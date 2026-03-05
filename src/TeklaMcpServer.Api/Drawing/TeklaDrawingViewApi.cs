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
    private readonly DrawingViewArrangementSelector _arrangementSelector;

    public TeklaDrawingViewApi(Model model, DrawingViewArrangementSelector? arrangementSelector = null)
    {
        _model = model;
        _arrangementSelector = arrangementSelector ?? DrawingViewArrangementSelector.CreateDefault();
    }

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
        if (margin < 0)
            throw new System.ArgumentOutOfRangeException(nameof(margin), "margin must be >= 0.");
        if (gap < 0)
            throw new System.ArgumentOutOfRangeException(nameof(gap), "gap must be >= 0.");
        if (titleBlockHeight < 0)
            throw new System.ArgumentOutOfRangeException(nameof(titleBlockHeight), "titleBlockHeight must be >= 0.");

        var activeDrawing = new DrawingHandler().GetActiveDrawing();
        if (activeDrawing == null)
            throw new DrawingNotOpenException();

        var views = EnumerateViews(activeDrawing).ToList();
        if (views.Count == 0)
            throw new System.InvalidOperationException("No views found in active drawing.");

        double sheetW = 0, sheetH = 0;
        try { var ss = activeDrawing.Layout.SheetSize; sheetW = ss.Width; sheetH = ss.Height; } catch { }
        if (sheetW <= 0 || sheetH <= 0)
            throw new System.InvalidOperationException("Unable to read drawing sheet size.");

        double availW = sheetW - 2 * margin;
        double availH = sheetH - 2 * margin - titleBlockHeight;
        if (availW <= 0 || availH <= 0)
            throw new System.InvalidOperationException("No drawable area left after applying margin/titleBlockHeight.");

        // Find optimal scale using current sizes scaled by ratio
        double currentScale = views.Select(v => v.Attributes.Scale).FirstOrDefault(s => s > 0);
        if (currentScale <= 0)
            currentScale = 1.0;

        var standardScales  = new[] { 1.0, 2, 5, 10, 15, 20, 25, 50, 100, 200, 250, 500, 1000 };
        double? optimalScale = null;
        foreach (var s in standardScales)
        {
            double ratio = currentScale / s;
            var frames   = views.Select(v => (w: v.Width * ratio, h: v.Height * ratio)).ToList();
            if (FitsShelfPacking(frames, availW, availH, gap))
            {
                optimalScale = s;
                break;
            }
        }

        if (!optimalScale.HasValue)
            throw new System.InvalidOperationException("Could not fit views on sheet with available standard scales.");

        // Pass 1: apply scale
        foreach (var v in views) { v.Attributes.Scale = optimalScale.Value; v.Modify(); }
        activeDrawing.CommitChanges();

        // Pass 2: re-read actual sizes and arrange
        var viewsAfterScale = EnumerateViews(activeDrawing).ToList();
        var arranged        = _arrangementSelector.Arrange(
            new DrawingArrangeContext(activeDrawing, viewsAfterScale, sheetW, sheetH, margin, gap));
        activeDrawing.CommitChanges();

        return new FitViewsResult
        {
            OptimalScale = optimalScale.Value,
            SheetWidth   = sheetW,
            SheetHeight  = sheetH,
            Arranged     = arranged.Count,
            Views        = arranged
        };
    }

    private static bool FitsShelfPacking(List<(double w, double h)> frames, double availW, double availH, double gap)
    {
        if (availW <= 0 || availH <= 0)
            return false;

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
