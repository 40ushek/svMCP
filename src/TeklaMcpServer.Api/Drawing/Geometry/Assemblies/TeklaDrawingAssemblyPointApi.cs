using System.Linq;
using TeklaMcpServer.Api.Algorithms.Geometry;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;

namespace TeklaMcpServer.Api.Drawing;

public sealed class TeklaDrawingAssemblyPointApi : IDrawingAssemblyPointApi
{
    private readonly IDrawingAssemblyGeometryApi _assemblyGeometryApi;

    public TeklaDrawingAssemblyPointApi(Model model)
        : this(new TeklaDrawingAssemblyGeometryApi(model))
    {
    }

    internal TeklaDrawingAssemblyPointApi(IDrawingAssemblyGeometryApi assemblyGeometryApi)
    {
        _assemblyGeometryApi = assemblyGeometryApi;
    }

    public GetAssemblyPointsResult GetAssemblyPointsInView(int viewId, int modelId)
    {
        var geometry = _assemblyGeometryApi.GetAssemblyGeometryInView(viewId, modelId);
        if (!geometry.Success)
            return Fail(viewId, modelId, geometry.Error ?? "Failed to read assembly geometry in view.");

        var result = BuildResult(geometry);
        if (result.Points.Count == 0)
            return Fail(viewId, modelId, $"Assembly {modelId} does not expose usable point geometry in view {viewId}.");

        return result;
    }

    private static GetAssemblyPointsResult BuildResult(AssemblyGeometryInViewResult geometry)
    {
        var result = new GetAssemblyPointsResult
        {
            Success = true,
            ViewId = geometry.ViewId,
            ModelId = geometry.ModelId,
            MainPartId = geometry.Assembly.MainPartId,
            Warnings = [.. geometry.Assembly.Warnings]
        };

        AddPoint(result.Points, DrawingAssemblyPointKind.BboxMin, DrawingAssemblyPointSourceKind.Assembly, geometry.ModelId, geometry.Assembly.BboxMin);
        AddPoint(result.Points, DrawingAssemblyPointKind.BboxMax, DrawingAssemblyPointSourceKind.Assembly, geometry.ModelId, geometry.Assembly.BboxMax);
        AddCenterAndDirectionalPoints(result.Points, geometry.Assembly.BboxMin, geometry.Assembly.BboxMax, geometry.ModelId);
        AddMainPartPoints(result.Points, geometry.Assembly);
        AddMemberPartCenters(result.Points, geometry.Assembly);
        AddBoltPoints(result.Points, geometry.Assembly);
        AddHullAndExtremes(result.Points, geometry.Assembly, geometry.ModelId);
        return result;
    }

    private static void AddCenterAndDirectionalPoints(
        List<DrawingAssemblyPointInfo> points,
        double[] bboxMin,
        double[] bboxMax,
        int assemblyId)
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

