using System.Collections.Generic;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using DrawingPart = Tekla.Structures.Drawing.Part;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>Shared boundary for geometry that must describe what one drawing view shows.</summary>
internal static class DrawingViewParts
{
    public static View? FindView(Tekla.Structures.Drawing.Drawing drawing, int viewId)
    {
        var views = drawing.GetSheet().GetViews();
        while (views.MoveNext())
        {
            if (views.Current is View candidate && candidate.GetIdentifier().ID == viewId)
                return candidate;
        }

        return null;
    }

    /// <summary>
    /// Model ids of the parts this view actually draws, each once. Hidden parts are not
    /// part of its visible outline, even when they belong to the same assembly.
    /// </summary>
    public static IEnumerable<int> VisibleModelIds(View view)
    {
        var seen = new HashSet<int>();
        var objects = view.GetObjects();
        while (objects.MoveNext())
        {
            if (objects.Current is not DrawingPart drawingPart || IsHidden(drawingPart))
                continue;

            var id = drawingPart.ModelIdentifier.ID;
            if (seen.Add(id))
                yield return id;
        }
    }

    private static bool IsHidden(DrawingPart drawingPart)
    {
        try
        {
            return drawingPart.Hideable.IsHidden;
        }
        catch
        {
            // A part that will not report its visibility is kept. Omitting it would turn
            // uncertainty into a false boundary; the caller can still report a read error.
            return false;
        }
    }
}
