using static TeklaMcpServer.Api.Drawing.OutlineTreeGeometry;
using static TeklaMcpServer.Api.Drawing.BoltChainGeometry;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>Places a chain outside a part or beside its bolts and clears geometric obstacles.
/// It does not decide which candidate will be created or which annotation row it occupies.</summary>
internal static class BoltChainPlacement
{
    internal sealed class LinePlacement(DimensionChainSide side, int sign, DimensionDirection normal, string? refusal,
        Func<double[][], double> offset)
    {
        public DimensionChainSide Side { get; } = side;
        /// <summary>Outward along the axis across the chain: +1 toward Top or Right.</summary>
        public int Sign { get; } = sign;
        public DimensionDirection Normal { get; } = normal;
        public string? Refusal { get; } = refusal;
        /// <summary>From the chain's first point to the bound the line stands one gap beyond.</summary>
        public Func<double[][], double> Offset { get; } = offset;
    }

    internal sealed class BoltChainSide(LinePlacement placement, string key, double[][] points, string? refusal)
    {
        public LinePlacement Placement { get; } = placement;
        public string Key { get; } = key;
        public double[][] Points { get; } = points;
        public string? Refusal { get; } = refusal;
    }

    // The line stands beside the nearer bound of the part, outside it, and beyond every other part
    // it would otherwise cross or come within one gap of along the chain's span (a part only touching
    // an end of the span is not crossed): an end plate
    // standing past the end of a beam pushes the line of the beam's bolts out with it.
    internal static LinePlacement Placement(IReadOnlyDictionary<int, IReadOnlyList<OutlineTreeNodeResult>>? contours,
        int partId, int axis, double center, double gap, double touch, int? outward = null, bool skipUnder = true)
    {
        var across = 1 - axis;
        var bounds = Bounds(contours, partId);
        var sign = outward ?? (bounds is { } b && b.Max[across] - center > center - b.Min[across] ? -1 : 1);
        var side = axis == 0 ? (sign > 0 ? DimensionChainSide.Top : DimensionChainSide.Bottom)
            : (sign > 0 ? DimensionChainSide.Right : DimensionChainSide.Left);
        var others = Others(contours, partId);
        double Clear(double[][] points)
        {
            var box = bounds!.Value;
            var bound = ClearPast(sign > 0 ? box.Max[across] : box.Min[across], sign, axis, points, others, gap, touch, skipUnder);
            return Math.Max(0, sign * (bound - points[0][across]));
        }
        return new LinePlacement(side, sign, new DimensionDirection(axis == 0 ? 0 : sign, axis == 0 ? sign : 0),
            bounds == null ? "part has no captured contour to place the line beside" : null,
            points => bounds == null ? 0 : Clear(points));
    }

    private static (double[] Min, double[] Max)[] Others(IReadOnlyDictionary<int, IReadOnlyList<OutlineTreeNodeResult>>? contours,
        int partId) => contours == null ? [] : contours.Keys.Where(id => id != partId)
            .Select(id => Bounds(contours, id)).Where(box => box != null).Select(box => box!.Value).ToArray();

    // The bound the line stands one gap beyond, moved outward past every other part in the way along
    // the chain's span: the near side of a part in the way stands less than one gap past the bound,
    // and its far side past it; the bound moves to that far side until nothing is in the way.
    private static double ClearPast(double start, int sign, int axis, double[][] points,
        (double[] Min, double[] Max)[] others, double gap, double touch, bool skipUnder = true)
    {
        var across = 1 - axis;
        var (from, to) = (points.Min(point => point[axis]), points.Max(point => point[axis]));
        var bound = start;
        // Under the chain or beside it is judged against the starting bound, not the moved one.
        var own = sign * start;
        for (var moved = true; moved;)
        {
            moved = false;
            foreach (var other in others.Where(other => other.Min[axis] < to - touch && other.Max[axis] > from + touch))
            {
                var (near, far) = sign > 0 ? (other.Min[across], other.Max[across]) : (-other.Max[across], -other.Min[across]);
                // A part starting on the line itself is in the way too: the line would lie on its edge.
                // Past the part, a part reaching back inside the bound lies under the chain (the beam
                // under a fin plate), not beside it: the line cannot clear it and must not try.
                if (sign * bound >= far || near > sign * bound + gap + touch || skipUnder && near < own - touch) continue;
                bound = sign * far;
                moved = true;
            }
        }
        return bound;
    }

    // A part reaching far past its group on both sides (a beam 4720 long, a group 1060 from its end)
    // gets the line beside the group, one gap past its outer bolt, not past the part's end: a dimension
    // far from its bolts reads as if they had none (M.298). The end points move across to the group, along edges
    // that run straight past it; where an edge does not, the line stays past the part.
    internal static BoltChainSide Beside(
        IReadOnlyDictionary<int, IReadOnlyList<OutlineTreeNodeResult>>? contours, int partId, int axis,
        BoltChainSide side,
        double[][] bolts, double gap, double tolerance)
    {
        var placement = side.Placement;
        var key = side.Key;
        if (placement.Refusal != null || side.Refusal != null || side.Points.Length < 2 || bolts.Length == 0
            || Bounds(contours, partId) is not { } box) return side;
        var across = 1 - axis;
        var sign = placement.Sign;
        var outer = sign > 0 ? bolts.Max(point => point[across]) : bolts.Min(point => point[across]);
        // Only a group standing in the middle of its part: the part reaches more than one gap past
        // it on both sides. A group at an end of the part keeps its line past that end.
        if (box.Max[across] - bolts.Max(point => point[across]) <= gap + tolerance
            || bolts.Min(point => point[across]) - box.Min[across] <= gap + tolerance) return side;
        var edges = Edges(contours![partId]);
        bool IsBolt(double[] point) => bolts.Any(bolt => Math.Abs(bolt[0] - point[0]) <= tolerance && Math.Abs(bolt[1] - point[1]) <= tolerance);
        bool EdgeAt(double[] point) => edges.Any(edge => Math.Abs(edge.A[axis] - point[axis]) <= tolerance
            && Math.Abs(edge.B[axis] - point[axis]) <= tolerance
            && Math.Min(edge.A[across], edge.B[across]) <= outer + tolerance && Math.Max(edge.A[across], edge.B[across]) >= outer - tolerance);
        if (side.Points.Any(point => !IsBolt(point) && !EdgeAt(point))) return side;
        var moved = side.Points.Select(point => IsBolt(point) ? point
            : axis == 0 ? new[] { point[0], outer } : new[] { outer, point[1] }).ToArray();
        // Beside the group the line clears the parts in the way as it does past the part (a plate
        // standing next to the group), and a part reaching over the group too: inside the part there
        // is room past it, and a line left there would cross it.
        var others = Others(contours, partId);
        return new BoltChainSide(new LinePlacement(placement.Side, sign, placement.Normal, null,
            points => Math.Max(0, sign * (ClearPast(outer, sign, axis, points, others, gap, tolerance, skipUnder: false) - points[0][across]))),
            key, moved, null);
    }

}
