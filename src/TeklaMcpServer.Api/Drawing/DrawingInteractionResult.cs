using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing;

public sealed class SelectDrawingObjectsResult
{
    public List<int> SelectedDrawingObjectIds { get; set; } = new();
    public List<int> SelectedModelIds { get; set; } = new();
}

public sealed class FilterDrawingObjectsResult
{
    public bool IsKnownType { get; set; }
    public List<DrawingObjectItem> Objects { get; set; } = new();
}

public sealed class DrawingContextResult
{
    public DrawingContextDrawingInfo Drawing { get; set; } = new();
    public List<DrawingObjectItem> SelectedObjects { get; set; } = new();
}

public sealed class DrawingContextDrawingInfo
{
    public string Guid { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Mark { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public sealed class DrawingObjectItem
{
    public int Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public int? ModelId { get; set; }
}
