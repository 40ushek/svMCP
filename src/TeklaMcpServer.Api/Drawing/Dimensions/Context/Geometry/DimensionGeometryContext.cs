using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class DimensionGeometryContext
{
    public DrawingLineInfo? ReferenceLine { get; set; }
    public DrawingPointInfo? DimensionLineStart { get; set; }
    public DrawingPointInfo? DimensionLineEnd { get; set; }
    public DrawingVectorInfo? LineDirection { get; set; }
    public DrawingVectorInfo? NormalDirection { get; set; }
    public double Distance { get; set; }
    public List<DrawingPointInfo> MeasuredPoints { get; } = [];
    public List<DimensionSegmentGeometry> SegmentGeometries { get; } = [];
    public double? StartAlong { get; set; }
    public double? EndAlong { get; set; }
    public DrawingBoundsInfo? TextBounds { get; set; }
    public DimensionGeometryBand? LocalBand { get; set; }
    public List<string> Warnings { get; } = [];

    public bool HasTextBounds => TextBounds != null;
}

internal sealed class DimensionSegmentGeometry
{
    public int SegmentId { get; set; }
    public DrawingLineInfo? DimensionLine { get; set; }
    public DrawingLineInfo? LeadLineMain { get; set; }
    public DrawingLineInfo? LeadLineSecond { get; set; }
    public DrawingBoundsInfo? TextBounds { get; set; }
    public DrawingPointInfo StartPoint { get; set; } = new();
    public DrawingPointInfo EndPoint { get; set; } = new();
}

internal sealed class DimensionGeometryBand
{
    public double StartAlong { get; set; }
    public double EndAlong { get; set; }
    public double MinOffset { get; set; }
    public double MaxOffset { get; set; }
}
