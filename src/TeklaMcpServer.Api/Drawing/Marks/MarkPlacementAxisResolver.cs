using System.Reflection;
using Tekla.Structures.Drawing;
using Tekla.Structures.Model;

namespace TeklaMcpServer.Api.Drawing;

internal static class MarkPlacementAxisResolver
{
    private const double AxisEpsilon = 0.001;

    public static bool TryGetRelatedPartAxisInView(Mark mark, Model model, int? explicitViewId, out double axisDx, out double axisDy)
    {
        axisDx = 0.0;
        axisDy = 0.0;

        var viewId = explicitViewId.GetValueOrDefault();
        if (viewId == 0)
        {
            var ownerView = mark.GetView();
            if (ownerView == null)
                return false;

            viewId = TryGetIdentifierId(ownerView);
        }

        if (viewId == 0)
            return false;

        var related = mark.GetRelatedObjects();
        var partGeometryApi = new TeklaDrawingPartGeometryApi(model);
        while (related.MoveNext())
        {
            if (related.Current is not Tekla.Structures.Drawing.ModelObject drawingModelObject)
                continue;

            var result = partGeometryApi.GetPartGeometryInView(viewId, drawingModelObject.ModelIdentifier.ID);
            if (!result.Success || result.StartPoint.Length < 2 || result.EndPoint.Length < 2)
                continue;

            axisDx = result.EndPoint[0] - result.StartPoint[0];
            axisDy = result.EndPoint[1] - result.StartPoint[1];
            var axisLength = Math.Sqrt((axisDx * axisDx) + (axisDy * axisDy));
            if (axisLength < AxisEpsilon)
                continue;

            axisDx /= axisLength;
            axisDy /= axisLength;
            return true;
        }

        return false;
    }

    public static bool TryGetPlacingLineAxis(object? placing, out double axisDx, out double axisDy)
    {
        axisDx = 0.0;
        axisDy = 0.0;

        if (placing == null)
            return false;

        var startPointProperty = placing.GetType().GetProperty("StartPoint", BindingFlags.Instance | BindingFlags.Public);
        var endPointProperty = placing.GetType().GetProperty("EndPoint", BindingFlags.Instance | BindingFlags.Public);
        if (startPointProperty?.GetValue(placing) is not Tekla.Structures.Geometry3d.Point startPoint ||
            endPointProperty?.GetValue(placing) is not Tekla.Structures.Geometry3d.Point endPoint)
            return false;

        axisDx = endPoint.X - startPoint.X;
        axisDy = endPoint.Y - startPoint.Y;
        var axisLength = Math.Sqrt((axisDx * axisDx) + (axisDy * axisDy));
        if (axisLength < AxisEpsilon)
            return false;

        axisDx /= axisLength;
        axisDy /= axisLength;
        return true;
    }

    public static bool TryGetAngleAxis(double angleDeg, out double axisDx, out double axisDy)
    {
        var rad = angleDeg * Math.PI / 180.0;
        axisDx = Math.Cos(rad);
        axisDy = Math.Sin(rad);
        return Math.Abs(axisDx) >= AxisEpsilon || Math.Abs(axisDy) >= AxisEpsilon;
    }

    private static int TryGetIdentifierId(object drawingObject)
    {
        var getIdentifier = drawingObject.GetType().GetMethod("GetIdentifier", BindingFlags.Instance | BindingFlags.Public);
        var identifier = getIdentifier?.Invoke(drawingObject, null);
        var idProperty = identifier?.GetType().GetProperty("ID", BindingFlags.Instance | BindingFlags.Public);
        return idProperty?.GetValue(identifier) as int? ?? 0;
    }
}
