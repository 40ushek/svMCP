using System.Collections.Generic;
using System.Linq;
using TeklaMcpServer.Api.Algorithms.Geometry;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;

namespace TeklaMcpServer.Api.Drawing;

public sealed class TeklaDrawingPartPointApi : IDrawingPartPointApi
{
    private readonly TeklaDrawingPartGeometryApi _partGeometryApi;

    public TeklaDrawingPartPointApi(Model model, TeklaDrawingPartGeometryApi? partGeometryApi = null)
    {
        _partGeometryApi = partGeometryApi ?? new TeklaDrawingPartGeometryApi(model);
    }

    public GetPartPointsResult GetPartPointsInView(int viewId, int modelId)
    {
        var geometry = _partGeometryApi.GetPartGeometryInView(viewId, modelId);
        if (!geometry.Success)
            return Fail(viewId, modelId, geometry.Error ?? "Failed to read part geometry in view.");

        var result = BuildResult(geometry);
        if (result.Points.Count == 0)
            return Fail(viewId, modelId, $"Model object {modelId} does not expose usable point geometry in view {viewId}.");

        return result;
    }

    public List<GetPartPointsResult> GetAllPartPointsInView(int viewId)
    {
        var geometries = _partGeometryApi.GetAllPartsGeometryInView(viewId);
        var results = new List<GetPartPointsResult>(geometries.Count);

        foreach (var geometry in geometries)
        {
            var result = BuildResult(geometry);
            if (result.Points.Count == 0)
                continue;

            results.Add(result);
        }

        return results;
    }

    private static GetPartPointsResult BuildResult(PartGeometryInViewResult geometry)
    {
        var result = new GetPartPointsResult
        {
            Success = true,
            ViewId = geometry.ViewId,
            ModelId = geometry.ModelId,
            Type = geometry.Type,
            Name = geometry.Name,
            PartPos = geometry.PartPos,
            Profile = geometry.Profile,
            Material = geometry.Material
        };

        AddPoint(result.Points, DrawingPartPointKind.AxisStart, DrawingPartPointSourceKind.Axis, geometry.StartPoint);
        AddPoint(result.Points, DrawingPartPointKind.AxisEnd, DrawingPartPointSourceKind.Axis, geometry.EndPoint);
        AddAxisMidpoint(result.Points, geometry.StartPoint, geometry.EndPoint);
        AddPoint(result.Points, DrawingPartPointKind.Origin, DrawingPartPointSourceKind.Part, geometry.CoordinateSystemOrigin);
        AddPoint(result.Points, DrawingPartPointKind.BboxMin, DrawingPartPointSourceKind.Part, geometry.BboxMin);
        AddPoint(result.Points, DrawingPartPointKind.BboxMax, DrawingPartPointSourceKind.Part, geometry.BboxMax);

        var centerPoint = TryCreateCenterPoint(geometry);
        if (centerPoint.Length > 0)
        {
            AddPoint(
                result.Points,
                DrawingPartPointKind.Center,
                geometry.BboxMin.Length >= 2 && geometry.BboxMax.Length >= 2
                    ? DrawingPartPointSourceKind.Part
                    : geometry.StartPoint.Length >= 2 && geometry.EndPoint.Length >= 2
                        ? DrawingPartPointSourceKind.Axis
                        : DrawingPartPointSourceKind.Part,
                centerPoint);
        }

        AddDirectionalPoints(result.Points, geometry.BboxMin, geometry.BboxMax);
        AddSolidDerivedPoints(result.Points, geometry.SolidVertices);
        return result;
    }

    private static void AddDirectionalPoints(List<DrawingPartPointInfo> points, double[] bboxMin, double[] bboxMax)
    {
        if (bboxMin.Length < 2 || bboxMax.Length < 2)
            return;

        var minX = bboxMin[0];
        var minY = bboxMin[1];
        var maxX = bboxMax[0];
        var maxY = bboxMax[1];
        var centerX = (minX + maxX) / 2.0;
        var centerY = (minY + maxY) / 2.0;
        var centerZ = GetMidpointCoordinate(bboxMin, bboxMax, 2);

        AddPoint(points, DrawingPartPointKind.BottomLeft, DrawingPartPointSourceKind.Part, [minX, minY, centerZ]);
        AddPoint(points, DrawingPartPointKind.BottomRight, DrawingPartPointSourceKind.Part, [maxX, minY, centerZ]);
        AddPoint(points, DrawingPartPointKind.TopLeft, DrawingPartPointSourceKind.Part, [minX, maxY, centerZ]);
        AddPoint(points, DrawingPartPointKind.TopRight, DrawingPartPointSourceKind.Part, [maxX, maxY, centerZ]);
        AddPoint(points, DrawingPartPointKind.Left, DrawingPartPointSourceKind.Part, [minX, centerY, centerZ]);
        AddPoint(points, DrawingPartPointKind.Right, DrawingPartPointSourceKind.Part, [maxX, centerY, centerZ]);
        AddPoint(points, DrawingPartPointKind.Top, DrawingPartPointSourceKind.Part, [centerX, maxY, centerZ]);
        AddPoint(points, DrawingPartPointKind.Bottom, DrawingPartPointSourceKind.Part, [centerX, minY, centerZ]);
    }

