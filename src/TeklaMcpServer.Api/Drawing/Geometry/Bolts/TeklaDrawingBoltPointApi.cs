using System.Linq;
using TeklaMcpServer.Api.Algorithms.Geometry;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;

namespace TeklaMcpServer.Api.Drawing;

public sealed class TeklaDrawingBoltPointApi : IDrawingBoltPointApi
{
    private readonly IDrawingBoltGeometryApi _boltGeometryApi;

    public TeklaDrawingBoltPointApi(Model model)
        : this(new TeklaDrawingBoltGeometryApi(model))
    {
    }

    internal TeklaDrawingBoltPointApi(IDrawingBoltGeometryApi boltGeometryApi)
    {
        _boltGeometryApi = boltGeometryApi;
    }

    public GetBoltGroupPointsResult GetBoltGroupPointsInView(int viewId, int modelId)
    {
        var geometry = _boltGeometryApi.GetBoltGroupGeometryInView(viewId, modelId);
        if (!geometry.Success)
            return Fail(viewId, modelId, geometry.Error ?? "Failed to read bolt group geometry in view.");

        var result = BuildResult(geometry);
        if (result.Points.Count == 0)
            return Fail(viewId, modelId, $"Bolt group {modelId} does not expose usable point geometry in view {viewId}.");

        return result;
    }

    public List<GetBoltGroupPointsResult> GetPartBoltPointsInView(int viewId, int partId)
    {
        var geometry = _boltGeometryApi.GetPartBoltGeometryInView(viewId, partId);
        if (!geometry.Success)
            return [];

        var results = new List<GetBoltGroupPointsResult>(geometry.BoltGroups.Count);
        foreach (var boltGroup in geometry.BoltGroups)
        {
            var result = BuildResult(new BoltGroupGeometryInViewResult
            {
                Success = true,
                ViewId = viewId,
                ModelId = boltGroup.ModelId,
                BoltGroup = boltGroup
            });

            if (result.Points.Count > 0)
                results.Add(result);
        }

        return results;
    }

    private static GetBoltGroupPointsResult BuildResult(BoltGroupGeometryInViewResult geometry)
    {
        var result = new GetBoltGroupPointsResult
        {
            Success = true,
            ViewId = geometry.ViewId,
            ModelId = geometry.ModelId
        };

        AddPoint(result.Points, DrawingBoltPointKind.ReferenceStart, DrawingBoltPointSourceKind.BoltGroup, geometry.ModelId, geometry.BoltGroup.FirstPosition);
        AddPoint(result.Points, DrawingBoltPointKind.ReferenceEnd, DrawingBoltPointSourceKind.BoltGroup, geometry.ModelId, geometry.BoltGroup.SecondPosition);
        AddPoint(result.Points, DrawingBoltPointKind.BboxMin, DrawingBoltPointSourceKind.BoltGroup, geometry.ModelId, geometry.BoltGroup.BboxMin);
        AddPoint(result.Points, DrawingBoltPointKind.BboxMax, DrawingBoltPointSourceKind.BoltGroup, geometry.ModelId, geometry.BoltGroup.BboxMax);

        var center = TryCreateCenterPoint(geometry.BoltGroup);
        AddPoint(result.Points, DrawingBoltPointKind.Center, DrawingBoltPointSourceKind.BoltGroup, geometry.ModelId, center);
        AddDirectionalPoints(result.Points, geometry.BoltGroup, geometry.ModelId);
        AddBoltPositions(result.Points, geometry.BoltGroup, geometry.ModelId);
        AddHullAndExtremes(result.Points, geometry.BoltGroup, geometry.ModelId);
        return result;
    }

    private static void AddDirectionalPoints(List<DrawingBoltPointInfo> points, BoltGroupGeometry geometry, int modelId)
    {
        if (geometry.BboxMin.Length < 2 || geometry.BboxMax.Length < 2)
            return;

        var minX = geometry.BboxMin[0];
        var minY = geometry.BboxMin[1];
        var maxX = geometry.BboxMax[0];
        var maxY = geometry.BboxMax[1];
        var centerX = (minX + maxX) / 2.0;
        var centerY = (minY + maxY) / 2.0;
        var centerZ = GetMidpointCoordinate(geometry.BboxMin, geometry.BboxMax, 2);

        AddPoint(points, DrawingBoltPointKind.Left, DrawingBoltPointSourceKind.BoltGroup, modelId, [minX, centerY, centerZ]);
        AddPoint(points, DrawingBoltPointKind.Right, DrawingBoltPointSourceKind.BoltGroup, modelId, [maxX, centerY, centerZ]);
        AddPoint(points, DrawingBoltPointKind.Top, DrawingBoltPointSourceKind.BoltGroup, modelId, [centerX, maxY, centerZ]);
        AddPoint(points, DrawingBoltPointKind.Bottom, DrawingBoltPointSourceKind.BoltGroup, modelId, [centerX, minY, centerZ]);
    }

