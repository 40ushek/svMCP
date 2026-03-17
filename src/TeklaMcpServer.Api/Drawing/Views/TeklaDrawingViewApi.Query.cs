using System.Collections.Generic;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;

namespace TeklaMcpServer.Api.Drawing;

public sealed partial class TeklaDrawingViewApi
{
    public DrawingViewsResult GetViews()
    {
        var activeDrawing = new DrawingHandler().GetActiveDrawing();
        if (activeDrawing == null)
            throw new DrawingNotOpenException();

        double sheetW = 0;
        double sheetH = 0;
        try
        {
            var ss = activeDrawing.Layout.SheetSize;
            sheetW = ss.Width;
            sheetH = ss.Height;
        }
        catch
        {
        }

        var result = new DrawingViewsResult { SheetWidth = sheetW, SheetHeight = sheetH };
        foreach (var v in EnumerateViews(activeDrawing))
            result.Views.Add(ToInfo(v));

        return result;
    }

    public DrawingReservedAreasResult GetReservedAreas(double margin)
    {
        var drawing = new DrawingHandler().GetActiveDrawing()
            ?? throw new DrawingNotOpenException();

        var tables = DrawingReservedAreaReader.ReadLayoutTableGeometries();
        var sheetMargin = DrawingReservedAreaReader.TryReadSheetMargin();
        var merged = DrawingReservedAreaReader.Read(drawing, margin, 0.0);

        return new DrawingReservedAreasResult
        {
            SheetWidth  = drawing.Layout.SheetSize.Width,
            SheetHeight = drawing.Layout.SheetSize.Height,
            Margin      = margin,
            SheetMargin = sheetMargin,
            Tables      = tables,
            MergedAreas = merged
        };
    }
}
