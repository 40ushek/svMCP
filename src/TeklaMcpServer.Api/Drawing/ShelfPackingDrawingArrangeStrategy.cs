using System.Collections.Generic;
using System.Linq;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;

namespace TeklaMcpServer.Api.Drawing;

public sealed class ShelfPackingDrawingArrangeStrategy : IDrawingViewArrangeStrategy
{
    public bool CanArrange(DrawingArrangeContext context) => true;

    public bool EstimateFit(IReadOnlyList<(double w, double h)> frames, double availableWidth, double availableHeight, double gap)
    {
        return DrawingPackingEstimator.FitsShelfPacking(frames, availableWidth, availableHeight, gap);
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

        foreach (var v in context.Views.OrderByDescending(v => v.Height))
        {
            if (curX + v.Width > sheetW - margin && curX > margin)
            {
                curX = margin;
                curY -= rowH + gap;
                rowH = 0;
            }

            var o = v.Origin;
            o.X = curX + v.Width / 2;
            o.Y = curY - v.Height / 2;
            v.Origin = o;
            v.Modify();
            arranged.Add(new ArrangedView { Id = v.GetIdentifier().ID, ViewType = v.ViewType.ToString(), OriginX = o.X, OriginY = o.Y });
            curX += v.Width + gap;
            if (v.Height > rowH) rowH = v.Height;
        }

        return arranged;
    }
}
