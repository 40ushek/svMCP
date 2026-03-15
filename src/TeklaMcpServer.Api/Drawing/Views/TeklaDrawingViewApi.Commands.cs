using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;

namespace TeklaMcpServer.Api.Drawing;

public sealed partial class TeklaDrawingViewApi
{
    public MoveViewResult MoveView(int viewId, double dx, double dy, bool absolute)
    {
        var activeDrawing = new DrawingHandler().GetActiveDrawing();
        if (activeDrawing == null)
            throw new DrawingNotOpenException();

        var view = EnumerateViews(activeDrawing).FirstOrDefault(v => v.GetIdentifier().ID == viewId)
            ?? throw new ViewNotFoundException(viewId);

        var oldX = view.Origin.X;
        var oldY = view.Origin.Y;
        var origin = view.Origin;

        if (absolute)
        {
            origin.X = dx;
            origin.Y = dy;
        }
        else
        {
            origin.X += dx;
            origin.Y += dy;
        }

        view.Origin = origin;
        view.Modify();
        activeDrawing.CommitChanges();

        return new MoveViewResult
        {
            Moved = true,
            ViewId = viewId,
            OldOriginX = oldX,
            OldOriginY = oldY,
            NewOriginX = origin.X,
            NewOriginY = origin.Y
        };
    }

    public SetViewScaleResult SetViewScale(IEnumerable<int> viewIds, double scale)
    {
        var activeDrawing = new DrawingHandler().GetActiveDrawing();
        if (activeDrawing == null)
            throw new DrawingNotOpenException();

        var targetIds = new HashSet<int>(viewIds);
        var updated = new List<int>();

        foreach (var v in EnumerateViews(activeDrawing))
        {
            var id = v.GetIdentifier().ID;
            if (!targetIds.Contains(id))
                continue;

            v.Attributes.Scale = scale;
            v.Modify();
            updated.Add(id);
        }

        if (updated.Count > 0)
            activeDrawing.CommitChanges();

        return new SetViewScaleResult { UpdatedCount = updated.Count, UpdatedIds = updated, Scale = scale };
    }

    public bool PlaceViews()
    {
        var activeDrawing = new DrawingHandler().GetActiveDrawing();
        if (activeDrawing == null)
            throw new DrawingNotOpenException();

        var iterations = 0;
        while (iterations < 10 && activeDrawing.PlaceViews())
            iterations++;

        if (iterations > 0)
            activeDrawing.CommitChanges();

        return iterations > 0;
    }
}
