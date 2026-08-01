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
    /// Rebuilds the point list and both length lists so they describe the dimension the way the
    /// sheet does.
    ///
    /// A dimension measures along its reference line: every snap point is projected onto that
    /// line along the normal, and the values printed are distances from the line's start. So the
    /// same rule is applied here — order the points by their projection, then measure along it.
    ///
    /// Verified against an exported PDF, all 26 printed values across four chains matched: a span
    /// previously reported as 2555.25 prints as 2546, 456.91 as 448, 298.53 as 301. The old
    /// formula measured straight-line distance from whichever point happened to be first in the
    /// array, which agreed with the sheet only when the points were collinear. It also invented
    /// fractions that were then read as snap drift, and produced negative segments — impossible
    /// for a dimension — whenever the array order ran against the axis.
    ///
    /// PointList itself is left in the order the points arrive in. Sorting it by projection also
    /// makes the reading direction match the sheet, but it swaps StartX/StartY with EndX/EndY on
    /// vertical chains, and grouping, dedup and arrangement all read those — not worth the risk
    /// for what is a cosmetic property of the output.
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

        var axis = ResolveMeasurementAxis();

        if (axis.HasValue)
        {
            // Values are the distances along the reference line, taken from its near end. Sorting
            // the projections rather than the points is deliberate: PointList order feeds grouping,
            // dedup and arrangement, and reordering it swapped the start and end of vertical chains.
            // The printed numbers do not depend on the order the points arrive in anyway.
            var (originX, originY, unitX, unitY) = axis.Value;

            var offsets = PointList
                .Select(point => ((point.X - originX) * unitX) + ((point.Y - originY) * unitY))
                .OrderBy(static offset => offset)
                .ToList();

            var origin = offsets[0];
            for (var i = 1; i < offsets.Count; i++)
                LengthList.Add(System.Math.Round(offsets[i] - origin, 2));
        }

        for (var i = 1; i < PointList.Count; i++)
        {
            var dx = PointList[i].X - StartX;
            var dy = PointList[i].Y - StartY;
            RealLengthList.Add(System.Math.Round(System.Math.Sqrt((dx * dx) + (dy * dy)), 2));
        }

        if (!axis.HasValue)
            LengthList.AddRange(RealLengthList);
    }

    /// <summary>
    /// Origin and unit vector to measure along: the reference line when it is known, otherwise the
    /// dimension direction anchored at the first point. Null when neither is available, in which
    /// case the caller falls back to straight-line distances rather than reporting zeros.
    /// </summary>
    private (double OriginX, double OriginY, double UnitX, double UnitY)? ResolveMeasurementAxis()
    {
        var line = ReferenceLine;
        if (line != null)
        {
            var dx = line.EndX - line.StartX;
            var dy = line.EndY - line.StartY;
            var length = System.Math.Sqrt((dx * dx) + (dy * dy));
            if (length > 1e-9)
                return (line.StartX, line.StartY, dx / length, dy / length);
        }

        var direction = Direction;
        return direction.HasValue
            ? (StartX, StartY, direction.Value.X, direction.Value.Y)
            : null;
    }
}
