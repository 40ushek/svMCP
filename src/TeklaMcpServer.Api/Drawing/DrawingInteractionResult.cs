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

public sealed class SheetObjectsDebugResult
{
    public DrawingContextDrawingInfo Drawing { get; set; } = new();
    public double SheetWidth { get; set; }
    public double SheetHeight { get; set; }
    public int TotalObjectsScanned { get; set; }
    public List<SheetObjectDebugItem> SheetLevelObjects { get; set; } = new();
    public List<ReservedRect> ReservedAreaCandidates { get; set; } = new();
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

public sealed class SheetObjectDebugItem
{
    public int Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public int? ModelId { get; set; }
    public bool IsSheetLevel { get; set; }
    public string OwnerViewType { get; set; } = string.Empty;
    public string OwnerViewName { get; set; } = string.Empty;
    public bool HasBoundingBox { get; set; }
    public double? BboxMinX { get; set; }
    public double? BboxMinY { get; set; }
    public double? BboxMaxX { get; set; }
    public double? BboxMaxY { get; set; }
}

