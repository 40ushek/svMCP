using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing;

public sealed class MarkPropertyValue
{
    public string Name  { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public sealed class MarkArrowheadInfo
{
    public string Type { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public double Height { get; set; }
    public double Width { get; set; }
}

public sealed class MarkLeaderLineInfo
{
    public string Type { get; set; } = string.Empty;
    public double StartX { get; set; }
    public double StartY { get; set; }
    public double EndX { get; set; }
    public double EndY { get; set; }
    public List<double[]> ElbowPoints { get; set; } = new();
}

public sealed class MarkAxisInfo
{
    public double StartX { get; set; }
    public double StartY { get; set; }
    public double EndX { get; set; }
    public double EndY { get; set; }
    public double Dx { get; set; }
    public double Dy { get; set; }
    public double Length { get; set; }
    public double AngleDeg { get; set; }
    /// <summary>
    /// False when BaseLinePlacing axis length was too short (&lt;0.001) — data may be unreliable.
    /// </summary>
    public bool IsReliable { get; set; }
}

public sealed class MarkObjectAlignedBoundingBoxInfo
{
    public double Width { get; set; }
    public double Height { get; set; }
    public double AngleToAxis { get; set; }
    public double CenterX { get; set; }
    public double CenterY { get; set; }
    public double MinX { get; set; }
    public double MinY { get; set; }
    public double MaxX { get; set; }
    public double MaxY { get; set; }
    public List<double[]> Corners { get; set; } = new();
}

public sealed class DrawingMarkInfo
{
    public int                     Id         { get; set; }
    public int                     ViewId     { get; set; }
    public int?                    ModelId    { get; set; }
    public double                  InsertionX { get; set; }
    public double                  InsertionY { get; set; }
    public double                  BboxMinX   { get; set; }
    public double                  BboxMinY   { get; set; }
    public double                  BboxMaxX    { get; set; }
    public double                  BboxMaxY    { get; set; }
    public double                  CenterX     { get; set; }
    public double                  CenterY     { get; set; }
    public string                  PlacingType  { get; set; } = string.Empty;
    public double                  PlacingX     { get; set; }
    public double                  PlacingY     { get; set; }
    public double                  Angle        { get; set; }
    public double                  RotationAngle { get; set; }
    public string                  TextAlignment { get; set; } = string.Empty;
    public MarkAxisInfo?           Axis         { get; set; }
    public MarkObjectAlignedBoundingBoxInfo? ObjectAlignedBoundingBox { get; set; }
    public MarkArrowheadInfo       ArrowHead    { get; set; } = new();
    public List<MarkLeaderLineInfo> LeaderLines { get; set; } = new();
    public List<MarkPropertyValue> Properties  { get; set; } = new();
}

public sealed class MarkOverlap
{
    public int IdA { get; set; }
    public int IdB { get; set; }
}

public sealed class GetMarksResult
{
    public int                    Total    { get; set; }
    public List<DrawingMarkInfo>  Marks    { get; set; } = new();
    public List<MarkOverlap>      Overlaps { get; set; } = new();
}

public sealed class ResolveMarksResult
{
    public int        MarksMovedCount { get; set; }
    public List<int>  MovedIds        { get; set; } = new();
    public int        Iterations      { get; set; }
    public int        RemainingOverlaps { get; set; }
}
