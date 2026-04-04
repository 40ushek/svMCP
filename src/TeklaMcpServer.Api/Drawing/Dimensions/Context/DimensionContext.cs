using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class DimensionContext
{
    public int DimensionId { get; set; }
    public DimensionItem Item { get; set; } = new();
    public int? ViewId { get; set; }
    public string ViewType { get; set; } = string.Empty;
    public double ViewScale { get; set; }
    public DimensionSourceKind SourceKind { get; set; }
    public DimensionContextRole Role { get; set; }
    public DimensionContextSourceSummary Source { get; set; } = new();
    public DimensionContextGeometry Geometry { get; set; } = new();
    public DimensionGeometryContext AnnotationGeometry { get; set; } = new();
    public DimensionContextSourceAssociation Association { get; set; } = new();

    public DrawingLineInfo? ReferenceLine => Geometry.ReferenceLine;
    public DrawingLineInfo? LeadLineMain => Geometry.LeadLineMain;
    public DrawingLineInfo? LeadLineSecond => Geometry.LeadLineSecond;
    public IReadOnlyList<DrawingPointInfo> PointList => Geometry.PointList;
    public IReadOnlyList<double> LengthList => Geometry.LengthList;
    public IReadOnlyList<double> RealLengthList => Geometry.RealLengthList;
    public double Distance => Geometry.Distance;
    public bool HasSourceGeometry => Geometry.LocalBounds != null;
    public IReadOnlyList<int> SourceDrawingObjectIds => Source.SourceDrawingObjectIds;
    public IReadOnlyList<int> SourceModelIds => Source.SourceModelIds;
    public DrawingBoundsInfo? LocalBounds => Geometry.LocalBounds;
    public IReadOnlyList<string> GeometryWarnings => Geometry.Warnings;
    public DrawingVectorInfo? AnnotationLineDirection => AnnotationGeometry.LineDirection;
    public DrawingVectorInfo? AnnotationNormalDirection => AnnotationGeometry.NormalDirection;
    public double? AnnotationStartAlong => AnnotationGeometry.StartAlong;
    public double? AnnotationEndAlong => AnnotationGeometry.EndAlong;
    public double? AnnotationBandStartAlong => AnnotationGeometry.LocalBand?.StartAlong;
    public double? AnnotationBandEndAlong => AnnotationGeometry.LocalBand?.EndAlong;
    public double? AnnotationBandMinOffset => AnnotationGeometry.LocalBand?.MinOffset;
    public double? AnnotationBandMaxOffset => AnnotationGeometry.LocalBand?.MaxOffset;
    public int AnnotationSegmentGeometryCount => AnnotationGeometry.SegmentGeometries.Count;
    public bool AnnotationHasTextBounds => AnnotationGeometry.HasTextBounds;
    public DrawingBoundsInfo? AnnotationTextBounds => AnnotationGeometry.TextBounds;
    public IReadOnlyList<string> AnnotationGeometryWarnings => AnnotationGeometry.Warnings;
    public IReadOnlyList<DrawingPointInfo> MeasuredPoints => Association.MeasuredPoints;
    public IReadOnlyList<DimensionContextRelatedSource> RelatedSources => Association.RelatedSources;
    public IReadOnlyList<DimensionContextPointAssociation> PointAssociations => Association.PointAssociations;
    public IReadOnlyList<string> AssociationWarnings => Association.Warnings;
    public int RelatedSourceCount => Association.RelatedSources.Count;
    public int AssociationMatchedCount => Association.PointAssociations.Count(static association => association.Status == DimensionPointObjectMappingStatus.Matched);
    public int AssociationAmbiguousCount => Association.PointAssociations.Count(static association => association.Status == DimensionPointObjectMappingStatus.Ambiguous);
    public int AssociationNoGeometryCount => Association.PointAssociations.Count(static association => association.Status == DimensionPointObjectMappingStatus.NoGeometry);
    public int AssociationNoCandidatesCount => Association.PointAssociations.Count(static association => association.Status == DimensionPointObjectMappingStatus.NoCandidates);
}

internal sealed class DimensionContextSourceSummary
{
    public DimensionSourceKind SourceKind { get; set; }
    public List<int> SourceDrawingObjectIds { get; } = [];
    public List<int> SourceModelIds { get; } = [];
}

internal sealed class DimensionContextSourceAssociation
{
    public List<DrawingPointInfo> MeasuredPoints { get; } = [];
    public List<DimensionContextRelatedSource> RelatedSources { get; } = [];
    public List<DimensionContextPointAssociation> PointAssociations { get; } = [];
    public List<string> Warnings { get; } = [];
}

internal sealed class DimensionContextRelatedSource
{
    public string Owner { get; set; } = string.Empty;
    public int? DrawingObjectId { get; set; }
    public int? ModelId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string SourceKind { get; set; } = string.Empty;
    public bool HasGeometry { get; set; }
    public DrawingBoundsInfo? GeometryBounds { get; set; }
}

internal sealed class DimensionContextPointAssociation
{
    public int Order { get; set; }
    public DimensionPointObjectMappingStatus Status { get; set; }
    public string MatchedOwner { get; set; } = string.Empty;
    public int? MatchedDrawingObjectId { get; set; }
    public int? MatchedModelId { get; set; }
    public string MatchedType { get; set; } = string.Empty;
    public string MatchedSourceKind { get; set; } = string.Empty;
    public double? DistanceToGeometry { get; set; }
}

internal sealed class DimensionContextGeometry
{
    public DrawingLineInfo? ReferenceLine { get; set; }
    public DrawingLineInfo? LeadLineMain { get; set; }
    public DrawingLineInfo? LeadLineSecond { get; set; }
    public List<DrawingPointInfo> PointList { get; } = [];
    public List<double> LengthList { get; } = [];
    public List<double> RealLengthList { get; } = [];
    public double Distance { get; set; }
    public DrawingBoundsInfo? LocalBounds { get; set; }
    public List<string> Warnings { get; } = [];
}

internal sealed class DimensionContextBuildResult
{
    public List<DimensionContext> Contexts { get; } = [];
    public List<string> Warnings { get; } = [];

    public DimensionContext? FindForItem(DimensionItem item) =>
        Contexts.FirstOrDefault(context => ReferenceEquals(context.Item, item));
}
