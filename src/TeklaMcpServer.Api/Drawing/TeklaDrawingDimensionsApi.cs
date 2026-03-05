using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Model;

namespace TeklaMcpServer.Api.Drawing;

public sealed class TeklaDrawingDimensionsApi : IDrawingDimensionsApi
{
    private readonly Model _model;

    public TeklaDrawingDimensionsApi(Model model) => _model = model;

    public GetDimensionsResult GetDimensions(int? viewId)
    {
        var activeDrawing = new DrawingHandler().GetActiveDrawing();
        if (activeDrawing == null)
            throw new DrawingNotOpenException();

        DrawingEnumeratorBase.AutoFetch = false;

        DrawingObjectEnumerator dimObjects;
        if (viewId.HasValue)
        {
            var view = EnumerateViews(activeDrawing).FirstOrDefault(v => v.GetIdentifier().ID == viewId.Value)
                ?? throw new ViewNotFoundException(viewId.Value);
            dimObjects = view.GetAllObjects(typeof(StraightDimensionSet));
        }
        else
        {
            dimObjects = activeDrawing.GetSheet().GetAllObjects(typeof(StraightDimensionSet));
        }

        var dimensions = new List<DrawingDimensionInfo>();

        while (dimObjects.MoveNext())
        {
            if (dimObjects.Current is not StraightDimensionSet dimSet) continue;

            var info = new DrawingDimensionInfo
            {
                Id   = dimSet.GetIdentifier().ID,
                Type = dimSet.GetType().Name
            };

            // Iterate individual StraightDimension segments within this set
            var segEnum = dimSet.GetObjects();
            while (segEnum.MoveNext())
            {
                if (segEnum.Current is not StraightDimension seg) continue;

                var start = seg.StartPoint;
                var end   = seg.EndPoint;

                // Read the displayed value directly — ContainerElement.GetUnformattedString()
                // returns the formatted dimension text exactly as shown on the drawing.
                var raw = seg.Value.GetUnformattedString() ?? string.Empty;
                double.TryParse(raw.Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var dist);

                info.Segments.Add(new DimensionSegmentInfo
                {
                    Id     = seg.GetIdentifier().ID,
                    Value  = dist,
                    StartX = Math.Round(start.X, 1),
                    StartY = Math.Round(start.Y, 1),
                    EndX   = Math.Round(end.X, 1),
                    EndY   = Math.Round(end.Y, 1)
                });
            }

            dimensions.Add(info);
        }

        return new GetDimensionsResult { Total = dimensions.Count, Dimensions = dimensions };
    }

    private static IEnumerable<View> EnumerateViews(Tekla.Structures.Drawing.Drawing drawing)
    {
        var enumerator = drawing.GetSheet().GetViews();
        while (enumerator.MoveNext())
            if (enumerator.Current is View v)
                yield return v;
    }
}
