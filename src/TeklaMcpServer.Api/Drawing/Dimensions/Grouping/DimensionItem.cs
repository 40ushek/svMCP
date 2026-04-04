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
    public List<int> SourceObjectIds { get; } = [];

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

        for (var i = 1; i < PointList.Count; i++)
        {
            var length = System.Math.Round(
                System.Math.Sqrt(
                    System.Math.Pow(PointList[i].X - StartX, 2) +
                    System.Math.Pow(PointList[i].Y - StartY, 2)),
                2);
            LengthList.Add(length);
            RealLengthList.Add(length);
        }
    }
}
