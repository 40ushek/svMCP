using System;
using System.Collections.Generic;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Model;

namespace TeklaMcpServer.Api.Drawing;

public sealed class MarkDrawingGeometryDebugInfo
{
    public int MarkId { get; set; }
    public double InsertionX { get; set; }
    public double InsertionY { get; set; }
    public MarkGeometryInfo Geometry { get; set; } = new();
}

public static class MarkDrawingGeometryDebugReader
{
    public static List<MarkDrawingGeometryDebugInfo> Collect(View view)
    {
        if (view == null)
            throw new ArgumentNullException(nameof(view));

        var result = new List<MarkDrawingGeometryDebugInfo>();
        var model = new Model();
        var viewId = ResolveViewId(view);
        var seenIds = new HashSet<int>();

        var markObjects = view.GetAllObjects(typeof(Mark));
        while (markObjects.MoveNext())
        {
            if (markObjects.Current is not Mark mark)
                continue;

            var markId = mark.GetIdentifier().ID;
            if (!seenIds.Add(markId))
                continue;

            result.Add(new MarkDrawingGeometryDebugInfo
            {
                MarkId = markId,
                InsertionX = mark.InsertionPoint.X,
                InsertionY = mark.InsertionPoint.Y,
                Geometry = MarkGeometryResolver.Build(mark, model, viewId)
            });
        }

        return result;
    }

    private static int? ResolveViewId(View view)
    {
        try
        {
            return view.GetIdentifier().ID;
        }
        catch
        {
            return null;
        }
    }
}
