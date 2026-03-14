using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing;

public sealed class DrawingDebugOverlayRequest
{
    public string Group { get; set; } = "default";
    public bool ClearGroupFirst { get; set; }
    public List<DrawingDebugShape> Shapes { get; set; } = new();
}

public sealed class DrawingDebugShape
{
    public string Kind { get; set; } = string.Empty;
    public int? ViewId { get; set; }
    public double X1 { get; set; }
    public double Y1 { get; set; }
    public double X2 { get; set; }
    public double Y2 { get; set; }
    public List<double[]> Points { get; set; } = new();
    public string Text { get; set; } = string.Empty;
    public double Angle { get; set; }
}

public sealed class DrawingDebugOverlayResult
{
    public string Group { get; set; } = string.Empty;
    public int ClearedCount { get; set; }
    public int CreatedCount { get; set; }
    public List<int> CreatedIds { get; set; } = new();
}

public sealed class ClearDrawingDebugOverlayResult
{
    public string Group { get; set; } = string.Empty;
    public int ClearedCount { get; set; }
}
