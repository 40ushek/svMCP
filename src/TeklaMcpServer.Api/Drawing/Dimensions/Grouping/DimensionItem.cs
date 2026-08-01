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

    public void ReplacePointList(IEnumerable<DrawingPointInfo> points)
    {
        PointList.Clear();
        PointList.AddRange(points.OrderBy(static point => point.Order));

        LengthList.Clear();
        RealLengthList.Clear();
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

        // LengthList must match what the sheet prints, and a dimension prints the PROJECTION of
        // the span onto its own axis — not the straight-line distance between the snap points.
        // The two only agree when the points are collinear, which is why overall dimensions and
        // control diagonals looked fine while chains reading off inset parts did not.
        //
        // Measured against an exported PDF: a span our old formula reported as 2555.25 is printed
        // as 2546 (the projection is 2545.5); 456.91 is printed as 448; 298.53 as 301. Taking the
        // hypotenuse also invented fractional values that were then mistaken for snap drift, and
        // produced negative segments — which a dimension cannot have — once the points were not
        // ordered along the axis.
        //
        // RealLengthList keeps the straight-line distance: it is the honest point-to-point value
        // and stays useful for geometry work.
        var axis = Direction;

        for (var i = 1; i < PointList.Count; i++)
        {
            var dx = PointList[i].X - StartX;
            var dy = PointList[i].Y - StartY;

            var real = System.Math.Round(System.Math.Sqrt((dx * dx) + (dy * dy)), 2);

            var projected = axis.HasValue
                ? System.Math.Round(System.Math.Abs((dx * axis.Value.X) + (dy * axis.Value.Y)), 2)
                : real;

            LengthList.Add(projected);
            RealLengthList.Add(real);
        }
    }
}
