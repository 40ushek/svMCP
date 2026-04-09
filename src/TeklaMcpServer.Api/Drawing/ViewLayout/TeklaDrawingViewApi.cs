using System.Collections.Generic;
using System.Linq;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using TeklaMcpServer.Api.Drawing;

namespace TeklaMcpServer.Api.Drawing.ViewLayout;

public sealed partial class TeklaDrawingViewApi : IDrawingViewApi
{
    private readonly DrawingViewArrangementSelector _arrangementSelector;

    public TeklaDrawingViewApi(DrawingViewArrangementSelector? arrangementSelector = null)
    {
        _arrangementSelector = arrangementSelector ?? DrawingViewArrangementSelector.CreateDefault();
    }

    private static IEnumerable<View> EnumerateViews(Tekla.Structures.Drawing.Drawing drawing)
    {
        var enumerator = drawing.GetSheet().GetViews();
        while (enumerator.MoveNext())
            if (enumerator.Current is View v)
                yield return v;
    }

    private static DrawingViewInfo ToInfo(View v, IReadOnlyDictionary<int, ReservedRect>? actualRects = null)
    {
        var hasBBox = DrawingViewFrameGeometry.TryGetBoundingRect(v, actualRects, out var bbox);
        return new DrawingViewInfo
        {
            Id = v.GetIdentifier().ID,
            ViewType = v.ViewType.ToString(),
            SemanticKind = ViewSemanticClassifier.Classify(v).ToString(),
            Name = v.Name ?? string.Empty,
            OriginX = v.Origin?.X ?? 0,
            OriginY = v.Origin?.Y ?? 0,
            Scale = v.Attributes.Scale,
            Width = v.Width,
            Height = v.Height,
            BBoxMinX = hasBBox ? bbox.MinX : null,
            BBoxMinY = hasBBox ? bbox.MinY : null,
            BBoxMaxX = hasBBox ? bbox.MaxX : null,
            BBoxMaxY = hasBBox ? bbox.MaxY : null
        };
    }

    internal static DrawingViewsResult BuildViewsResult(
        Tekla.Structures.Drawing.Drawing drawing,
        IReadOnlyList<View>? views = null,
        IReadOnlyDictionary<int, ReservedRect>? actualRects = null)
    {
        double sheetW = 0;
        double sheetH = 0;
        try
        {
            var ss = drawing.Layout.SheetSize;
            sheetW = ss.Width;
            sheetH = ss.Height;
        }
        catch
        {
        }

        var currentViews = views ?? EnumerateViews(drawing).ToList();
        var rects = actualRects ?? DrawingViewFrameGeometry.BuildActualViewRects(drawing);
        var result = new DrawingViewsResult { SheetWidth = sheetW, SheetHeight = sheetH };
        foreach (var view in currentViews)
            result.Views.Add(ToInfo(view, rects));

        return result;
    }
}

