using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing;

public sealed class MarkPropertyValue
{
    public string Name  { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public sealed class DrawingMarkInfo
{
    public int                     Id         { get; set; }
    public int?                    ModelId    { get; set; }
    public double                  InsertionX { get; set; }
    public double                  InsertionY { get; set; }
    public double                  BboxMinX   { get; set; }
    public double                  BboxMinY   { get; set; }
    public double                  BboxMaxX   { get; set; }
    public double                  BboxMaxY   { get; set; }
    public List<MarkPropertyValue> Properties { get; set; } = new();
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
