using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing;

public sealed class DrawingBoundsInfo
{
    public double MinX { get; set; }
    public double MinY { get; set; }
    public double MaxX { get; set; }
    public double MaxY { get; set; }
    public double Width => MaxX - MinX;
    public double Height => MaxY - MinY;
}

public sealed class DrawingLineInfo
{
    public double StartX { get; set; }
    public double StartY { get; set; }
    public double EndX { get; set; }
    public double EndY { get; set; }
    public double Length => System.Math.Sqrt(((EndX - StartX) * (EndX - StartX)) + ((EndY - StartY) * (EndY - StartY)));
}

public sealed class DrawingPointInfo
{
    public double X { get; set; }
    public double Y { get; set; }
    public int Order { get; set; }
}

public sealed class DrawingVectorInfo
{
    public double X { get; set; }
    public double Y { get; set; }
}

public sealed class DimensionSegmentInfo
{
    public int                Id            { get; set; }
    public double             StartX        { get; set; }
    public double             StartY        { get; set; }
    public double             EndX          { get; set; }
    public double             EndY          { get; set; }
    public double             Distance      { get; set; }
    public double             DirectionX    { get; set; }
    public double             DirectionY    { get; set; }
    public int                TopDirection  { get; set; }
    public DrawingBoundsInfo? Bounds        { get; set; }
    public DrawingBoundsInfo? TextBounds    { get; set; }
    public DrawingLineInfo?   DimensionLine { get; set; }
    public DrawingLineInfo?   LeadLineMain  { get; set; }
    public DrawingLineInfo?   LeadLineSecond { get; set; }
}

public sealed class DrawingDimensionInfo
{
    public int                        Id            { get; set; }
    public string                     Type          { get; set; } = string.Empty;
    public string                     DimensionType { get; set; } = string.Empty;
    public int?                       ViewId        { get; set; }
    public string                     ViewType      { get; set; } = string.Empty;
    public double                     ViewScale     { get; set; }
    public string                     Orientation   { get; set; } = string.Empty;
    public double                     Distance      { get; set; }
    public double                     DirectionX    { get; set; }
    public double                     DirectionY    { get; set; }
    public int                        TopDirection  { get; set; }
    public DrawingBoundsInfo?         Bounds        { get; set; }
    public DrawingLineInfo?           ReferenceLine { get; set; }
    public List<DrawingPointInfo>     MeasuredPoints { get; set; } = new();
    public List<DimensionSegmentInfo> Segments      { get; set; } = new();
    internal DimensionSourceKind      SourceKind    { get; set; }
    internal DimensionGeometryKind    GeometryKind  { get; set; }
    internal DimensionType            ClassifiedDimensionType { get; set; }
    internal List<int>                SourceObjectIds { get; } = [];
}

public sealed class DimensionItemInfo
{
    public int Id { get; set; }
    public List<int> SegmentIds { get; set; } = new();
    public int? ViewId { get; set; }
    public string DimensionType { get; set; } = string.Empty;
    public string TeklaDimensionType { get; set; } = string.Empty;
    public DrawingLineInfo? ReferenceLine { get; set; }
    public DrawingPointInfo? StartPoint { get; set; }
    public DrawingPointInfo? EndPoint { get; set; }
    public DrawingPointInfo? CenterPoint { get; set; }
    public List<DrawingPointInfo> PointList { get; set; } = new();
    public List<double> LengthList { get; set; } = new();
    public List<double> RealLengthList { get; set; } = new();
    public double Distance { get; set; }
}

public sealed class DimensionGroupInfo
{
    public int? ViewId { get; set; }
    public string ViewType { get; set; } = string.Empty;
    public string DimensionType { get; set; } = string.Empty;
    public string TeklaDimensionType { get; set; } = string.Empty;
    public DrawingVectorInfo? Direction { get; set; }
    public int TopDirection { get; set; }
    public DrawingLineInfo? ReferenceLine { get; set; }
    public DrawingLineInfo? LeadLineMain { get; set; }
    public DrawingLineInfo? LeadLineSecond { get; set; }
    public double MaximumDistance { get; set; }
    public int RawItemCount { get; set; }
    public int ReducedItemCount { get; set; }
    public List<DimensionItemInfo> Items { get; set; } = new();
}