    private static void AddBoltPositions(List<DrawingBoltPointInfo> points, BoltGroupGeometry geometry, int modelId)
    {
        foreach (var position in geometry.Positions)
        {
            AddPoint(
                points,
                DrawingBoltPointKind.BoltPosition,
                DrawingBoltPointSourceKind.BoltPosition,
                modelId,
                position.Point,
                position.Index);
        }
    }

    private static void AddHullAndExtremes(List<DrawingBoltPointInfo> points, BoltGroupGeometry geometry, int modelId)
    {
        var sourcePoints = geometry.Positions
            .Where(static p => p.Point.Length >= 2)
            .Select(static p => new Point(
                p.Point[0],
                p.Point[1],
                p.Point.Length > 2 ? p.Point[2] : 0.0))
            .ToList();

        if (sourcePoints.Count == 0)
        {
            TryAddReferencePoint(sourcePoints, geometry.FirstPosition);
            TryAddReferencePoint(sourcePoints, geometry.SecondPosition);
        }

        if (sourcePoints.Count == 0)
            return;

        var hull = ConvexHull.Compute(sourcePoints);
        for (var i = 0; i < hull.Count; i++)
            AddPoint(points, DrawingBoltPointKind.HullVertex, DrawingBoltPointSourceKind.BoltGroup, modelId, [hull[i].X, hull[i].Y, hull[i].Z], i);

        var farthestPair = FarthestPointPair.Find(hull);
        AddPoint(points, DrawingBoltPointKind.ExtremeStart, DrawingBoltPointSourceKind.BoltGroup, modelId, [farthestPair.First.X, farthestPair.First.Y, farthestPair.First.Z]);
        AddPoint(points, DrawingBoltPointKind.ExtremeEnd, DrawingBoltPointSourceKind.BoltGroup, modelId, [farthestPair.Second.X, farthestPair.Second.Y, farthestPair.Second.Z]);
    }

    private static void TryAddReferencePoint(List<Point> points, double[] source)
    {
        if (source.Length < 2)
            return;

        points.Add(new Point(
            source[0],
            source[1],
            source.Length > 2 ? source[2] : 0.0));
    }

    private static double[] TryCreateCenterPoint(BoltGroupGeometry geometry)
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

        if (geometry.Positions.Count > 0)
        {
            var sumX = 0.0;
            var sumY = 0.0;
            var sumZ = 0.0;
            var count = 0;

            foreach (var position in geometry.Positions)
            {
                if (position.Point.Length < 2)
                    continue;

                sumX += position.Point[0];
                sumY += position.Point[1];
                sumZ += position.Point.Length > 2 ? position.Point[2] : 0.0;
                count++;
            }

            if (count > 0)
                return [sumX / count, sumY / count, sumZ / count];
        }

        if (geometry.FirstPosition.Length >= 2 && geometry.SecondPosition.Length >= 2)
        {
            return
            [
                GetMidpointCoordinate(geometry.FirstPosition, geometry.SecondPosition, 0),
                GetMidpointCoordinate(geometry.FirstPosition, geometry.SecondPosition, 1),
                GetMidpointCoordinate(geometry.FirstPosition, geometry.SecondPosition, 2)
            ];
        }

        return [];
    }

    private static double GetMidpointCoordinate(double[] first, double[] second, int index)
    {
        var firstValue = first.Length > index ? first[index] : 0.0;
        var secondValue = second.Length > index ? second[index] : firstValue;
        return (firstValue + secondValue) / 2.0;
    }

    private static void AddPoint(
        List<DrawingBoltPointInfo> points,
        DrawingBoltPointKind kind,
        DrawingBoltPointSourceKind sourceKind,
        int sourceModelId,
        double[] point,
        int index = 0)
    {
        if (point.Length < 2)
            return;

        points.Add(new DrawingBoltPointInfo
        {
            Kind = kind,
            SourceKind = sourceKind,
            SourceModelId = sourceModelId,
            Index = index,
            Point = [.. point]
        });
    }

    private static GetBoltGroupPointsResult Fail(int viewId, int modelId, string error) =>
        new()
        {
            Success = false,
            ViewId = viewId,
            ModelId = modelId,
            Error = error
        };
}
