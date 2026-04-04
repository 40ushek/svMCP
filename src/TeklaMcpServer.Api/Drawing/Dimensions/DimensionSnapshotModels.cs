using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class DimensionSourceReference
{
    public DimensionSourceKind SourceKind { get; set; }
    public int? DrawingObjectId { get; set; }
    public int? ModelId { get; set; }
}

internal sealed class TeklaDimensionSegmentSnapshot
{
    public int Id { get; set; }
    public double StartX { get; set; }
    public double StartY { get; set; }
    public double EndX { get; set; }
    public double EndY { get; set; }
    public double Distance { get; set; }
    public double DirectionX { get; set; }
    public double DirectionY { get; set; }
    public int TopDirection { get; set; }
    public DrawingBoundsInfo? Bounds { get; set; }
    public DrawingBoundsInfo? TextBounds { get; set; }
    public DrawingLineInfo? DimensionLine { get; set; }
    public DrawingLineInfo? LeadLineMain { get; set; }
    public DrawingLineInfo? LeadLineSecond { get; set; }
}

internal sealed class TeklaDimensionSetSnapshot
{
    public int Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string TeklaDimensionType { get; set; } = string.Empty;
    public int? ViewId { get; set; }
    public string ViewType { get; set; } = string.Empty;
    public double ViewScale { get; set; }
    public string Orientation { get; set; } = string.Empty;
    public double Distance { get; set; }
    public double DirectionX { get; set; }
    public double DirectionY { get; set; }
    public int TopDirection { get; set; }
    public DrawingBoundsInfo? Bounds { get; set; }
    public DrawingLineInfo? ReferenceLine { get; set; }
    public List<DrawingPointInfo> MeasuredPoints { get; } = [];
    public List<TeklaDimensionSegmentSnapshot> Segments { get; } = [];
    public DimensionSourceKind SourceKind { get; set; }
    public DimensionGeometryKind GeometryKind { get; set; }
    public DimensionType ClassifiedDimensionType { get; set; }
    public List<DimensionSourceReference> SourceReferences { get; } = [];
}
