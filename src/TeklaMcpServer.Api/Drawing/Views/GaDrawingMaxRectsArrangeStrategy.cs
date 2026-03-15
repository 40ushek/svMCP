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
        => context.ReservedAreas.Count > 0 || context.Drawing is GADrawing;

    public bool EstimateFit(DrawingArrangeContext context, IReadOnlyList<(double w, double h)> frames)
    {
        var availableW = context.SheetWidth - 2 * context.Margin;
        var availableH = context.SheetHeight - 2 * context.Margin;
        if (availableW <= 0 || availableH <= 0)
            return false;

        var packer = CreatePacker(context, availableW, availableH);
        foreach (var frame in frames.OrderByDescending(f => f.w * f.h))
        {
            if (!packer.TryInsert(frame.w + context.Gap, frame.h + context.Gap, MaxRectsHeuristic.BestAreaFit, out _))
                return false;
        }

        return true;
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
        var packer = CreatePacker(context, availableW, availableH);
        var packed = new Dictionary<View, PackedRectangle>();

        foreach (var view in orderedViews)
        {
            if (!packer.TryInsert(view.Width + context.Gap, view.Height + context.Gap, MaxRectsHeuristic.BestAreaFit, out var placement))
            {
                if (context.ReservedAreas.Count == 0)
                    return _fallback.Arrange(context);

                throw new System.InvalidOperationException("Could not arrange views within the available sheet area after applying reserved zones.");
            }

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

    private static MaxRectsBinPacker CreatePacker(DrawingArrangeContext context, double availableW, double availableH)
    {
        return new MaxRectsBinPacker(
            availableW + context.Gap,
            availableH + context.Gap,
            allowRotation: false,
            blockedRectangles: ToBlockedRectangles(context));
    }

    private static IEnumerable<PackedRectangle> ToBlockedRectangles(DrawingArrangeContext context)
    {
        foreach (var area in context.ReservedAreas)
        {
            var minX = System.Math.Max(context.Margin, area.MinX - context.Gap);
            var maxX = System.Math.Min(context.SheetWidth - context.Margin, area.MaxX + context.Gap);
            var minY = System.Math.Max(context.Margin, area.MinY - context.Gap);
            var maxY = System.Math.Min(context.SheetHeight - context.Margin, area.MaxY + context.Gap);

            if (maxX <= minX || maxY <= minY)
                continue;

            yield return new PackedRectangle(
                minX - context.Margin,
                (context.SheetHeight - context.Margin) - maxY,
                maxX - minX,
                maxY - minY);
        }
    }
}
