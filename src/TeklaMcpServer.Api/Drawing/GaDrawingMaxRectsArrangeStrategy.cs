using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using TeklaMcpServer.Api.Algorithms.Packing;

namespace TeklaMcpServer.Api.Drawing;

public sealed class GaDrawingMaxRectsArrangeStrategy : IDrawingViewArrangeStrategy
{
    private readonly ShelfPackingDrawingArrangeStrategy _fallback = new();

    public bool CanArrange(DrawingArrangeContext context)
    {
        return context.Drawing is GADrawing;
    }

    public bool EstimateFit(IReadOnlyList<(double w, double h)> frames, double availableWidth, double availableHeight, double gap)
    {
        return DrawingPackingEstimator.FitsMaxRects(frames, availableWidth, availableHeight, gap, MaxRectsHeuristic.BestAreaFit);
    }

    public List<ArrangedView> Arrange(DrawingArrangeContext context)
    {
        var availableW = context.SheetWidth - 2 * context.Margin;
        var availableH = context.SheetHeight - 2 * context.Margin;
        if (availableW <= 0 || availableH <= 0)
            return _fallback.Arrange(context);

        var orderedViews = context.Views
            .OrderByDescending(v => v.Width * v.Height)
            .ToList();

        // Inflate with gap and expand bin by the same amount so sheet edges keep full usable span.
        var packer = new MaxRectsBinPacker(availableW + context.Gap, availableH + context.Gap, allowRotation: false);
        var packed = new Dictionary<View, PackedRectangle>();

        foreach (var view in orderedViews)
        {
            if (!packer.TryInsert(view.Width + context.Gap, view.Height + context.Gap, MaxRectsHeuristic.BestAreaFit, out var placement))
                return _fallback.Arrange(context);

            packed[view] = placement;
        }

        var arranged = new List<ArrangedView>(orderedViews.Count);
        foreach (var view in orderedViews)
        {
            var rect = packed[view];
            var origin = view.Origin;
            origin.X = context.Margin + rect.X + view.Width / 2.0;
            origin.Y = context.SheetHeight - context.Margin - rect.Y - view.Height / 2.0;
            view.Origin = origin;
            view.Modify();

            arranged.Add(new ArrangedView
            {
                Id = view.GetIdentifier().ID,
                ViewType = view.ViewType.ToString(),
                OriginX = origin.X,
                OriginY = origin.Y
            });
        }

        return arranged;
    }
}
