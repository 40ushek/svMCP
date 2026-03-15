using System.Collections.Generic;
using Tekla.Structures.Drawing;

namespace TeklaMcpServer.Api.Drawing;

public sealed class DrawingArrangeContext
{
    public DrawingArrangeContext(
        Tekla.Structures.Drawing.Drawing drawing,
        IReadOnlyList<View> views,
        double sheetWidth,
        double sheetHeight,
        double margin,
        double gap,
        IReadOnlyList<ReservedRect>? reservedAreas = null)
    {
        Drawing = drawing ?? throw new System.ArgumentNullException(nameof(drawing));
        Views = views ?? throw new System.ArgumentNullException(nameof(views));
        SheetWidth = sheetWidth;
        SheetHeight = sheetHeight;
        Margin = margin;
        Gap = gap;
        ReservedAreas = reservedAreas ?? System.Array.Empty<ReservedRect>();
    }

    public Tekla.Structures.Drawing.Drawing Drawing { get; }
    public IReadOnlyList<View> Views { get; }
    public double SheetWidth { get; }
    public double SheetHeight { get; }
    public double Margin { get; }
    public double Gap { get; }
    public IReadOnlyList<ReservedRect> ReservedAreas { get; }
}
