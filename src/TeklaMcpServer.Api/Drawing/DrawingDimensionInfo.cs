using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing;

public sealed class DimensionSegmentInfo
{
    public int    Id     { get; set; }
    public double StartX { get; set; }
    public double StartY { get; set; }
    public double EndX   { get; set; }
    public double EndY   { get; set; }
}

public sealed class DrawingDimensionInfo
{
    public int                         Id       { get; set; }
    public string                      Type     { get; set; } = string.Empty;
    public List<DimensionSegmentInfo>  Segments { get; set; } = new();
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
