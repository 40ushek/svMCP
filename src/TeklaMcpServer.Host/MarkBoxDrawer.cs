using Tekla.Structures.Drawing;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;
using DrwPart = Tekla.Structures.Drawing.Part;

namespace TeklaMcpServer.Host;

internal static class MarkBoxDrawer
{
    private static readonly Model _model = new();
    public static void DrawBoundingBox(Mark mark, Drawing activeDrawing)
    {
        var view = mark.GetView() as View;
        if (view == null) return;

        var objs = mark.GetRelatedObjects();
        objs.MoveNext();

        var related = objs.Current;

        var placing = mark.Placing;

        var ob = mark.GetObjectAlignedBoundingBox();
        var center = new Point((ob.MinPoint.X + ob.MaxPoint.X) / 2, (ob.MinPoint.Y + ob.MaxPoint.Y) / 2);

        var dirX = related is DrwPart part && (placing is AlongLinePlacing || placing is BaseLinePlacing)
            ? GetMarkDirection(part)
            : MarkBoxDrawer._dirX;

        var dirY = _dirZ.Cross(dirX);

        dirX.Normalize(Math.Round(ob.Width / 2, 2));
        dirY.Normalize(Math.Round(ob.Height / 2, 2));

        var pointList = new PointList
        {
            new Point(center - dirX - dirY),
            new Point(center - dirX + dirY),
            new Point(center + dirX + dirY),
            new Point(center + dirX - dirY),
            new Point(center - dirX - dirY),
        };

        var rect1 = new Rectangle(view, ob.MinPoint, ob.MaxPoint);
        rect1.Attributes.Line.Color = DrawingColors.Magenta;
        rect1.Insert();

        var pl = new Polyline(view, pointList);
        pl.Attributes.Line.Color = DrawingColors.Green;
        pl.Insert();

        activeDrawing.CommitChanges();
    }


    private static readonly Vector _dirX = new(1, 0, 0);
    private static readonly Vector _dirZ = new(0, 0, 1);
    private static Vector GetMarkDirection(DrwPart part)
    {
        var modelObject = _model.SelectModelObject(part.ModelIdentifier);
        var partCs = modelObject.GetCoordinateSystem();

        var view = part.GetView() as View;
        var viewCs = view?.ViewCoordinateSystem;
        var viewNormal = viewCs.AxisX.Cross(viewCs.AxisY);
        var viewPlane = new GeometricPlane(viewCs);


        var angleRad = viewNormal.GetAngleBetween(partCs.AxisX);
        var angleDeg = angleRad * 180 / Math.PI;

        // part axis not visible in this view — fall back to view X axis
        if (angleDeg < 5)
            return _dirX;

        var matrix = MatrixFactory.ToCoordinateSystem(viewCs);
        var p1 = matrix.Transform(Projection.PointToPlane(partCs.Origin, viewPlane));
        var p2 = matrix.Transform(Projection.PointToPlane(partCs.Origin + partCs.AxisX, viewPlane));
        var dir = new Vector(p2 - p1);
        dir.Normalize();
        return dir;
    }
}
