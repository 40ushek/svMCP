using System;
using Tekla.Structures.Drawing;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;
using TeklaMcpServer.Api.Drawing;

namespace TeklaMcpServer.Host;

internal static class MarkBoxDrawer
{
    private static Model _model = new();
    public static void DrawBoundingBox(Mark mark, Drawing activeDrawing)
    {
        var view = mark.GetView() as View;
        if (view == null) return;

        var objs = mark.GetRelatedObjects();
        objs.MoveNext();

        var ob = mark.GetObjectAlignedBoundingBox();
        var ab = mark.GetAxisAlignedBoundingBox();


        //var enumerator = mark.Attributes.Content.GetEnumerator();
        //while (enumerator.MoveNext())
        //{
        //    var c = enumerator.Current;
        //}

        var rect1 = new Rectangle(view, ob.MinPoint, ob.MaxPoint);
        rect1.Attributes.Line.Color = DrawingColors.Magenta;
        rect1.Insert();

        var rect2 = new Rectangle(view, ab.MinPoint, ab.MaxPoint);
        rect2.Attributes.Line.Color = DrawingColors.Black;
        rect2.Insert();

        activeDrawing.CommitChanges();

    }
}
