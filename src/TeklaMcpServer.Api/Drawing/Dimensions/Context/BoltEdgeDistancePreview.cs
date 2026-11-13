using TeklaMcpServer.Api.Algorithms.Geometry;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>Axis distance to an actual projected outer contour; never a bbox or convex hull.</summary>
internal static class BoltEdgeDistancePreview
{
    internal static object Build(int groupId, int partId, string rowId, BoltPointGeometry[] sources,
        int axis, int sign, IReadOnlyList<OutlineTreeNodeResult>? contours, bool centersInside,
        double tolerance = DimensionCoordinateSettings.DefaultToleranceMm)
    {
        var center = sources[0].Point;
        var reason = centersInside ? null : "not every source center is inside the view restriction; axial extent is unverified";
        double[]? edge = null;
        var contourIndex = -1;
        if (reason == null)
            reason = FindEdge(contours, center, axis, sign, out edge, out contourIndex, tolerance);
        var distance = edge == null ? (double?)null : Math.Abs(edge[axis] - center[axis]);
        return new {
            proposalId = $"{rowId}-part-{partId}-edge-{(sign < 0 ? "min" : "max")}",
            kind = "edge", boltGroupId = groupId, partId,
            axis = axis == 0 ? "X" : "Y", direction = axis == 0 ? "horizontal" : "vertical",
            edgeSide = sign < 0 ? "min" : "max",
            state = reason == null ? "Candidate" : "Blocked", reason,
            creationMode = "explicitSelectionOnly", sourceIndices = sources.Select(p => p.Index).ToArray(),
            center, edgePoint = edge, distance, contourIndex,
            points = edge == null ? Array.Empty<double[]>() : sign < 0 ? new[] { edge, center } : new[] { center, edge },
            contourSource = "captured full-solid projected outer contour; section clipping is unverified",
            policyStatus = "part-specific proposal; edge-dimension policy and placement are unverified"
        };
    }

    internal static string? FindEdge(IReadOnlyList<OutlineTreeNodeResult>? contours, double[] center,
        int axis, int sign, out double[]? edge, out int contourIndex, double tolerance = DimensionCoordinateSettings.DefaultToleranceMm) =>
        FindEdge(contours, center, axis, sign, out edge, out contourIndex, out _, tolerance);

    /// <summary>The same search, with the kind of its outcome: a refusal is either ambiguous or invalid geometry.</summary>
    internal static string? FindEdge(IReadOnlyList<OutlineTreeNodeResult>? contours, double[] center,
        int axis, int sign, out double[]? edge, out int contourIndex, out BoltEdgeSearch search,
        double tolerance = DimensionCoordinateSettings.DefaultToleranceMm)
    {
        edge = null;
        contourIndex = -1;
        search = BoltEdgeSearch.Invalid;
        if (contours == null || contours.Count == 0) return "part has no captured projected contour";
        var outer = Flatten(contours).Where(node => !node.IsHole).ToArray();
        if (outer.Length == 0 || outer.Any(node => node.Polygon.Count < 3 || node.Polygon.Any(p =>
                p.Length < 2 || p.Take(2).Any(value => double.IsNaN(value) || double.IsInfinity(value)))))
            return "part outer contour is invalid or incomplete";
        if (Owner(outer, center, tolerance) is not { } owner)
            return "bolt center is outside or belongs to ambiguous projected outer contours";
        contourIndex = owner.Index;
        var polygon = owner.Polygon;
        var across = 1 - axis;
        search = BoltEdgeSearch.Ambiguous;
        // Avoid choosing a crossing through a vertex or along a boundary from rounded evidence.
        if (polygon.Any(p => Math.Abs(p[across] - center[across]) <= tolerance))
            return "axis ray aligns with a contour vertex; edge crossing requires review";
        if (owner.State == BoltCenterState.Boundary)
            return "bolt center lies on or too close to the projected part edge";
        var crossings = new List<double>();
        for (var index = 0; index < polygon.Count; index++)
        {
            var a = polygon[index];
            var b = polygon[(index + 1) % polygon.Count];
            if ((a[across] > center[across]) == (b[across] > center[across])) continue;
            var along = a[axis] + (center[across] - a[across]) * (b[axis] - a[axis]) / (b[across] - a[across]);
            if (Math.Abs(along - center[axis]) <= tolerance)
                return "bolt center lies on or too close to the projected part edge";
            if (sign * (along - center[axis]) > tolerance) crossings.Add(along);
        }
        search = BoltEdgeSearch.Invalid;
        if (crossings.Count == 0) return "no external contour crossing in the requested axis direction";
        var selected = crossings.OrderBy(value => sign * (value - center[axis])).First();
        edge = (double[])center.Clone();
        edge[axis] = selected;
        search = BoltEdgeSearch.Found;
        return null;
    }

    /// <summary>
    /// The one outer contour that holds the center: the one it stands inside, or, inside none, the
    /// one whose boundary it lies on. Null when there is none or more than one.
    /// </summary>
    internal static (int Index, IReadOnlyList<double[]> Polygon, BoltCenterState State)? Owner(
        IReadOnlyList<OutlineTreeNodeResult> outer, double[] center, double tolerance)
    {
        var states = outer.Select((node, index) => (Index: index, Polygon: (IReadOnlyList<double[]>)node.Polygon,
            State: Locate(node.Polygon, center, tolerance))).ToArray();
        var inside = states.Where(item => item.State == BoltCenterState.Inside).ToArray();
        var matches = inside.Length > 0 ? inside : states.Where(item => item.State == BoltCenterState.Boundary).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    /// <summary>Within the tolerance of the polygon's line the point is on the boundary, whichever side it fell on.</summary>
    internal static BoltCenterState Locate(IReadOnlyList<double[]> polygon, double[] point, double tolerance)
    {
        for (var index = 0; index < polygon.Count; index++)
            if (DistanceToSegment(point, polygon[index], polygon[(index + 1) % polygon.Count]) <= tolerance)
                return BoltCenterState.Boundary;
        return PolygonGeometry.ContainsPoint(polygon, point[0], point[1]) ? BoltCenterState.Inside : BoltCenterState.Outside;
    }

    internal static double DistanceToSegment(double[] point, double[] a, double[] b)
    {
        var (dx, dy) = (b[0] - a[0], b[1] - a[1]);
        var length = dx * dx + dy * dy;
        var t = length <= 0 ? 0 : Math.Max(0, Math.Min(1, ((point[0] - a[0]) * dx + (point[1] - a[1]) * dy) / length));
        var (x, y) = (a[0] + t * dx - point[0], a[1] + t * dy - point[1]);
        return Math.Sqrt(x * x + y * y);
    }

    internal static IEnumerable<OutlineTreeNodeResult> Flatten(IEnumerable<OutlineTreeNodeResult> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Children)) yield return child;
        }
    }
}

/// <summary>Where a bolt center stands against one projected contour, within the coordinate tolerance.</summary>
internal enum BoltCenterState { Outside, Boundary, Inside }

/// <summary>How the search for the part edge beside a bolt ended.</summary>
internal enum BoltEdgeSearch
{
    /// <summary>One crossing of the contour was found.</summary>
    Found,
    /// <summary>The evidence does not single out a crossing: a vertex at the bolt's height, a center on the edge.</summary>
    Ambiguous,
    /// <summary>No usable contour, or no crossing on that side.</summary>
    Invalid
}
