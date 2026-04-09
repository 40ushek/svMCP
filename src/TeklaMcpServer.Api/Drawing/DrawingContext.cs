using System.Collections.Generic;
using TeklaMcpServer.Api.Drawing.ViewLayout;

namespace TeklaMcpServer.Api.Drawing;

public sealed class DrawingContext
{
    public DrawingInfo Drawing { get; set; } = new();
    public DrawingSheetContext Sheet { get; set; } = new();
    public List<DrawingViewInfo> Views { get; set; } = new();
    public DrawingReservedLayoutContext ReservedLayout { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public sealed class DrawingSheetContext
{
    public double Width { get; set; }
    public double Height { get; set; }
}

public sealed class DrawingReservedLayoutContext
{
    public double Margin { get; set; }
    public double? SheetMargin { get; set; }
    public List<LayoutTableGeometryInfo> Tables { get; set; } = new();
    public List<ReservedRect> Areas { get; set; } = new();
}

public sealed class GetDrawingLayoutContextResult
{
    public bool Success { get; set; }
    public DrawingContext Context { get; set; } = new();
    public string Error { get; set; } = string.Empty;
}
