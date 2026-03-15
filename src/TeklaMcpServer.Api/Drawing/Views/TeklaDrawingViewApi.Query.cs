using Tekla.Structures.Drawing;

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
}
