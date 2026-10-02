using TeklaMcpServer.Api.Algorithms.Geometry;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>Axis distance to an actual projected outer contour; never a bbox or convex hull.</summary>
internal static class BoltEdgeDistancePreview
{
    internal static object Build(int groupId, int partId, string rowId, BoltPointGeometry[] sources,
        int axis, int sign, IReadOnlyList<OutlineTreeNodeResult>? contours, bool centersInside)
    {
        var center = sources[0].Point;
        var reason = centersInside ? null : "not every source center is inside the view restriction; axial extent is unverified";
        double[]? edge = null;
        var contourIndex = -1;
        if (reason == null)
            reason = FindEdge(contours, center, axis, sign, out edge, out contourIndex);
        var distance = edge == null ? (double?)null : Math.Abs(edge[axis] - center[axis]);
        return new {
            proposalId = $"{rowId}-part-{partId}-edge-{(sign < 0 ? "min" : "max")}",
            kind = "edge", boltGroupId = groupId, partId,
            axis = axis == 0 ? "X" : "Y", direction = axis == 0 ? "horizontal" : "vertical",
            edgeSide = sign < 0 ? "min" : "max",
            state = reason == null ? "Candidate" : "Blocked", reason,
            writeReady = false, sourceIndices = sources.Select(p => p.Index).ToArray(),
            center, edgePoint = edge, distance, contourIndex,
            points = edge == null ? Array.Empty<double[]>() : sign < 0 ? new[] { edge, center } : new[] { center, edge },
            contourSource = "captured full-solid projected outer contour; section clipping is unverified",
            policyStatus = "part-specific proposal; edge-dimension policy and placement are unverified"
        };
    }

    private static string? FindEdge(IReadOnlyList<OutlineTreeNodeResult>? contours, double[] center,
        int axis, int sign, out double[]? edge, out int contourIndex)
    {
        edge = null;
        contourIndex = -1;
        if (contours == null || contours.Count == 0) return "part has no captured projected contour";
        var outer = Flatten(contours).Where(node => !node.IsHole).ToArray();
        if (outer.Length == 0 || outer.Any(node => node.Polygon.Count < 3 || node.Polygon.Any(p =>
                p.Length < 2 || p.Take(2).Any(value => double.IsNaN(value) || double.IsInfinity(value)))))
            return "part outer contour is invalid or incomplete";
        var matches = outer.Select((node, index) => (node, index)).Where(item =>
            PolygonGeometry.ContainsPoint(item.node.Polygon, center[0], center[1])).ToArray();
        if (matches.Length != 1) return "bolt center is outside or belongs to ambiguous projected outer contours";
        contourIndex = matches[0].index;
        var polygon = matches[0].node.Polygon;
        var across = 1 - axis;
        // Avoid choosing a crossing through a vertex or along a boundary from rounded evidence.
        if (polygon.Any(p => Math.Abs(p[across] - center[across]) <= BoltDimensionChainPreview.CoordinateTolerance))
            return "axis ray aligns with a contour vertex; edge crossing requires review";
        var crossings = new List<double>();
        for (var index = 0; index < polygon.Count; index++)
        {
            var a = polygon[index];
            var b = polygon[(index + 1) % polygon.Count];
            if ((a[across] > center[across]) == (b[across] > center[across])) continue;
            var along = a[axis] + (center[across] - a[across]) * (b[axis] - a[axis]) / (b[across] - a[across]);
            if (Math.Abs(along - center[axis]) <= BoltDimensionChainPreview.CoordinateTolerance)
                return "bolt center lies on or too close to the projected part edge";
            if (sign * (along - center[axis]) > BoltDimensionChainPreview.CoordinateTolerance) crossings.Add(along);
        }
        if (crossings.Count == 0) return "no external contour crossing in the requested axis direction";
        var selected = crossings.OrderBy(value => sign * (value - center[axis])).First();
        edge = (double[])center.Clone();
        edge[axis] = selected;
        return null;
    }

    private static IEnumerable<OutlineTreeNodeResult> Flatten(IEnumerable<OutlineTreeNodeResult> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Children)) yield return child;
        }
    }
}