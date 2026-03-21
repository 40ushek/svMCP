using System.Collections.Generic;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;

namespace TeklaMcpServer.Api.Drawing;

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

    private static DrawingViewInfo ToInfo(View v) => new()
    {
        Id = v.GetIdentifier().ID,
        ViewType = v.ViewType.ToString(),
        SemanticKind = ViewSemanticClassifier.Classify(v.ViewType).ToString(),
        Name = v.Name ?? string.Empty,
        OriginX = v.Origin?.X ?? 0,
        OriginY = v.Origin?.Y ?? 0,
        Scale = v.Attributes.Scale,
        Width = v.Width,
        Height = v.Height,
        BBoxMinX = (v as IAxisAlignedBoundingBox)?.GetAxisAlignedBoundingBox()?.MinPoint.X,
        BBoxMinY = (v as IAxisAlignedBoundingBox)?.GetAxisAlignedBoundingBox()?.MinPoint.Y,
        BBoxMaxX = (v as IAxisAlignedBoundingBox)?.GetAxisAlignedBoundingBox()?.MaxPoint.X,
        BBoxMaxY = (v as IAxisAlignedBoundingBox)?.GetAxisAlignedBoundingBox()?.MaxPoint.Y
    };

    private static Dictionary<int, (double X, double Y)> TryGetFrameOffsetsFromBoundingBoxes(
        IReadOnlyList<View> views)
    {
        var offsets = new Dictionary<int, (double X, double Y)>(views.Count);
        foreach (var view in views)
        {
            if (view is not IAxisAlignedBoundingBox bounded)
            {
                offsets.Clear();
                return offsets;
            }

            var box = bounded.GetAxisAlignedBoundingBox();
            if (box == null)
            {
                offsets.Clear();
                return offsets;
            }

            var centerX = (box.MinPoint.X + box.MaxPoint.X) * 0.5;
            var centerY = (box.MinPoint.Y + box.MaxPoint.Y) * 0.5;
            var origin = view.Origin;
            var originX = origin?.X ?? 0;
            var originY = origin?.Y ?? 0;
            var scale = view.Attributes.Scale > 0 ? view.Attributes.Scale : 1.0;
            offsets[view.GetIdentifier().ID] = ((centerX - originX) * scale, (centerY - originY) * scale);
        }

        return offsets;
    }

    private static Dictionary<int, (double Width, double Height)> TryGetFrameSizesFromBoundingBoxes(
        IReadOnlyList<View> views)
    {
        var sizes = new Dictionary<int, (double Width, double Height)>(views.Count);
        foreach (var view in views)
        {
            if (view is not IAxisAlignedBoundingBox bounded)
            {
                sizes.Clear();
                return sizes;
            }

            var box = bounded.GetAxisAlignedBoundingBox();
            if (box == null)
            {
                sizes.Clear();
                return sizes;
            }

            sizes[view.GetIdentifier().ID] = (
                box.MaxPoint.X - box.MinPoint.X,
                box.MaxPoint.Y - box.MinPoint.Y);
        }

        return sizes;
    }
}

public sealed class DrawingNotOpenException : System.Exception
{
    public DrawingNotOpenException() : base("No drawing is currently open.") { }
}

public sealed class ViewNotFoundException : System.Exception
{
    public ViewNotFoundException(int id) : base($"View with ID {id} not found in active drawing.") { }
}
