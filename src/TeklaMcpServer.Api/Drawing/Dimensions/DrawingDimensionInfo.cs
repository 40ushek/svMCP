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
    public string                     Orientation   { get; set; } = string.Empty;
    public double                     Distance      { get; set; }
    public double                     DirectionX    { get; set; }
    public double                     DirectionY    { get; set; }
    public int                        TopDirection  { get; set; }
    public DrawingBoundsInfo?         Bounds        { get; set; }
    public DrawingLineInfo?           ReferenceLine { get; set; }
    public List<DimensionSegmentInfo> Segments      { get; set; } = new();
}

public sealed class GetDimensionsResult
{
    public int                           Total      { get; set; }
    public List<DrawingDimensionInfo>    Dimensions { get; set; } = new();
}

public sealed class MoveDimensionResult
{
    public bool   Moved        { get; set; }
    public int    DimensionId  { get; set; }
    public double NewDistance  { get; set; }
}

public sealed class DeleteDimensionResult
{
    public bool HasActiveDrawing { get; set; }
    public bool Deleted { get; set; }
    public int DimensionId { get; set; }
}
