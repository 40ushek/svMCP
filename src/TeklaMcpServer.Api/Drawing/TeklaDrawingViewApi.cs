using System.Collections.Generic;
using System.Linq;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
namespace TeklaMcpServer.Api.Drawing;

public sealed class TeklaDrawingViewApi : IDrawingViewApi
{
    private readonly DrawingViewArrangementSelector _arrangementSelector;

    public TeklaDrawingViewApi(DrawingViewArrangementSelector? arrangementSelector = null)
    {
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

        var viewIds = views.Select(v => v.GetIdentifier().ID).ToHashSet();
        IReadOnlyList<ReservedRect> reservedAreas = DrawingReservedAreaReader.Read(
            activeDrawing,
            margin,
            titleBlockHeight,
            viewIds);
        double availW = sheetW - 2 * margin;
        double availH = sheetH - 2 * margin;
        if (availW <= 0 || availH <= 0)
            throw new System.InvalidOperationException("No drawable area left after applying margin.");

        // Rough lower bound for the scale denominator, ignoring Tekla's fixed view-frame border
        // (~20 mm regardless of scale). Filters out candidates that are obviously too large
        // before making any Tekla API calls.
        double currentScale = views.Select(v => v.Attributes.Scale).FirstOrDefault(s => s > 0);
        if (currentScale <= 0) currentScale = 1.0;

        const double borderEst = 20.0;
        double maxModelW = views.Max(v => v.Width  * currentScale);
        double maxModelH = views.Max(v => v.Height * currentScale);
        double minDenom  = System.Math.Max(
            maxModelW / System.Math.Max(availW - borderEst, 1),
            maxModelH / System.Math.Max(availH - borderEst, 1));

        var standardScales = new[] { 1.0, 2, 5, 10, 15, 20, 25, 50, 75, 100, 125, 150, 175, 200, 250, 500, 1000 };
        var candidates     = System.Array.FindAll(standardScales, s => s >= minDenom);
        if (candidates.Length == 0) candidates = new[] { standardScales[standardScales.Length - 1] };

        // Save original scales so we can restore them if no candidate fits.
        var originalScales = views.ToDictionary(v => v.GetIdentifier().ID, v => v.Attributes.Scale);

        // Apply each candidate scale, then read the ACTUAL Tekla-reported view dimensions and
        // check fit. Estimating via ratio is unreliable: Tekla's view frame has a fixed border
        // that does not scale with content, so the simple linear estimate can be 5-10 mm off.
        double? optimalScale = null;
        var currentViews    = views;

        foreach (var s in candidates)
        {
            foreach (var v in currentViews) { v.Attributes.Scale = s; v.Modify(); }
            activeDrawing.CommitChanges();

            var reread       = EnumerateViews(activeDrawing).ToList();
            var actualFrames = reread.Select(v => (w: v.Width, h: v.Height)).ToList();
            var ctx          = new DrawingArrangeContext(activeDrawing, reread, sheetW, sheetH, margin, gap, reservedAreas);

            if (_arrangementSelector.EstimateFit(ctx, actualFrames))
            {
                optimalScale = s;
                currentViews = reread;
                break;
            }

            currentViews = reread;
        }

        if (!optimalScale.HasValue)
        {
            // Restore original scales before throwing
            foreach (var v in EnumerateViews(activeDrawing))
            {
                if (originalScales.TryGetValue(v.GetIdentifier().ID, out var orig))
                { v.Attributes.Scale = orig; v.Modify(); }
            }
            activeDrawing.CommitChanges();
            throw new System.InvalidOperationException("Could not fit views on sheet with available standard scales.");
        }

        // ── Compute frame-to-origin offsets (two-scale probe) ─────────────────
        // view.Origin in Tekla is the projection of the view's local (0,0) onto the sheet —
        // NOT the center of the view frame.  Frame center = Origin + frameOffset / scale.
        // When scale changes Tekla adjusts Origin to keep the frame center fixed, so two
        // readings at different scales let us solve for frameOffset analytically.
        var originsAtOptimal = currentViews
            .Select(v => (X: v.Origin?.X ?? 0, Y: v.Origin?.Y ?? 0))
            .ToList();

        var frameOffsetXs = new double[currentViews.Count];
        var frameOffsetYs = new double[currentViews.Count];
        bool offsetsComputed = false;

        double probeScale = standardScales.FirstOrDefault(s => System.Math.Abs(s - optimalScale.Value) > 0.5);
        if (probeScale > 0)
        {
            foreach (var v in currentViews) { v.Attributes.Scale = probeScale; v.Modify(); }
            activeDrawing.CommitChanges();
            var probeViews = EnumerateViews(activeDrawing).ToList();

            double denom = 1.0 / optimalScale.Value - 1.0 / probeScale;
            for (int i = 0; i < currentViews.Count && i < probeViews.Count; i++)
            {
                double dX = (probeViews[i].Origin?.X ?? 0) - originsAtOptimal[i].X;
                double dY = (probeViews[i].Origin?.Y ?? 0) - originsAtOptimal[i].Y;
                frameOffsetXs[i] = dX / denom;
                frameOffsetYs[i] = dY / denom;
            }

            // Restore optimal scale
            foreach (var v in probeViews) { v.Attributes.Scale = optimalScale.Value; v.Modify(); }
            activeDrawing.CommitChanges();
            currentViews = EnumerateViews(activeDrawing).ToList();
            offsetsComputed = true;
        }

        // ── Arrange (naive: assumes Origin = frame center) ────────────────────
        var arranged = _arrangementSelector.Arrange(
            new DrawingArrangeContext(activeDrawing, currentViews, sheetW, sheetH, margin, gap, reservedAreas));

        // ── Post-correction: shift each origin so the frame lands at the intended position ─
        // arranged[i].OriginX/Y holds the desired frame-center position on the sheet.
        // Correct: actual_origin = desired_frame_center - frameOffset / scale
        // Use ID-based lookup because Arrange may reorder views relative to currentViews.
        if (offsetsComputed)
        {
            var viewById   = currentViews.ToDictionary(v => v.GetIdentifier().ID);
            var offsetById = new System.Collections.Generic.Dictionary<int, (double X, double Y)>();
            for (int i = 0; i < currentViews.Count; i++)
                offsetById[currentViews[i].GetIdentifier().ID] = (frameOffsetXs[i], frameOffsetYs[i]);

            for (int i = 0; i < arranged.Count; i++)
            {
                if (!viewById.TryGetValue(arranged[i].Id, out var v)) continue;
                if (!offsetById.TryGetValue(arranged[i].Id, out var off)) continue;
                var o = v.Origin;
                o.X = arranged[i].OriginX - off.X / optimalScale.Value;
                o.Y = arranged[i].OriginY - off.Y / optimalScale.Value;
                v.Origin = o;
                v.Modify();
                arranged[i] = new ArrangedView
                {
                    Id       = arranged[i].Id,
                    ViewType = arranged[i].ViewType,
                    OriginX  = o.X,
                    OriginY  = o.Y
                };
            }
        }

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

    // ── PlaceViews ────────────────────────────────────────────────────────

    public bool PlaceViews()
    {
        var activeDrawing = new DrawingHandler().GetActiveDrawing();
        if (activeDrawing == null)
            throw new DrawingNotOpenException();

        int iterations = 0;
        while (iterations < 10 && activeDrawing.PlaceViews())
            iterations++;

        if (iterations > 0)
            activeDrawing.CommitChanges();

        return iterations > 0;
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
