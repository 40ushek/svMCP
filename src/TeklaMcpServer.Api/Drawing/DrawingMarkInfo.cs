using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing;

public sealed class MarkPropertyValue
{
    public string Name  { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public sealed class DrawingMarkInfo
{
    public int               Id         { get; set; }
    public int?              ModelId    { get; set; }
    public List<MarkPropertyValue> Properties { get; set; } = new();
}

public sealed class GetMarksResult
{
    public int                    Total { get; set; }
    public List<DrawingMarkInfo>  Marks { get; set; } = new();
}
