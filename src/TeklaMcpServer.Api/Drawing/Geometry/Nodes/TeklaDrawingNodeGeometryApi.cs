using System.Linq;
using Tekla.Structures.Model;

namespace TeklaMcpServer.Api.Drawing;

public sealed class TeklaDrawingNodeGeometryApi : IDrawingNodeGeometryApi
{
    private readonly IDrawingAssemblyGeometryApi _assemblyGeometryApi;
    private readonly IDrawingAssemblyPointApi _assemblyPointApi;
    private readonly IDrawingBoltPointApi _boltPointApi;

    public TeklaDrawingNodeGeometryApi(Model model)
        : this(
            new TeklaDrawingAssemblyGeometryApi(model),
            new TeklaDrawingAssemblyPointApi(model),
            new TeklaDrawingBoltPointApi(model))
    {
    }

    internal TeklaDrawingNodeGeometryApi(
        IDrawingAssemblyGeometryApi assemblyGeometryApi,
        IDrawingAssemblyPointApi assemblyPointApi,
        IDrawingBoltPointApi boltPointApi)
    {
        _assemblyGeometryApi = assemblyGeometryApi;
        _assemblyPointApi = assemblyPointApi;
        _boltPointApi = boltPointApi;
    }

    public GetAssemblyNodesResult GetAssemblyNodesInView(int viewId, int modelId)
    {
        var assemblyGeometry = _assemblyGeometryApi.GetAssemblyGeometryInView(viewId, modelId);
        if (!assemblyGeometry.Success)
            return Fail(viewId, modelId, assemblyGeometry.Error ?? "Failed to read assembly geometry in view.");

        var result = new GetAssemblyNodesResult
        {
            Success = true,
            ViewId = viewId,
            ModelId = modelId,
            MainPartId = assemblyGeometry.Assembly.MainPartId,
            Warnings = [.. assemblyGeometry.Assembly.Warnings]
        };

        var assemblyPoints = _assemblyPointApi.GetAssemblyPointsInView(viewId, modelId);
        if (!assemblyPoints.Success && !string.IsNullOrWhiteSpace(assemblyPoints.Error))
            result.Warnings.Add($"assembly-points:{assemblyPoints.Error}");

        var nodeIndex = 0;
        foreach (var boltGroup in assemblyGeometry.Assembly.BoltGroups)
        {
            var boltPoints = _boltPointApi.GetBoltGroupPointsInView(viewId, boltGroup.ModelId);
            if (!boltPoints.Success)
            {
                result.Warnings.Add($"bolt-node:{boltGroup.ModelId}:{boltPoints.Error}");
                continue;
            }

            var node = BuildBoltNode(nodeIndex++, assemblyGeometry.Assembly, assemblyPoints, boltPoints, boltGroup);
            if (node.Points.Count > 0)
                result.Nodes.Add(node);
        }

        if (result.Nodes.Count == 0)
        {
            var fallbackNode = BuildAssemblyFallbackNode(nodeIndex, assemblyGeometry.Assembly, assemblyPoints, modelId);
            if (fallbackNode.Points.Count > 0)
                result.Nodes.Add(fallbackNode);
        }

        return result;
    }

