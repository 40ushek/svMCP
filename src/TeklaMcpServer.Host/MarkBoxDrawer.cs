using System;
using Tekla.Structures.Drawing;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;
using TeklaMcpServer.Api.Drawing;

namespace TeklaMcpServer.Host;

internal static class MarkBoxDrawer
{
    private static Model _model = new();
    public static void DrawBoundingBox(Mark mark, Tekla.Structures.Drawing.Drawing activeDrawing)
    {
        var view = mark.GetView() as View;
        if (view == null) return;

        var objs = mark.GetRelatedObjects();
        objs.MoveNext();

        Tekla.Structures.Model.Part? modelPart = null;
        if (objs.Current is not Tekla.Structures.Drawing.ModelObject drawingModelObj) return;

        var modelObject = _model.SelectModelObject(drawingModelObj.ModelIdentifier);
        modelPart = modelObject as Tekla.Structures.Model.Part;
        var cs = modelPart.GetCoordinateSystem();
        var matrix = MatrixFactory.ByCoordinateSystems(view.ViewCoordinateSystem, cs);

        var box = mark.GetObjectAlignedBoundingBox();


        var enumerator = mark.Attributes.Content.GetEnumerator();
        while (enumerator.MoveNext()) 
        {
            var c = enumerator.Current;

        }
 


        var a = matrix.Transform(box.MinPoint);
        var b = matrix.Transform(box.MaxPoint);

        var linePlacing = mark.Placing as BaseLinePlacing;
        var line = new Tekla.Structures.Drawing.Line(view, a, b);
        line.Attributes.Line.Color = DrawingColors.Green;
        line.Insert();

        var rect = new Rectangle(view, box.MinPoint, box.MaxPoint);
        rect.Attributes.Line.Color = DrawingColors.Magenta;


        if (rect.Insert())
            activeDrawing.CommitChanges("(Host) MarkBBox");

    }
}
