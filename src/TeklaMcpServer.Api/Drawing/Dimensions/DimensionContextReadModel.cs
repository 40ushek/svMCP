using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

public sealed class DimensionGeometryBandInfo
{
    public double StartAlong { get; set; }
    public double EndAlong { get; set; }
    public double MinOffset { get; set; }
    public double MaxOffset { get; set; }
}

public sealed class DimensionContextRelatedSourceInfo
{
    public string Owner { get; set; } = string.Empty;
    public int? DrawingObjectId { get; set; }
    public int? ModelId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string SourceKind { get; set; } = string.Empty;
    public bool HasGeometry { get; set; }
    public DrawingBoundsInfo? GeometryBounds { get; set; }
}

public sealed class DimensionContextPointAssociationInfo
{
    public int Order { get; set; }
    public string Status { get; set; } = string.Empty;
    public string MatchedOwner { get; set; } = string.Empty;
    public int? MatchedDrawingObjectId { get; set; }
    public int? MatchedModelId { get; set; }
    public string MatchedType { get; set; } = string.Empty;
    public string MatchedSourceKind { get; set; } = string.Empty;
    public double? DistanceToGeometry { get; set; }
}

public sealed class DimensionContextInfo
{
    public int DimensionId { get; set; }
    public List<int> SegmentIds { get; set; } = new();
    public int? ViewId { get; set; }
    public string ViewType { get; set; } = string.Empty;
    public double ViewScale { get; set; }
    public string DimensionType { get; set; } = string.Empty;
    public string TeklaDimensionType { get; set; } = string.Empty;
    public string Orientation { get; set; } = string.Empty;
    public string SourceKind { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public DrawingLineInfo? ReferenceLine { get; set; }
    public DrawingLineInfo? LeadLineMain { get; set; }
    public DrawingLineInfo? LeadLineSecond { get; set; }
    public List<DrawingPointInfo> PointList { get; set; } = new();
    public List<double> LengthList { get; set; } = new();
    public List<double> RealLengthList { get; set; } = new();
    public double Distance { get; set; }
    public DrawingBoundsInfo? LocalBounds { get; set; }
    public bool HasSourceGeometry { get; set; }
    public List<string> GeometryWarnings { get; set; } = new();
    public List<int> SnapshotSourceDrawingObjectIds { get; set; } = new();
    public List<int> SnapshotSourceModelIds { get; set; } = new();
    public List<int> SourceDrawingObjectIds { get; set; } = new();
    public List<int> SourceModelIds { get; set; } = new();
    public DrawingVectorInfo? AnnotationLineDirection { get; set; }
    public DrawingVectorInfo? AnnotationNormalDirection { get; set; }
    public double? AnnotationStartAlong { get; set; }
    public double? AnnotationEndAlong { get; set; }
    public DimensionGeometryBandInfo? AnnotationBand { get; set; }
    public int AnnotationSegmentGeometryCount { get; set; }
    public bool AnnotationHasTextBounds { get; set; }
    public DrawingBoundsInfo? AnnotationTextBounds { get; set; }
    public List<string> AnnotationGeometryWarnings { get; set; } = new();
    public List<DrawingPointInfo> MeasuredPoints { get; set; } = new();
    public List<DimensionContextRelatedSourceInfo> RelatedSources { get; set; } = new();
    public List<DimensionContextPointAssociationInfo> PointAssociations { get; set; } = new();
    public List<string> AssociationWarnings { get; set; } = new();
    public int RelatedSourceCount { get; set; }
    public int AssociationMatchedCount { get; set; }
    public int AssociationAmbiguousCount { get; set; }
    public int AssociationNoGeometryCount { get; set; }
    public int AssociationNoCandidatesCount { get; set; }
}

public sealed class GetDimensionContextsResult
{
    public int? ViewId { get; set; }
    public int Total { get; set; }
    public List<string> Warnings { get; set; } = new();
    public List<DimensionContextInfo> Dimensions { get; set; } = new();
}

internal static class DimensionContextReadModelMapper
{
    public static GetDimensionContextsResult ToResult(
        int? viewId,
        IReadOnlyList<DimensionContext> contexts,
        IReadOnlyList<string> warnings)
    {
        return new GetDimensionContextsResult
        {
            ViewId = viewId,
            Total = contexts.Count,
            Warnings = warnings.Distinct().ToList(),
            Dimensions = contexts.Select(ToInfo).ToList()
        };
    }