    private static NodeGeometry BuildBoltNode(
        int nodeIndex,
        AssemblyGeometry assemblyGeometry,
        GetAssemblyPointsResult assemblyPoints,
        GetBoltGroupPointsResult boltPoints,
        BoltGroupGeometry boltGroup)
    {
        var node = new NodeGeometry
        {
            Index = nodeIndex,
            Kind = DrawingNodeKind.BoltGroup,
            ModelId = boltGroup.ModelId,
            MainPartId = assemblyGeometry.MainPartId,
            BboxMin = CloneOrEmpty(boltGroup.BboxMin),
            BboxMax = CloneOrEmpty(boltGroup.BboxMax)
        };

        var center = FindPoint(boltPoints.Points, DrawingBoltPointKind.Center)
            ?? TryCreateCenterPoint(boltGroup.BboxMin, boltGroup.BboxMax)
            ?? TryCreateCenterPoint(boltGroup.FirstPosition, boltGroup.SecondPosition);
        if (center != null)
        {
            node.Center = [.. center];
            AddNodePoint(node.Points, DrawingNodePointKind.Center, DrawingNodePointSourceKind.Node, boltGroup.ModelId, center);
        }

        AddNodePoint(node.Points, DrawingNodePointKind.BboxMin, DrawingNodePointSourceKind.BoltGroup, boltGroup.ModelId, boltGroup.BboxMin);
        AddNodePoint(node.Points, DrawingNodePointKind.BboxMax, DrawingNodePointSourceKind.BoltGroup, boltGroup.ModelId, boltGroup.BboxMax);
        AddNodePoint(node.Points, DrawingNodePointKind.ReferenceStart, DrawingNodePointSourceKind.BoltGroup, boltGroup.ModelId, boltGroup.FirstPosition);
        AddNodePoint(node.Points, DrawingNodePointKind.ReferenceEnd, DrawingNodePointSourceKind.BoltGroup, boltGroup.ModelId, boltGroup.SecondPosition);

        foreach (var point in boltPoints.Points.Where(p => p.Kind == DrawingBoltPointKind.BoltPosition))
            AddNodePoint(node.Points, DrawingNodePointKind.BoltPosition, DrawingNodePointSourceKind.BoltGroup, point.SourceModelId, point.Point, point.Index);

        var extremeStart = FindPoint(boltPoints.Points, DrawingBoltPointKind.ExtremeStart);
        var extremeEnd = FindPoint(boltPoints.Points, DrawingBoltPointKind.ExtremeEnd);
        AddNodePoint(node.Points, DrawingNodePointKind.ExtremeStart, DrawingNodePointSourceKind.BoltGroup, boltGroup.ModelId, extremeStart);
        AddNodePoint(node.Points, DrawingNodePointKind.ExtremeEnd, DrawingNodePointSourceKind.BoltGroup, boltGroup.ModelId, extremeEnd);

        var assemblyCenter = FindPoint(assemblyPoints.Points, DrawingAssemblyPointKind.Center);
        var mainPartCenter = FindPoint(assemblyPoints.Points, DrawingAssemblyPointKind.MainPartCenter);
        AddNodePoint(node.Points, DrawingNodePointKind.AssemblyCenter, DrawingNodePointSourceKind.Assembly, assemblyGeometry.ModelId, assemblyCenter);
        AddNodePoint(node.Points, DrawingNodePointKind.MainPartCenter, DrawingNodePointSourceKind.MainPart, assemblyGeometry.MainPartId, mainPartCenter);

        ExtendBoundsFromPoints(node);
        return node;
    }

    private static NodeGeometry BuildAssemblyFallbackNode(
        int nodeIndex,
        AssemblyGeometry assemblyGeometry,
        GetAssemblyPointsResult assemblyPoints,
        int assemblyModelId)
    {
        var node = new NodeGeometry
        {
            Index = nodeIndex,
            Kind = DrawingNodeKind.AssemblyFallback,
            ModelId = assemblyModelId,
            MainPartId = assemblyGeometry.MainPartId,
            BboxMin = CloneOrEmpty(assemblyGeometry.BboxMin),
            BboxMax = CloneOrEmpty(assemblyGeometry.BboxMax)
        };

        var center = FindPoint(assemblyPoints.Points, DrawingAssemblyPointKind.Center)
            ?? TryCreateCenterPoint(assemblyGeometry.BboxMin, assemblyGeometry.BboxMax);
        if (center != null)
        {
            node.Center = [.. center];
            AddNodePoint(node.Points, DrawingNodePointKind.Center, DrawingNodePointSourceKind.Node, assemblyModelId, center);
        }

        AddNodePoint(node.Points, DrawingNodePointKind.BboxMin, DrawingNodePointSourceKind.Assembly, assemblyModelId, assemblyGeometry.BboxMin);
        AddNodePoint(node.Points, DrawingNodePointKind.BboxMax, DrawingNodePointSourceKind.Assembly, assemblyModelId, assemblyGeometry.BboxMax);

        var mainPartCenter = FindPoint(assemblyPoints.Points, DrawingAssemblyPointKind.MainPartCenter);
        var extremeStart = FindPoint(assemblyPoints.Points, DrawingAssemblyPointKind.ExtremeStart);
        var extremeEnd = FindPoint(assemblyPoints.Points, DrawingAssemblyPointKind.ExtremeEnd);
        AddNodePoint(node.Points, DrawingNodePointKind.MainPartCenter, DrawingNodePointSourceKind.MainPart, assemblyGeometry.MainPartId, mainPartCenter);
        AddNodePoint(node.Points, DrawingNodePointKind.ExtremeStart, DrawingNodePointSourceKind.Assembly, assemblyModelId, extremeStart);
        AddNodePoint(node.Points, DrawingNodePointKind.ExtremeEnd, DrawingNodePointSourceKind.Assembly, assemblyModelId, extremeEnd);

        ExtendBoundsFromPoints(node);
        return node;
    }

