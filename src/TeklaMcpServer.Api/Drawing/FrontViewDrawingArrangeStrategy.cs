using System.Collections.Generic;
using System.Linq;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;

namespace TeklaMcpServer.Api.Drawing;

public sealed class FrontViewDrawingArrangeStrategy : IDrawingViewArrangeStrategy
{
    public bool CanArrange(DrawingArrangeContext context)
    {
        return context.Views.Any(v => v.ViewType == View.ViewTypes.FrontView);
    }

    public List<ArrangedView> Arrange(DrawingArrangeContext context)
    {
        var arranged = new List<ArrangedView>();

        var front = context.Views.FirstOrDefault(v => v.ViewType == View.ViewTypes.FrontView);
        var top = context.Views.FirstOrDefault(v => v.ViewType == View.ViewTypes.TopView);
        var bottom = context.Views.FirstOrDefault(v => v.ViewType == View.ViewTypes.BottomView);
        var back = context.Views.FirstOrDefault(v => v.ViewType == View.ViewTypes.BackView);
        var sections = context.Views.Where(v => v.ViewType == View.ViewTypes.SectionView).ToList();
        var view3d = context.Views.FirstOrDefault(v => v.ViewType.ToString().Contains("3D") || v.ViewType.ToString().Contains("Iso"));
        var others = context.Views.Where(v => v != front && v != top && v != bottom && v != back
                                           && !sections.Contains(v) && v != view3d).ToList();

        if (front == null)
            return arranged;

        var margin = context.Margin;
        var gap = context.Gap;
        var sheetW = context.SheetWidth;
        var sheetH = context.SheetHeight;

        double leftW = back != null ? back.Width + gap : 0;
        double rightW = sections.Sum(s => s.Width + gap);
        double topH = top != null ? top.Height + gap : 0;
        double bottomH = bottom != null ? bottom.Height + gap : 0;

        double groupW = leftW + front.Width + rightW;
        double groupH = topH + front.Height + bottomH;

        double frontCX = margin + (sheetW - 2 * margin - groupW) / 2 + leftW + front.Width / 2;
        double frontCY = margin + (sheetH - 2 * margin - groupH) / 2 + bottomH + front.Height / 2;

        void Place(View v, double cx, double cy)
        {
            var o = v.Origin;
            o.X = cx;
            o.Y = cy;
            v.Origin = o;
            v.Modify();
            arranged.Add(new ArrangedView { Id = v.GetIdentifier().ID, ViewType = v.ViewType.ToString(), OriginX = cx, OriginY = cy });
        }

        Place(front, frontCX, frontCY);
        if (top != null) Place(top, frontCX, frontCY + front.Height / 2 + gap + top.Height / 2);
        if (bottom != null) Place(bottom, frontCX, frontCY - front.Height / 2 - gap - bottom.Height / 2);
        if (back != null) Place(back, frontCX - front.Width / 2 - gap - back.Width / 2, frontCY);

        double rightX = frontCX + front.Width / 2 + gap;
        foreach (var s in sections)
        {
            Place(s, rightX + s.Width / 2, frontCY);
            rightX += s.Width + gap;
        }

        if (view3d != null)
        {
            double opt1X = rightX + view3d.Width / 2;
            bool opt1Fits = opt1X + view3d.Width / 2 <= sheetW - margin;

            double topEdge = frontCY + front.Height / 2 + gap + (top != null ? top.Height + gap : 0);
            double opt2X = frontCX;
            double opt2Y = topEdge + view3d.Height / 2;
            bool opt2Fits = opt2Y + view3d.Height / 2 <= sheetH - margin
                         && opt2X + view3d.Width / 2 <= sheetW - margin
                         && opt2X - view3d.Width / 2 >= margin;

            double botEdge = frontCY - front.Height / 2 - (bottom != null ? bottom.Height + gap : 0) - gap;
            double opt3X = frontCX;
            double opt3Y = botEdge - view3d.Height / 2;
            bool opt3Fits = opt3Y - view3d.Height / 2 >= margin;

            double finalX;
            double finalY;
            if (opt1Fits) { finalX = opt1X; finalY = frontCY; }
            else if (opt2Fits) { finalX = opt2X; finalY = opt2Y; }
            else if (opt3Fits) { finalX = opt3X; finalY = opt3Y; }
            else { finalX = margin + view3d.Width / 2; finalY = margin + view3d.Height / 2; }

            Place(view3d, finalX, finalY);
        }

        double curX = margin;
        double curY = frontCY - front.Height / 2 - bottomH - gap * 2;
        double rowH = 0;
        foreach (var v in others)
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
