namespace TeklaMcpServer.Api.Drawing;

/// <summary>Shared traversal of projected contour trees, independent of dimension previews.</summary>
internal static class OutlineTreeGeometry
{
    internal static (double[] A, double[] B)[] Edges(IEnumerable<OutlineTreeNodeResult> nodes) =>
        Flatten(nodes).Where(node => !node.IsHole)
            .SelectMany(node => node.Polygon.Select((point, index) =>
                (A: point, B: node.Polygon[(index + 1) % node.Polygon.Count]))).ToArray();

    internal static IEnumerable<OutlineTreeNodeResult> Flatten(IEnumerable<OutlineTreeNodeResult> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Children)) yield return child;
        }
    }
}
