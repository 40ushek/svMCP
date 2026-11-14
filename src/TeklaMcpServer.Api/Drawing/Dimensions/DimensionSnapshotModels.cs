using System.Collections.Generic;
using System.Linq;

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

/// <summary>Compatibility input adapter; grouping always uses the production snapshot contract.</summary>
internal static class DimensionSnapshotProjection
{
    internal static TeklaDimensionSetSnapshot FromReadModel(DrawingDimensionInfo source)
    {
        var snapshot = new TeklaDimensionSetSnapshot {
            Id = source.Id, Type = source.Type, TeklaDimensionType = source.DimensionType,
            ViewId = source.ViewId, ViewType = source.ViewType, ViewScale = source.ViewScale,
            Orientation = source.Orientation, Distance = source.Distance,
            DirectionX = source.DirectionX, DirectionY = source.DirectionY, TopDirection = source.TopDirection,
            Bounds = Copy(source.Bounds), ReferenceLine = Copy(source.ReferenceLine),
            SourceKind = source.SourceKind, GeometryKind = source.GeometryKind,
            ClassifiedDimensionType = source.ClassifiedDimensionType
        };
        snapshot.MeasuredPoints.AddRange(source.MeasuredPoints.Select(point => new DrawingPointInfo {
            X = point.X, Y = point.Y, Order = point.Order
        }));
        snapshot.Segments.AddRange(source.Segments.Select(FromReadModel));
        snapshot.SourceReferences.AddRange(source.SourceReferences.Select(reference => new DimensionSourceReference {
            SourceKind = reference.SourceKind, DrawingObjectId = reference.DrawingObjectId, ModelId = reference.ModelId
        }));
        return snapshot;
    }

    internal static TeklaDimensionSegmentSnapshot FromReadModel(DimensionSegmentInfo source) => new() {
        Id = source.Id, StartX = source.StartX, StartY = source.StartY, EndX = source.EndX, EndY = source.EndY,
        Distance = source.Distance, DirectionX = source.DirectionX, DirectionY = source.DirectionY,
        TopDirection = source.TopDirection, Bounds = Copy(source.Bounds), TextBounds = Copy(source.TextBounds),
        DimensionLine = Copy(source.DimensionLine), LeadLineMain = Copy(source.LeadLineMain), LeadLineSecond = Copy(source.LeadLineSecond)
    };

    private static DrawingBoundsInfo? Copy(DrawingBoundsInfo? source) => source == null ? null : new() {
        MinX = source.MinX, MinY = source.MinY, MaxX = source.MaxX, MaxY = source.MaxY
    };
    private static DrawingLineInfo? Copy(DrawingLineInfo? source) => source == null ? null : new() {
        StartX = source.StartX, StartY = source.StartY, EndX = source.EndX, EndY = source.EndY
    };
}