public sealed class GetDimensionsResult
{
    public int Total { get; set; }
    public int DrawingDimensionCount { get; set; }
    public int RawItemCount { get; set; }
    public int ReducedItemCount { get; set; }
    public int GroupCount { get; set; }
    public List<DimensionGroupInfo> Groups { get; set; } = new();
}

public sealed class MoveDimensionResult
{
    public bool   Moved        { get; set; }
    public int    DimensionId  { get; set; }
    public double NewDistance  { get; set; }
}

public sealed class DrawDimensionTextBoxesResult
{
    public string Group { get; set; } = string.Empty;
    public int ClearedCount { get; set; }
    public int CreatedCount { get; set; }
    public List<int> CreatedIds { get; set; } = new();
    public int DimensionCount { get; set; }
    public int SegmentCount { get; set; }
}

public sealed class DimensionTextPlacementDebugResult
{
    public int? ViewId { get; set; }
    public int Total { get; set; }
    public List<DimensionTextPlacementDebugInfo> Dimensions { get; set; } = new();
}

public sealed class DimensionTextPlacementDebugInfo
{
    public int DimensionId { get; set; }
    public string DimensionType { get; set; } = string.Empty;
    public string TextPlacing { get; set; } = string.Empty;
    public string ShortDimension { get; set; } = string.Empty;
    public int PlacingDirectionSign { get; set; }
    public double LeftTagLineOffset { get; set; }
    public double RightTagLineOffset { get; set; }
    public List<DimensionSegmentTextPlacementDebugInfo> Segments { get; set; } = new();
}

public sealed class DimensionSegmentTextPlacementDebugInfo
{
    public int SegmentId { get; set; }
    public string ExpectedText { get; set; } = string.Empty;
    public DrawingLineInfo? DimensionLine { get; set; }
    public string SelectedSource { get; set; } = string.Empty;
    public List<RelatedTextCandidateDebugInfo> RelatedTextCandidates { get; set; } = new();
}

public sealed class RelatedTextCandidateDebugInfo
{
    public string Owner { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public bool MatchesExpected { get; set; }
    public double Score { get; set; }
    public double CenterX { get; set; }
    public double CenterY { get; set; }
}

public sealed class DeleteDimensionResult
{
    public bool HasActiveDrawing { get; set; }
    public bool Deleted { get; set; }
    public int DimensionId { get; set; }
}

public sealed class DimensionSourceDebugResult
{
    public int? ViewId { get; set; }
    public int Total { get; set; }
    public List<DimensionSourceDebugInfo> Dimensions { get; set; } = new();
}

public sealed class DimensionSourceDebugInfo
{
    public int DimensionId { get; set; }
    public string DimensionType { get; set; } = string.Empty;
    public string TeklaDimensionType { get; set; } = string.Empty;
    public List<DrawingPointInfo> MeasuredPoints { get; set; } = new();
    public List<DimensionPointObjectMappingInfo> PointMappings { get; set; } = new();
    public List<DimensionSourceCandidateInfo> Candidates { get; set; } = new();
}

public sealed class DimensionPointObjectMappingInfo
{
    public int Order { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public string Status { get; set; } = string.Empty;
    public string MatchedOwner { get; set; } = string.Empty;
    public int? MatchedDrawingObjectId { get; set; }
    public int? MatchedModelId { get; set; }
    public string MatchedType { get; set; } = string.Empty;
    public string MatchedSourceKind { get; set; } = string.Empty;
    public double? DistanceToGeometry { get; set; }
    public DrawingPointInfo? NearestGeometryPoint { get; set; }
    public int CandidateCount { get; set; }
    public string Warning { get; set; } = string.Empty;
}

public sealed class DimensionSourceCandidateInfo
{
    public string Owner { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int? DrawingObjectId { get; set; }
    public int? ModelId { get; set; }
    public string ResolvedModelType { get; set; } = string.Empty;
    public string SourceKind { get; set; } = string.Empty;
    public string GeometrySource { get; set; } = string.Empty;
    public bool HasGeometry { get; set; }
    public int GeometryPointCount { get; set; }
    public DrawingBoundsInfo? GeometryBounds { get; set; }
    public List<DrawingPointInfo> GeometryPoints { get; set; } = new();
    public List<string> GeometryWarnings { get; set; } = new();
}
