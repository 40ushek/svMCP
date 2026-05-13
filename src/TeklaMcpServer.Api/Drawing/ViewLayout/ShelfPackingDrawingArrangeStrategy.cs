using System.Collections.Generic;
using System.Linq;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Geometry3d;
using TeklaMcpServer.Api.Drawing;

namespace TeklaMcpServer.Api.Drawing.ViewLayout;

public sealed class ShelfPackingDrawingArrangeStrategy : IDrawingViewArrangeStrategy, IDrawingViewArrangeDiagnosticsStrategy
{
    public bool CanArrange(DrawingArrangeContext context) => true;

    public bool EstimateFit(DrawingArrangeContext context, IReadOnlyList<(double w, double h)> frames)
    {
        var availableWidth = context.SheetWidth - 2 * context.Margin;
        var availableHeight = context.SheetHeight - 2 * context.Margin;
        return DrawingPackingEstimator.FitsShelfPacking(frames, availableWidth, availableHeight, context.Gap);
    }

    public List<ArrangedView> Arrange(DrawingArrangeContext context)
    {
        var arranged = new List<ArrangedView>();
        var margin = context.Margin;
        var gap = context.Gap;
        var sheetW = context.SheetWidth;
        var sheetH = context.SheetHeight;

        double curX = margin;
        double curY = sheetH - margin;
        double rowH = 0;

        foreach (var v in context.Views.OrderByDescending(v => DrawingArrangeContextSizing.GetHeight(context, v)))
        {
            var width = DrawingArrangeContextSizing.GetWidth(context, v);
            var height = DrawingArrangeContextSizing.GetHeight(context, v);

            if (curX + width > sheetW - margin && curX > margin)
            {
                curX = margin;
                curY -= rowH + gap;
                rowH = 0;
            }

            var currentOrigin = v.Origin;
            var o = new Point(currentOrigin?.X ?? 0, currentOrigin?.Y ?? 0, currentOrigin?.Z ?? 0);
            o.X = curX + width / 2;
            o.Y = curY - height / 2;
            if (context.ApplyChanges)
            {
                v.Origin = o;
                v.Modify();
            }
            arranged.Add(new ArrangedView { Id = v.GetIdentifier().ID, ViewType = v.ViewType.ToString(), OriginX = o.X, OriginY = o.Y });
            curX += width + gap;
            if (height > rowH) rowH = height;
        }

        return arranged;
    }

    public List<DrawingFitConflict> DiagnoseFitConflicts(DrawingArrangeContext context, IReadOnlyList<(double w, double h)> frames)
    {
        var conflicts = new List<DrawingFitConflict>();
        var margin = context.Margin;
        var gap = context.Gap;
        var sheetW = context.SheetWidth;
        var sheetH = context.SheetHeight;

        double curX = margin;
        double curY = sheetH - margin;
        double rowH = 0;

        foreach (var view in context.Views.OrderByDescending(v => v.Height))
        {
            var width = DrawingArrangeContextSizing.GetWidth(context, view);
            var height = DrawingArrangeContextSizing.GetHeight(context, view);

            if (curX + width > sheetW - margin && curX > margin)
            {
                curX = margin;
                curY -= rowH + gap;
                rowH = 0;
            }

            if (curX + width > sheetW - margin || curY - height < margin)
            {
                conflicts.Add(new DrawingFitConflict
                {
                    ViewId = view.GetIdentifier().ID,
                    ViewType = view.ViewType.ToString(),
                    AttemptedZone = "Shelf",
                    Conflicts = new List<DrawingFitConflictItem>
                    {
                        new() { Type = "outside_sheet_bounds", Target = "shelf_packing" }
                    }
                });
            }

            curX += width + gap;
            if (height > rowH) rowH = height;
        }

        return conflicts;
    }
}

