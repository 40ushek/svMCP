using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

internal class DimensionItem
{
    public int DimensionId { get; set; }
    public int SegmentId => SegmentIds.Count > 0 ? SegmentIds[0] : 0;
    public List<int> SegmentIds { get; } = [];
    public int? ViewId { get; set; }
    public string ViewType { get; set; } = string.Empty;
    public double ViewScale { get; set; }
    public DimensionType DomainDimensionType { get; set; }
    public string DimensionType => DomainDimensionType.ToString();
    public DimensionSourceKind SourceKind { get; set; }
    public DimensionGeometryKind GeometryKind { get; set; }
    public string TeklaDimensionType { get; set; } = string.Empty;
    public double StartX { get; set; }
    public double StartY { get; set; }
    public double EndX { get; set; }
    public double EndY { get; set; }
    public double CenterX { get; set; }
    public double CenterY { get; set; }
    public int StartPointOrder { get; set; } = -1;
    public int EndPointOrder { get; set; } = -1;
    public double Distance { get; set; }
    public double SortKey { get; set; }
    public double DirectionX { get; set; }
    public double DirectionY { get; set; }
    public int TopDirection { get; set; }
    public DrawingBoundsInfo? Bounds { get; set; }
    public DrawingLineInfo? ReferenceLine { get; set; }
    public DrawingLineInfo? LeadLineMain { get; set; }
    public DrawingLineInfo? LeadLineSecond { get; set; }
    public string Orientation { get; set; } = string.Empty;
    public List<DrawingPointInfo> PointList { get; } = [];
    public List<double> LengthList { get; } = [];
    public List<double> RealLengthList { get; } = [];
    public List<DrawingPointInfo> MeasuredPoints { get; } = [];
    public List<DimensionSegmentInfo> Segments { get; } = [];
    public List<DimensionSourceReference> SourceReferences { get; } = [];

    public (double X, double Y)? Direction =>
        TeklaDrawingDimensionsApi.TryNormalizeDirection(DirectionX, DirectionY, out var direction)
            ? direction
            : null;

    public DrawingPointInfo StartPoint => new() { X = StartX, Y = StartY, Order = StartPointOrder };
    public DrawingPointInfo EndPoint => new() { X = EndX, Y = EndY, Order = EndPointOrder };
    public DrawingPointInfo CenterPoint => new() { X = CenterX, Y = CenterY, Order = -1 };

    public double GetLeadLineMainLength() => LeadLineMain?.Length ?? 0;

    public double GetLeadLineSecondLength() => LeadLineSecond?.Length ?? 0;

    /// <summary>
    /// Rebuilds the point list and both length lists so they describe the projected dimension
    /// geometry used by grouping and reduction.
    ///
    /// A dimension measures along its reference line: every snap point is projected onto that
    /// line along the normal. Projecting is what these lists now do too.
    ///
    /// Verified against an exported PDF, all 26 printed values across four chains matched: a span
    /// previously reported as 2555.25 prints as 2546, 456.91 as 448, 298.53 as 301. The old
    /// formula measured straight-line distance from whichever point happened to be first in the
    /// array, which agreed with the sheet only when the points were collinear. It also invented
    /// fractions that were then read as snap drift. Point order and printed-run order are separate
    /// concepts; this method does not claim that Tekla's incoming order is geometric order.
    ///
    /// Measurement runs from PointList[0], NOT from the near end of the reference line, even
    /// though the sheet prints the latter. Both lists have to stay index-aligned with PointList,
    /// and taking the values in position order breaks that: DimensionOperations pairs
    /// LengthList[i] with PointList[i + 1] and would then match a length against the wrong point
    /// while reducing packets. The printed absolute run needs its own field, not this one.
    /// Point.Order is preserved as the supplied point/mapping identity; it is not reinterpreted as
    /// a projection sort key here.
    ///
    /// RealLengthList keeps the straight-line distance between points on purpose. It is the
    /// honest point-to-point value, and DimensionOperations matches packets against LengthList
    /// with a tolerance, so the two must not collapse into one.
    /// </summary>
    public void ReplacePointList(IEnumerable<DrawingPointInfo> points)
    {
        PointList.Clear();
        LengthList.Clear();
        RealLengthList.Clear();

        PointList.AddRange(points.OrderBy(static point => point.Order));
        if (PointList.Count == 0)
            return;

        StartPointOrder = PointList[0].Order;
        EndPointOrder = PointList[PointList.Count - 1].Order;
        StartX = PointList[0].X;
        StartY = PointList[0].Y;
        EndX = PointList[PointList.Count - 1].X;
        EndY = PointList[PointList.Count - 1].Y;
        CenterX = System.Math.Round((StartX + EndX) / 2.0, 3);
        CenterY = System.Math.Round((StartY + EndY) / 2.0, 3);

        // Both lists stay index-aligned with PointList: entry i describes PointList[i + 1].
        // DimensionOperations relies on that — GetLengthMatchedPointIndices turns a length index
        // into a point index by adding one, and takes LengthList[0] as the first span. Sorting the
        // values by position along the line breaks the pairing and mismatches lengths to points
        // during packet reduction, so measurement runs from PointList[0] in point order.
        var axis = ResolveProjectionAxis();

        for (var i = 1; i < PointList.Count; i++)
        {
            var dx = PointList[i].X - StartX;
            var dy = PointList[i].Y - StartY;

            var real = System.Math.Round(System.Math.Sqrt((dx * dx) + (dy * dy)), 2);

            LengthList.Add(axis.HasValue
                ? System.Math.Round(System.Math.Abs((dx * axis.Value.X) + (dy * axis.Value.Y)), 2)
                : real);
            RealLengthList.Add(real);
        }
    }

    /// <summary>
    /// Unit vector to project onto: taken from the reference line when it is known, since that is
    /// the line the dimension actually measures along, and from the dimension direction otherwise.
    /// Null when neither is available, in which case the caller keeps straight-line distances
    /// rather than reporting zeros.
    /// </summary>
    private (double X, double Y)? ResolveProjectionAxis()
    {
        var line = ReferenceLine;
        if (line != null)
        {
            var dx = line.EndX - line.StartX;
            var dy = line.EndY - line.StartY;
            var length = System.Math.Sqrt((dx * dx) + (dy * dy));
            if (length > 1e-9)
                return (dx / length, dy / length);
        }

        return Direction;
    }
}
