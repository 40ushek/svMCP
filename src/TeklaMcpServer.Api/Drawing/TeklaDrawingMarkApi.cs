using System;
using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Model;

namespace TeklaMcpServer.Api.Drawing;

public sealed class TeklaDrawingMarkApi : IDrawingMarkApi
{
    private readonly Model _model;

    public TeklaDrawingMarkApi(Model model) => _model = model;

    public GetMarksResult GetMarks(int? viewId)
    {
        var activeDrawing = new DrawingHandler().GetActiveDrawing();
        if (activeDrawing == null)
            throw new DrawingNotOpenException();

        // Performance: disable auto-fetch during enumeration
        DrawingEnumeratorBase.AutoFetch = false;

        DrawingObjectEnumerator markObjects;
        if (viewId.HasValue)
        {
            var view = EnumerateViews(activeDrawing).FirstOrDefault(v => v.GetIdentifier().ID == viewId.Value)
                ?? throw new ViewNotFoundException(viewId.Value);
            markObjects = view.GetAllObjects(typeof(Mark));
        }
        else
        {
            markObjects = activeDrawing.GetSheet().GetAllObjects(typeof(Mark));
        }

        var marks = new List<DrawingMarkInfo>();

        while (markObjects.MoveNext())
        {
            if (markObjects.Current is not Mark mark) continue;

            var bbox = mark.GetAxisAlignedBoundingBox();
            var ins  = mark.InsertionPoint;
            var info = new DrawingMarkInfo
            {
                Id         = mark.GetIdentifier().ID,
                InsertionX = Math.Round(ins.X, 1),
                InsertionY = Math.Round(ins.Y, 1),
                BboxMinX   = Math.Round(bbox.MinPoint.X, 1),
                BboxMinY   = Math.Round(bbox.MinPoint.Y, 1),
                BboxMaxX   = Math.Round(bbox.MaxPoint.X, 1),
                BboxMaxY   = Math.Round(bbox.MaxPoint.Y, 1)
            };

            // Resolve model object ID from first related drawing object
            var related = mark.GetRelatedObjects();
            while (related.MoveNext())
            {
                if (related.Current is Tekla.Structures.Drawing.ModelObject mo)
                {
                    info.ModelId = mo.ModelIdentifier.ID;
                    break;
                }
            }

            // Read property element names and their computed values
            var contentEnum = mark.Attributes.Content.GetEnumerator();
            while (contentEnum.MoveNext())
            {
                if (contentEnum.Current is PropertyElement prop)
                    info.Properties.Add(new MarkPropertyValue { Name = prop.Name, Value = prop.Value });
            }

            marks.Add(info);
        }

        // Detect pairwise AABB overlaps
        var overlaps = new List<MarkOverlap>();
        for (int i = 0; i < marks.Count; i++)
        for (int j = i + 1; j < marks.Count; j++)
        {
            var a = marks[i]; var b = marks[j];
            if (a.BboxMaxX > b.BboxMinX && b.BboxMaxX > a.BboxMinX &&
                a.BboxMaxY > b.BboxMinY && b.BboxMaxY > a.BboxMinY)
                overlaps.Add(new MarkOverlap { IdA = a.Id, IdB = b.Id });
        }

        return new GetMarksResult { Total = marks.Count, Marks = marks, Overlaps = overlaps };
    }

    private static IEnumerable<View> EnumerateViews(Tekla.Structures.Drawing.Drawing drawing)
    {
        var enumerator = drawing.GetSheet().GetViews();
        while (enumerator.MoveNext())
            if (enumerator.Current is View v)
                yield return v;
    }
}
