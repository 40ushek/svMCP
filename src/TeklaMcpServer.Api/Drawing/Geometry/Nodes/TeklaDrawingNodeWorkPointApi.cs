using System.Linq;
using Tekla.Structures.Model;

namespace TeklaMcpServer.Api.Drawing;

public sealed class TeklaDrawingNodeWorkPointApi : IDrawingNodeWorkPointApi
{
    private readonly IDrawingNodeGeometryApi _nodeGeometryApi;

    public TeklaDrawingNodeWorkPointApi(Model model)
        : this(new TeklaDrawingNodeGeometryApi(model))
    {
    }

    internal TeklaDrawingNodeWorkPointApi(IDrawingNodeGeometryApi nodeGeometryApi)
    {
        _nodeGeometryApi = nodeGeometryApi;
    }

    public GetAssemblyWorkPointsResult GetAssemblyWorkPointsInView(int viewId, int modelId)
    {
        var nodes = _nodeGeometryApi.GetAssemblyNodesInView(viewId, modelId);
        if (!nodes.Success)
            return Fail(viewId, modelId, nodes.Error ?? "Failed to read node geometry in view.");

        var result = new GetAssemblyWorkPointsResult
        {
            Success = true,
            ViewId = viewId,
            ModelId = modelId,
            MainPartId = nodes.MainPartId,
            Warnings = [.. nodes.Warnings]
        };

        foreach (var node in nodes.Nodes)
        {
            var workPoints = BuildWorkPointSet(node);
            if (workPoints.Points.Count == 0)
                continue;

            result.Nodes.Add(workPoints);
        }

        if (result.Nodes.Count == 0)
            return Fail(viewId, modelId, $"Assembly {modelId} does not expose usable work points in view {viewId}.");

        return result;
    }

    private static NodeWorkPointSet BuildWorkPointSet(NodeGeometry node)
    {
        var result = new NodeWorkPointSet
        {
            NodeIndex = node.Index,
            NodeKind = node.Kind,
            NodeModelId = node.ModelId,
            MainPartId = node.MainPartId
        };

        var nodeCenter = FindPoint(node.Points, DrawingNodePointKind.Center);
        var mainPartCenter = FindPoint(node.Points, DrawingNodePointKind.MainPartCenter);
        var assemblyCenter = FindPoint(node.Points, DrawingNodePointKind.AssemblyCenter);
        var referenceStart = FindPoint(node.Points, DrawingNodePointKind.ReferenceStart);
        var referenceEnd = FindPoint(node.Points, DrawingNodePointKind.ReferenceEnd);
        var extremeStart = FindPoint(node.Points, DrawingNodePointKind.ExtremeStart);
        var extremeEnd = FindPoint(node.Points, DrawingNodePointKind.ExtremeEnd);
        var firstBoltPosition = node.Points.FirstOrDefault(p => p.Kind == DrawingNodePointKind.BoltPosition)?.Point;

        var primary = FirstUsable(nodeCenter, firstBoltPosition, mainPartCenter, assemblyCenter);
        var secondary = FirstDistinct(primary, mainPartCenter, assemblyCenter, referenceStart, extremeStart, referenceEnd, extremeEnd);

        result.PrimaryPoint = CloneOrEmpty(primary);
        result.SecondaryPoint = CloneOrEmpty(secondary);

        AddWorkPoint(result.Points, DrawingWorkPointKind.Primary, node, primary);
        AddWorkPoint(result.Points, DrawingWorkPointKind.Secondary, node, secondary);
        AddWorkPoint(result.Points, DrawingWorkPointKind.MainPartAnchor, node, mainPartCenter);
        AddWorkPoint(result.Points, DrawingWorkPointKind.AssemblyAnchor, node, assemblyCenter);

        var lineStart = FirstUsable(referenceStart, extremeStart);
        var lineEnd = FirstDistinct(lineStart, referenceEnd, extremeEnd);
        AddWorkPoint(result.Points, DrawingWorkPointKind.ReferenceStart, node, lineStart);
        AddWorkPoint(result.Points, DrawingWorkPointKind.ReferenceEnd, node, lineEnd);
        AddWorkPoint(result.Points, DrawingWorkPointKind.ExtremeStart, node, extremeStart);
        AddWorkPoint(result.Points, DrawingWorkPointKind.ExtremeEnd, node, extremeEnd);

        if (lineStart != null && lineEnd != null)
        {
            result.ReferenceLine = new DrawingLineInfo
            {
                StartX = lineStart[0],
                StartY = lineStart[1],
                EndX = lineEnd[0],
                EndY = lineEnd[1]
            };
        }

        return result;
    }

    private static void AddWorkPoint(
        List<DrawingWorkPointInfo> points,
        DrawingWorkPointKind kind,
        NodeGeometry node,
        double[]? point)
    {
        if (point == null || point.Length < 2)
            return;

        points.Add(new DrawingWorkPointInfo
        {
            Kind = kind,
            SourceNodeIndex = node.Index,
            SourceModelId = node.ModelId,
            Point = [.. point]
        });
    }

    private static double[]? FindPoint(IEnumerable<DrawingNodePointInfo> points, DrawingNodePointKind kind) =>
        points.FirstOrDefault(p => p.Kind == kind)?.Point;

    private static double[]? FirstUsable(params double[]?[] points)
    {
        foreach (var point in points)
            if (point != null && point.Length >= 2)
                return point;

        return null;
    }

    private static double[]? FirstDistinct(double[]? basis, params double[]?[] points)
    {
        foreach (var point in points)
        {
            if (point == null || point.Length < 2)
                continue;

            if (basis == null || !SamePoint(basis, point))
                return point;
        }

        return null;
    }

    private static bool SamePoint(double[] left, double[] right)
    {
        const double epsilon = 0.0001;
        var leftX = left.Length > 0 ? left[0] : 0.0;
        var leftY = left.Length > 1 ? left[1] : 0.0;
        var rightX = right.Length > 0 ? right[0] : 0.0;
        var rightY = right.Length > 1 ? right[1] : 0.0;

        return System.Math.Abs(leftX - rightX) <= epsilon
            && System.Math.Abs(leftY - rightY) <= epsilon;
    }

    private static double[] CloneOrEmpty(double[]? point) => point == null || point.Length == 0 ? [] : [.. point];

    private static GetAssemblyWorkPointsResult Fail(int viewId, int modelId, string error) =>
        new()
        {
            Success = false,
            ViewId = viewId,
            ModelId = modelId,
            Error = error
        };
}
