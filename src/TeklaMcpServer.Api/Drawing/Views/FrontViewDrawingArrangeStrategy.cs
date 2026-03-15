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

    public bool EstimateFit(DrawingArrangeContext context, IReadOnlyList<(double w, double h)> frames)
    {
        var availW = context.SheetWidth - 2 * context.Margin;
        var availH = context.SheetHeight - 2 * context.Margin;
        if (availW <= 0 || availH <= 0) return false;

        var front    = context.Views.FirstOrDefault(v => v.ViewType == View.ViewTypes.FrontView);
        var top      = context.Views.FirstOrDefault(v => v.ViewType == View.ViewTypes.TopView);
        var bottom   = context.Views.FirstOrDefault(v => v.ViewType == View.ViewTypes.BottomView);
        var sections = context.Views.Where(v => v.ViewType == View.ViewTypes.SectionView).ToList();

        if (front == null) return false;

        // Landscape sections (w >= h) → left column; portrait sections (h > w) → right column
        var (leftSecs, rightSecs) = ClassifySections(sections);

        double leftColW  = leftSecs.Count  > 0 ? leftSecs.Max(s => s.Width)   + context.Gap : 0;
        double rightColW = rightSecs.Count > 0 ? rightSecs.Max(s => s.Width)  + context.Gap : 0;
        double leftColH  = leftSecs.Count  > 0 ? leftSecs.Sum(s => s.Height)  + (leftSecs.Count  - 1) * context.Gap : 0;
        double rightColH = rightSecs.Count > 0 ? rightSecs.Sum(s => s.Height) + (rightSecs.Count - 1) * context.Gap : 0;
        double topH      = top    != null ? top.Height    + context.Gap : 0;
        double bottomH   = bottom != null ? bottom.Height + context.Gap : 0;

        double backColW = context.Views.FirstOrDefault(v => v.ViewType == View.ViewTypes.BackView) is { } bk ? bk.Width + context.Gap : 0;

        double neededW = backColW + leftColW + front.Width + rightColW;
        double neededH = System.Math.Max(
            System.Math.Max(leftColH, rightColH),
            front.Height + topH + bottomH);

        return neededW <= availW && neededH <= availH;
    }

    private static (List<View> left, List<View> right) ClassifySections(List<View> sections)
    {
        var left  = new List<View>();
        var right = new List<View>();
        foreach (var s in sections)
            (s.Width >= s.Height ? left : right).Add(s);
        return (left, right);
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

        // Classify sections by orientation: landscape → left column, portrait → right column
        var (leftSecs, rightSecs) = ClassifySections(sections);

        double leftSecMaxW  = leftSecs.Count  > 0 ? leftSecs.Max(s => s.Width)   : 0;
        double rightSecMaxW = rightSecs.Count > 0 ? rightSecs.Max(s => s.Width)  : 0;
        double leftSecTotalH  = leftSecs.Count  > 0 ? leftSecs.Sum(s => s.Height)  + (leftSecs.Count  - 1) * gap : 0;
        double rightSecTotalH = rightSecs.Count > 0 ? rightSecs.Sum(s => s.Height) + (rightSecs.Count - 1) * gap : 0;

        double backColW  = back != null ? back.Width + gap : 0;
        double leftColW  = leftSecMaxW  > 0 ? leftSecMaxW  + gap : 0;
        double rightColW = rightSecMaxW > 0 ? rightSecMaxW + gap : 0;
        double topH    = top    != null ? top.Height    + gap : 0;
        double bottomH = bottom != null ? bottom.Height + gap : 0;

        double groupW = backColW + leftColW + front.Width + rightColW;
        double groupH = System.Math.Max(System.Math.Max(leftSecTotalH, rightSecTotalH), topH + front.Height + bottomH);

        double frontCX = margin + (sheetW - 2 * margin - groupW) / 2 + backColW + leftColW + front.Width / 2;
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
        if (top    != null) Place(top,    frontCX, frontCY + front.Height / 2 + gap + top.Height / 2);
        if (bottom != null) Place(bottom, frontCX, frontCY - front.Height / 2 - gap - bottom.Height / 2);
        if (back   != null) Place(back,   frontCX - front.Width / 2 - leftColW - gap - back.Width / 2, frontCY);

        // Left column: landscape sections, centred vertically
        if (leftSecs.Count > 0)
        {
            double lx = frontCX - front.Width / 2 - gap - leftSecMaxW / 2;
            double ly = frontCY + leftSecTotalH / 2;
            foreach (var s in leftSecs) { ly -= s.Height / 2; Place(s, lx, ly); ly -= s.Height / 2 + gap; }
        }

        // Right column: portrait sections, centred vertically
        double rightX = frontCX + front.Width / 2;
        if (rightSecs.Count > 0)
        {
            double rx = rightX + gap + rightSecMaxW / 2;
            double ry = frontCY + rightSecTotalH / 2;
            foreach (var s in rightSecs) { ry -= s.Height / 2; Place(s, rx, ry); ry -= s.Height / 2 + gap; }
            rightX += rightColW;
        }

        if (view3d != null)
        {
            double opt1X = rightX + gap + view3d.Width / 2;
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
