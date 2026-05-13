using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Geometry3d;
using TeklaMcpServer.Api.Algorithms.Packing;
using TeklaMcpServer.Api.Drawing;

namespace TeklaMcpServer.Api.Drawing.ViewLayout;

public sealed class GaDrawingMaxRectsArrangeStrategy : IDrawingViewArrangeStrategy, IDrawingViewArrangeDiagnosticsStrategy
{
    private readonly ShelfPackingDrawingArrangeStrategy _fallback = new();

    public bool CanArrange(DrawingArrangeContext context)
        => context.Drawing is GADrawing;

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
            .OrderByDescending(v => DrawingArrangeContextSizing.GetWidth(context, v) * DrawingArrangeContextSizing.GetHeight(context, v))
            .ToList();

        // Inflate with gap and expand bin by the same amount so sheet edges keep full usable span.
        var packer = CreatePacker(context, availableW, availableH);
        var packed = new Dictionary<View, PackedRectangle>();

        foreach (var view in orderedViews)
        {
            var width = DrawingArrangeContextSizing.GetWidth(context, view);
            var height = DrawingArrangeContextSizing.GetHeight(context, view);
            if (!packer.TryInsert(width + context.Gap, height + context.Gap, MaxRectsHeuristic.BestAreaFit, out var placement))
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
            var width = DrawingArrangeContextSizing.GetWidth(context, view);
            var height = DrawingArrangeContextSizing.GetHeight(context, view);
            var currentOrigin = view.Origin;
            var origin = new Point(currentOrigin?.X ?? 0, currentOrigin?.Y ?? 0, currentOrigin?.Z ?? 0);
            origin.X = context.Margin + rect.X + width / 2.0;
            origin.Y = context.SheetHeight - context.Margin - rect.Y - height / 2.0;
            if (context.ApplyChanges)
            {
                view.Origin = origin;
                view.Modify();
            }

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

    public List<DrawingFitConflict> DiagnoseFitConflicts(DrawingArrangeContext context, IReadOnlyList<(double w, double h)> frames)
    {
        var conflicts = new List<DrawingFitConflict>();
        var availableW = context.SheetWidth - 2 * context.Margin;
        var availableH = context.SheetHeight - 2 * context.Margin;
        if (availableW <= 0 || availableH <= 0)
            return conflicts;

        var orderedViews = context.Views
            .OrderByDescending(v => DrawingArrangeContextSizing.GetWidth(context, v) * DrawingArrangeContextSizing.GetHeight(context, v))
            .ToList();
        var packer = CreatePacker(context, availableW, availableH);

        foreach (var view in orderedViews)
        {
            var width = DrawingArrangeContextSizing.GetWidth(context, view);
            var height = DrawingArrangeContextSizing.GetHeight(context, view);
            if (!packer.TryInsert(width + context.Gap, height + context.Gap, MaxRectsHeuristic.BestAreaFit, out _))
            {
                conflicts.Add(new DrawingFitConflict
                {
                    ViewId = view.GetIdentifier().ID,
                    ViewType = view.ViewType.ToString(),
                    AttemptedZone = "Residual",
                    Conflicts = new List<DrawingFitConflictItem>
                    {
                        new() { Type = "no_residual_space", Target = "maxrects" }
                    }
                });
                break;
            }
        }

        return conflicts;
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

