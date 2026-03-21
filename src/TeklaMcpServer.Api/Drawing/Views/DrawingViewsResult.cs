using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing;

public sealed class DrawingViewsResult
{
    public double SheetWidth  { get; set; }
    public double SheetHeight { get; set; }
    public List<DrawingViewInfo> Views { get; set; } = new();
}

public sealed class DrawingSectionPlacementSidesResult
{
    public double SheetWidth  { get; set; }
    public double SheetHeight { get; set; }
    public int? BaseViewId { get; set; }
    public string BaseViewType { get; set; } = string.Empty;
    public string BaseViewSelectionKind { get; set; } = string.Empty;
    public string BaseViewReason { get; set; } = string.Empty;
    public bool BaseViewIsFallback { get; set; }
    public List<SectionPlacementSideInfo> Sections { get; set; } = new();
}

public sealed class DrawingDetailMarksResult
{
    public double SheetWidth  { get; set; }
    public double SheetHeight { get; set; }
    public List<DetailMarkInfo> DetailMarks { get; set; } = new();
}

public sealed class DrawingSectionMarksResult
{
    public double SheetWidth  { get; set; }
    public double SheetHeight { get; set; }
    public List<SectionMarkInfo> SectionMarks { get; set; } = new();
}

public sealed class SectionPlacementSideInfo
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PlacementSide { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public bool IsFallback { get; set; }
    public double Scale { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double[] ReferenceAxisX { get; set; } = [];
    public double[] ReferenceAxisY { get; set; } = [];
    public double[] ViewAxisX { get; set; } = [];
    public double[] ViewAxisY { get; set; } = [];
    public double[] ViewNormal { get; set; } = [];
}

public sealed class DetailMarkInfo
{
    public int Id { get; set; }
    public int OwnerViewId { get; set; }
    public string OwnerViewType { get; set; } = string.Empty;
    public string OwnerViewName { get; set; } = string.Empty;
    public string MarkName { get; set; } = string.Empty;
    public int? DetailViewId { get; set; }
    public string DetailViewType { get; set; } = string.Empty;
    public string DetailViewName { get; set; } = string.Empty;
    public double? DetailViewScale { get; set; }
    public List<RelatedDrawingObjectInfo> RelatedObjects { get; set; } = new();
    public double[] CenterPoint { get; set; } = [];
    public double[] BoundaryPoint { get; set; } = [];
    public double[] LabelPoint { get; set; } = [];
}

public sealed class SectionMarkInfo
{
    public int Id { get; set; }
    public int OwnerViewId { get; set; }
    public string OwnerViewType { get; set; } = string.Empty;
    public string OwnerViewName { get; set; } = string.Empty;
    public List<RelatedDrawingObjectInfo> RelatedObjects { get; set; } = new();
}

public sealed class RelatedDrawingObjectInfo
{
    public int Id { get; set; }
    public string ObjectType { get; set; } = string.Empty;
    public string ViewType { get; set; } = string.Empty;
    public string ViewName { get; set; } = string.Empty;
}

public sealed class MoveViewResult
{
    public bool   Moved      { get; set; }
    public int    ViewId     { get; set; }
    public double OldOriginX { get; set; }
    public double OldOriginY { get; set; }
    public double NewOriginX { get; set; }
    public double NewOriginY { get; set; }
}

public sealed class SetViewScaleResult
{
    public int          UpdatedCount { get; set; }
    public List<int>    UpdatedIds   { get; set; } = new();
    public double       Scale        { get; set; }
}

public sealed class FitViewsResult
{
    public double              OptimalScale        { get; set; }
    /// <summary>True when ScalePolicy=PreserveExistingScales was used; OptimalScale is Max of existing scales, not a unified target.</summary>
    public bool                ScalePreserved      { get; set; }
    public string              ScalePolicy         { get; set; } = string.Empty;
    public string              ApplyMode           { get; set; } = string.Empty;
    public double              SheetWidth          { get; set; }
    public double              SheetHeight         { get; set; }
    public double              Margin              { get; set; }
    public int                 Arranged            { get; set; }
    public List<ArrangedView>  Views               { get; set; } = new();
    public int                 ProjectionApplied     { get; set; }
    public int                 ProjectionSkipped     { get; set; }
    public List<string>?       ProjectionDiagnostics { get; set; }
    public DrawingReservedAreasResult? ReservedAreas { get; set; }
}

public sealed class ArrangedView
{
    public int    Id       { get; set; }
    public string ViewType { get; set; } = string.Empty;
    public double OriginX  { get; set; }
    public double OriginY  { get; set; }
    public string PreferredPlacementSide { get; set; } = string.Empty;
    public string ActualPlacementSide    { get; set; } = string.Empty;
    public bool   PlacementFallbackUsed  { get; set; }
}

public sealed class DrawingReservedAreasResult
{
    public double  SheetWidth   { get; set; }
    public double  SheetHeight  { get; set; }
    public double  Margin       { get; set; }
    public double? SheetMargin  { get; set; }
    public IReadOnlyList<LayoutTableGeometryInfo> Tables      { get; set; } = System.Array.Empty<LayoutTableGeometryInfo>();
    public IReadOnlyList<ReservedRect>            MergedAreas { get; set; } = System.Array.Empty<ReservedRect>();
}