    private static DimensionContextInfo ToInfo(DimensionContext context)
    {
        return new DimensionContextInfo
        {
            DimensionId = context.DimensionId,
            SegmentIds = context.Item.SegmentIds.ToList(),
            ViewId = context.ViewId,
            ViewType = context.ViewType,
            ViewScale = context.ViewScale,
            DimensionType = context.Item.DimensionType,
            TeklaDimensionType = context.Item.TeklaDimensionType,
            Orientation = context.Item.Orientation,
            SourceKind = context.SourceKind.ToString(),
            Role = context.Role.ToString(),
            ReferenceLine = CopyLine(context.ReferenceLine),
            LeadLineMain = CopyLine(context.LeadLineMain),
            LeadLineSecond = CopyLine(context.LeadLineSecond),
            PointList = context.PointList.Select(CopyPoint).ToList(),
            LengthList = context.LengthList.ToList(),
            RealLengthList = context.RealLengthList.ToList(),
            Distance = context.Distance,
            LocalBounds = CopyBounds(context.LocalBounds),
            HasSourceGeometry = context.HasSourceGeometry,
            GeometryWarnings = context.GeometryWarnings.ToList(),
            SnapshotSourceDrawingObjectIds = context.SnapshotSourceDrawingObjectIds.ToList(),
            SnapshotSourceModelIds = context.SnapshotSourceModelIds.ToList(),
            SourceDrawingObjectIds = context.SourceDrawingObjectIds.ToList(),
            SourceModelIds = context.SourceModelIds.ToList(),
            AnnotationLineDirection = CopyVector(context.AnnotationLineDirection),
            AnnotationNormalDirection = CopyVector(context.AnnotationNormalDirection),
            AnnotationStartAlong = context.AnnotationStartAlong,
            AnnotationEndAlong = context.AnnotationEndAlong,
            AnnotationBand = context.AnnotationGeometry.LocalBand == null
                ? null
                : new DimensionGeometryBandInfo
                {
                    StartAlong = context.AnnotationGeometry.LocalBand.StartAlong,
                    EndAlong = context.AnnotationGeometry.LocalBand.EndAlong,
                    MinOffset = context.AnnotationGeometry.LocalBand.MinOffset,
                    MaxOffset = context.AnnotationGeometry.LocalBand.MaxOffset
                },
            AnnotationSegmentGeometryCount = context.AnnotationSegmentGeometryCount,
            AnnotationHasTextBounds = context.AnnotationHasTextBounds,
            AnnotationTextBounds = CopyBounds(context.AnnotationTextBounds),
            AnnotationGeometryWarnings = context.AnnotationGeometryWarnings.ToList(),
            MeasuredPoints = context.MeasuredPoints.Select(CopyPoint).ToList(),
            RelatedSources = context.RelatedSources.Select(relatedSource => new DimensionContextRelatedSourceInfo
            {
                Owner = relatedSource.Owner,
                DrawingObjectId = relatedSource.DrawingObjectId,
                ModelId = relatedSource.ModelId,
                Type = relatedSource.Type,
                SourceKind = relatedSource.SourceKind,
                HasGeometry = relatedSource.HasGeometry,
                GeometryBounds = CopyBounds(relatedSource.GeometryBounds)
            }).ToList(),
            PointAssociations = context.PointAssociations.Select(pointAssociation => new DimensionContextPointAssociationInfo
            {
                Order = pointAssociation.Order,
                Status = pointAssociation.Status.ToString(),
                MatchedOwner = pointAssociation.MatchedOwner,
                MatchedDrawingObjectId = pointAssociation.MatchedDrawingObjectId,
                MatchedModelId = pointAssociation.MatchedModelId,
                MatchedType = pointAssociation.MatchedType,
                MatchedSourceKind = pointAssociation.MatchedSourceKind,
                DistanceToGeometry = pointAssociation.DistanceToGeometry
            }).ToList(),
            AssociationWarnings = context.AssociationWarnings.ToList(),
            RelatedSourceCount = context.RelatedSourceCount,
            AssociationMatchedCount = context.AssociationMatchedCount,
            AssociationAmbiguousCount = context.AssociationAmbiguousCount,
            AssociationNoGeometryCount = context.AssociationNoGeometryCount,
            AssociationNoCandidatesCount = context.AssociationNoCandidatesCount
        };
    }

    private static DrawingPointInfo CopyPoint(DrawingPointInfo point) => new()
    {
        X = point.X,
        Y = point.Y,
        Order = point.Order
    };

    private static DrawingLineInfo? CopyLine(DrawingLineInfo? line)
    {
        return line == null
            ? null
            : new DrawingLineInfo
            {
                StartX = line.StartX,
                StartY = line.StartY,
                EndX = line.EndX,
                EndY = line.EndY
            };
    }

    private static DrawingBoundsInfo? CopyBounds(DrawingBoundsInfo? bounds)
    {
        return bounds == null
            ? null
            : new DrawingBoundsInfo
            {
                MinX = bounds.MinX,
                MinY = bounds.MinY,
                MaxX = bounds.MaxX,
                MaxY = bounds.MaxY
            };
    }

    private static DrawingVectorInfo? CopyVector(DrawingVectorInfo? vector)
    {
        return vector == null
            ? null
            : new DrawingVectorInfo
            {
                X = vector.X,
                Y = vector.Y
            };
    }
}