    private static void AddAxisMidpoint(List<DrawingPartPointInfo> points, double[] startPoint, double[] endPoint)
    {
        if (startPoint.Length < 2 || endPoint.Length < 2)
            return;

        AddPoint(
            points,
            DrawingPartPointKind.AxisMidpoint,
            DrawingPartPointSourceKind.Axis,
            [
                GetMidpointCoordinate(startPoint, endPoint, 0),
                GetMidpointCoordinate(startPoint, endPoint, 1),
                GetMidpointCoordinate(startPoint, endPoint, 2)
            ]);
    }

    private static void AddSolidDerivedPoints(List<DrawingPartPointInfo> points, List<double[]> solidVertices)
    {
        if (solidVertices.Count == 0)
            return;

        for (var i = 0; i < solidVertices.Count; i++)
            AddPoint(points, DrawingPartPointKind.SolidVertex, DrawingPartPointSourceKind.Part, solidVertices[i], i);

        var geometryPoints = solidVertices
            .Where(static vertex => vertex.Length >= 2)
            .Select(static vertex => new Point(
                vertex[0],
                vertex[1],
                vertex.Length > 2 ? vertex[2] : 0.0))
            .ToList();
        if (geometryPoints.Count == 0)
            return;

        var hull = ConvexHull.Compute(geometryPoints);
        for (var i = 0; i < hull.Count; i++)
            AddPoint(points, DrawingPartPointKind.HullVertex, DrawingPartPointSourceKind.Part, [hull[i].X, hull[i].Y, hull[i].Z], i);

        var farthestPair = FarthestPointPair.Find(hull);
        AddPoint(points, DrawingPartPointKind.ExtremeStart, DrawingPartPointSourceKind.Part, [farthestPair.First.X, farthestPair.First.Y, farthestPair.First.Z]);
        AddPoint(points, DrawingPartPointKind.ExtremeEnd, DrawingPartPointSourceKind.Part, [farthestPair.Second.X, farthestPair.Second.Y, farthestPair.Second.Z]);
    }

    private static double[] TryCreateCenterPoint(PartGeometryInViewResult geometry)
    {
        if (geometry.BboxMin.Length >= 2 && geometry.BboxMax.Length >= 2)
        {
            return
            [
                GetMidpointCoordinate(geometry.BboxMin, geometry.BboxMax, 0),
                GetMidpointCoordinate(geometry.BboxMin, geometry.BboxMax, 1),
                GetMidpointCoordinate(geometry.BboxMin, geometry.BboxMax, 2)
            ];
        }

        if (geometry.StartPoint.Length >= 2 && geometry.EndPoint.Length >= 2)
        {
            return
            [
                GetMidpointCoordinate(geometry.StartPoint, geometry.EndPoint, 0),
                GetMidpointCoordinate(geometry.StartPoint, geometry.EndPoint, 1),
                GetMidpointCoordinate(geometry.StartPoint, geometry.EndPoint, 2)
            ];
        }

        return geometry.CoordinateSystemOrigin.Length >= 2
            ? [.. geometry.CoordinateSystemOrigin]
            : [];
    }

    private static double GetMidpointCoordinate(double[] first, double[] second, int index)
    {
        var firstValue = first.Length > index ? first[index] : 0.0;
        var secondValue = second.Length > index ? second[index] : firstValue;
        return (firstValue + secondValue) / 2.0;
    }

    private static void AddPoint(
        List<DrawingPartPointInfo> points,
        DrawingPartPointKind kind,
        DrawingPartPointSourceKind sourceKind,
        double[] point,
        int index = 0)
    {
        if (point.Length < 2)
            return;

        points.Add(new DrawingPartPointInfo
        {
            Kind = kind,
            SourceKind = sourceKind,
            Index = index,
            Point = [.. point]
        });
    }

    private static GetPartPointsResult Fail(int viewId, int modelId, string error) =>
        new()
        {
            Success = false,
            ViewId = viewId,
            ModelId = modelId,
            Error = error
        };
}
