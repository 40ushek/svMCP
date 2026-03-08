using Tekla.Structures.Drawing;

namespace TeklaBridge.Commands;

internal sealed partial class DrawingCommandHandler
{
    private bool EnsureActiveDrawing(string noActiveDrawingJson)
    {
        if (new DrawingHandler().GetActiveDrawing() != null)
        {
            return true;
        }

        WriteRawJson(noActiveDrawingJson);
        return false;
    }

    private bool EnsureActiveDrawing()
    {
        return EnsureActiveDrawing(NoActiveDrawingErrorJson);
    }
}
