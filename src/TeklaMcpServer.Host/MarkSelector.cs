using Tekla.Structures.Drawing;

namespace TeklaMcpServer.Host;

internal static class MarkSelector
{
    /// <summary>
    /// Returns the first selected Mark in the active drawing, or null if none selected.
    /// </summary>
    public static Mark? GetSelected(DrawingHandler drawingHandler)
    {
        var selected = drawingHandler.GetDrawingObjectSelector().GetSelected();
        while (selected.MoveNext())
        {
            if (selected.Current is Mark mark)
                return mark;
        }
        return null;
    }
}