    private static void ExtendBoundsFromPoints(NodeGeometry node)
    {
        foreach (var point in node.Points)
            ExtendBounds(node, point.Point);
    }

    private static void ExtendBounds(NodeGeometry node, double[] point)
    {
        if (point.Length < 2)
            return;

        var x = point[0];
        var y = point[1];
        var z = point.Length > 2 ? point[2] : 0.0;

        if (node.BboxMin.Length < 3 || node.BboxMax.Length < 3)
        {
            node.BboxMin = [x, y, z];
            node.BboxMax = [x, y, z];
            return;
        }

        node.BboxMin[0] = System.Math.Min(node.BboxMin[0], x);
        node.BboxMin[1] = System.Math.Min(node.BboxMin[1], y);
        node.BboxMin[2] = System.Math.Min(node.BboxMin[2], z);

        node.BboxMax[0] = System.Math.Max(node.BboxMax[0], x);
        node.BboxMax[1] = System.Math.Max(node.BboxMax[1], y);
        node.BboxMax[2] = System.Math.Max(node.BboxMax[2], z);
    }

    private static void AddNodePoint(
        List<DrawingNodePointInfo> points,
        DrawingNodePointKind kind,
        DrawingNodePointSourceKind sourceKind,
        int sourceModelId,
        double[]? point,
        int index = 0)
    {
        if (point == null || point.Length < 2)
            return;

        points.Add(new DrawingNodePointInfo
        {
            Kind = kind,
            SourceKind = sourceKind,
            SourceModelId = sourceModelId,
            Index = index,
            Point = [.. point]
        });
    }

    private static double[]? FindPoint(IEnumerable<DrawingAssemblyPointInfo> points, DrawingAssemblyPointKind kind) =>
        points.FirstOrDefault(p => p.Kind == kind)?.Point;

    private static double[]? FindPoint(IEnumerable<DrawingBoltPointInfo> points, DrawingBoltPointKind kind) =>
        points.FirstOrDefault(p => p.Kind == kind)?.Point;

    private static double[]? TryCreateCenterPoint(double[] first, double[] second)
    {
        if (first.Length < 2 || second.Length < 2)
            return null;

        return
        [
            GetMidpointCoordinate(first, second, 0),
            GetMidpointCoordinate(first, second, 1),
            GetMidpointCoordinate(first, second, 2)
        ];
    }

    private static double GetMidpointCoordinate(double[] first, double[] second, int index)
    {
        var firstValue = first.Length > index ? first[index] : 0.0;
        var secondValue = second.Length > index ? second[index] : firstValue;
        return (firstValue + secondValue) / 2.0;
    }

    private static double[] CloneOrEmpty(double[] point) => point.Length == 0 ? [] : [.. point];

    private static GetAssemblyNodesResult Fail(int viewId, int modelId, string error) =>
        new()
        {
            Success = false,
            ViewId = viewId,
            ModelId = modelId,
            Error = error
        };
}