        AddPoint(points, DrawingAssemblyPointKind.Center, DrawingAssemblyPointSourceKind.Assembly, assemblyId, [centerX, centerY, centerZ]);
        AddPoint(points, DrawingAssemblyPointKind.Left, DrawingAssemblyPointSourceKind.Assembly, assemblyId, [minX, centerY, centerZ]);
        AddPoint(points, DrawingAssemblyPointKind.Right, DrawingAssemblyPointSourceKind.Assembly, assemblyId, [maxX, centerY, centerZ]);
        AddPoint(points, DrawingAssemblyPointKind.Top, DrawingAssemblyPointSourceKind.Assembly, assemblyId, [centerX, maxY, centerZ]);
        AddPoint(points, DrawingAssemblyPointKind.Bottom, DrawingAssemblyPointSourceKind.Assembly, assemblyId, [centerX, minY, centerZ]);
    }

    private static void AddMainPartPoints(List<DrawingAssemblyPointInfo> points, AssemblyGeometry geometry)
    {
        var mainPart = geometry.PartMembers.FirstOrDefault(static p => p.IsMainPart)
            ?? geometry.PartMembers.FirstOrDefault(p => p.ModelId == geometry.MainPartId);
        if (mainPart == null)
            return;

        AddPoint(points, DrawingAssemblyPointKind.MainPartBboxMin, DrawingAssemblyPointSourceKind.MainPart, mainPart.ModelId, mainPart.Solid.BboxMin);
        AddPoint(points, DrawingAssemblyPointKind.MainPartBboxMax, DrawingAssemblyPointSourceKind.MainPart, mainPart.ModelId, mainPart.Solid.BboxMax);

        var center = TryCreateCenterPoint(mainPart.Solid.BboxMin, mainPart.Solid.BboxMax);
        AddPoint(points, DrawingAssemblyPointKind.MainPartCenter, DrawingAssemblyPointSourceKind.MainPart, mainPart.ModelId, center);
    }

    private static void AddMemberPartCenters(List<DrawingAssemblyPointInfo> points, AssemblyGeometry geometry)
    {
        foreach (var part in geometry.PartMembers)
        {
            var center = TryCreateCenterPoint(part.Solid.BboxMin, part.Solid.BboxMax);
            AddPoint(points, DrawingAssemblyPointKind.MemberPartCenter, part.IsMainPart ? DrawingAssemblyPointSourceKind.MainPart : DrawingAssemblyPointSourceKind.Part, part.ModelId, center);
        }
    }

    private static void AddBoltPoints(List<DrawingAssemblyPointInfo> points, AssemblyGeometry geometry)
    {
        foreach (var boltGroup in geometry.BoltGroups)
        {
            foreach (var boltPoint in boltGroup.Positions)
            {
                AddPoint(
                    points,
                    DrawingAssemblyPointKind.BoltPoint,
                    DrawingAssemblyPointSourceKind.Bolt,
                    boltGroup.ModelId,
                    boltPoint.Point,
                    boltPoint.Index);
            }
        }
    }

    private static void AddHullAndExtremes(List<DrawingAssemblyPointInfo> points, AssemblyGeometry geometry, int assemblyId)
    {
        var sourcePoints = geometry.PartMembers
            .SelectMany(static part => part.Solid.Vertices)
            .Where(static vertex => vertex.Point.Length >= 2)
            .Select(static vertex => new Point(
                vertex.Point[0],
                vertex.Point[1],
                vertex.Point.Length > 2 ? vertex.Point[2] : 0.0))
            .ToList();

        foreach (var boltPoint in geometry.BoltGroups.SelectMany(static bolt => bolt.Positions))
        {
            if (boltPoint.Point.Length < 2)
                continue;

            sourcePoints.Add(new Point(
                boltPoint.Point[0],
                boltPoint.Point[1],
                boltPoint.Point.Length > 2 ? boltPoint.Point[2] : 0.0));
        }

        if (sourcePoints.Count == 0)
            return;

        var hull = ConvexHull.Compute(sourcePoints);
        for (var i = 0; i < hull.Count; i++)
            AddPoint(points, DrawingAssemblyPointKind.HullVertex, DrawingAssemblyPointSourceKind.Assembly, assemblyId, [hull[i].X, hull[i].Y, hull[i].Z], i);

        var farthestPair = FarthestPointPair.Find(hull);
        AddPoint(points, DrawingAssemblyPointKind.ExtremeStart, DrawingAssemblyPointSourceKind.Assembly, assemblyId, [farthestPair.First.X, farthestPair.First.Y, farthestPair.First.Z]);
        AddPoint(points, DrawingAssemblyPointKind.ExtremeEnd, DrawingAssemblyPointSourceKind.Assembly, assemblyId, [farthestPair.Second.X, farthestPair.Second.Y, farthestPair.Second.Z]);
    }

    private static double[] TryCreateCenterPoint(double[] bboxMin, double[] bboxMax)
    {
        if (bboxMin.Length < 2 || bboxMax.Length < 2)
            return [];

        return
        [
            GetMidpointCoordinate(bboxMin, bboxMax, 0),
            GetMidpointCoordinate(bboxMin, bboxMax, 1),
            GetMidpointCoordinate(bboxMin, bboxMax, 2)
        ];
    }

    private static double GetMidpointCoordinate(double[] first, double[] second, int index)
    {
        var firstValue = first.Length > index ? first[index] : 0.0;
        var secondValue = second.Length > index ? second[index] : firstValue;
        return (firstValue + secondValue) / 2.0;
    }

    private static void AddPoint(
        List<DrawingAssemblyPointInfo> points,
        DrawingAssemblyPointKind kind,
        DrawingAssemblyPointSourceKind sourceKind,
        int sourceModelId,
        double[] point,
        int index = 0)
    {
        if (point.Length < 2)
            return;

        points.Add(new DrawingAssemblyPointInfo
        {
            Kind = kind,
            SourceKind = sourceKind,
            SourceModelId = sourceModelId,
            Index = index,
            Point = [.. point]
        });
    }

    private static GetAssemblyPointsResult Fail(int viewId, int modelId, string error) =>
        new()
        {
            Success = false,
            ViewId = viewId,
            ModelId = modelId,
            Error = error
        };
}
