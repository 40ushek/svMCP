using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing;

public sealed class DrawingViewsResult
{
    public double SheetWidth  { get; set; }
    public double SheetHeight { get; set; }
    public List<DrawingViewInfo> Views { get; set; } = new();
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
    public double              SheetWidth          { get; set; }
    public double              SheetHeight         { get; set; }
    public int                 Arranged            { get; set; }
    public List<ArrangedView>  Views               { get; set; } = new();
    public int                 ProjectionApplied     { get; set; }
    public int                 ProjectionSkipped     { get; set; }
    public List<string>?       ProjectionDiagnostics { get; set; }
}

public sealed class ArrangedView
{
    public int    Id       { get; set; }
    public string ViewType { get; set; } = string.Empty;
    public double OriginX  { get; set; }
    public double OriginY  { get; set; }
}

public sealed class DrawingReservedAreasResult
{
    public double SheetWidth  { get; set; }
    public double SheetHeight { get; set; }
    public double Margin      { get; set; }
    public IReadOnlyList<LayoutTableGeometryInfo> RawTables      { get; set; } = System.Array.Empty<LayoutTableGeometryInfo>();
    public IReadOnlyList<LayoutTableGeometryInfo> FilteredTables { get; set; } = System.Array.Empty<LayoutTableGeometryInfo>();
    public IReadOnlyList<ReservedRect>            MergedAreas    { get; set; } = System.Array.Empty<ReservedRect>();
    public List<LayoutTableInfo>  LayoutTables  { get; set; } = new();
    public List<DrawingFrameInfo> DrawingFrames { get; set; } = new();
}

public sealed class LayoutTableInfo
{
    public int    Id              { get; set; }
    public string Name            { get; set; } = "";
    public string FileName        { get; set; } = "";
    public double Scale           { get; set; }
    public double XOffset         { get; set; }
    public double YOffset         { get; set; }
    public int    TableCorner     { get; set; }
    public int    RefCorner       { get; set; }
    public bool   OverlapWithViews { get; set; }
}

public sealed class DrawingFrameInfo
{
    public bool   Active { get; set; }
    public double X      { get; set; }
    public double Y      { get; set; }
    public double W      { get; set; }
    public double H      { get; set; }
    public int    Corner { get; set; }
}
