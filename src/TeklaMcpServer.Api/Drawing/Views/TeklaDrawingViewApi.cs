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

    private static DrawingViewInfo ToInfo(View v)
    {
        var hasBBox = DrawingViewSheetGeometry.TryGetBoundingRect(v, out var bbox);
        return new DrawingViewInfo
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
            BBoxMinX = hasBBox ? bbox.MinX : null,
            BBoxMinY = hasBBox ? bbox.MinY : null,
            BBoxMaxX = hasBBox ? bbox.MaxX : null,
            BBoxMaxY = hasBBox ? bbox.MaxY : null
        };
    }

    private static Dictionary<int, (double X, double Y)> TryGetFrameOffsetsFromBoundingBoxes(
        IReadOnlyList<View> views)
    {
        var offsets = new Dictionary<int, (double X, double Y)>(views.Count);
        foreach (var view in views)
        {
            var origin = view.Origin;
            var originX = origin?.X ?? 0;
            var originY = origin?.Y ?? 0;

            // Only record offset when the bbox is plausible — i.e. the bbox center is close
            // enough to Origin that it actually represents the current view position.
            // If the bbox is stale (e.g. right after a Modify/CommitChanges cycle), the center
            // returned by TryGetCenter falls back to Origin, giving a false zero offset.
            // A false-zero offset causes the correction to leave the view at arranged.OriginX,
            // which is the packer's target CENTER — but the view displays at center + true_offset,
            // landing far outside the sheet. Skipping such views leaves them at their last
            // committed position rather than actively misplacing them.
            if (view is IAxisAlignedBoundingBox bounded)
            {
                var box = bounded.GetAxisAlignedBoundingBox();
                if (box == null || !DrawingViewSheetGeometry.IsBoundingBoxOffsetPlausible(
                        originX, originY, view.Width, view.Height,
                        new ReservedRect(box.MinPoint.X, box.MinPoint.Y, box.MaxPoint.X, box.MaxPoint.Y)))
                    continue;
            }

            if (!DrawingViewSheetGeometry.TryGetCenter(view, out var centerX, out var centerY))
            {
                offsets.Clear();
                return offsets;
            }

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
            if (!DrawingViewSheetGeometry.TryGetBoundingRect(view, out var rect))
            {
                sizes.Clear();
                return sizes;
            }

            sizes[view.GetIdentifier().ID] = (
                rect.MaxX - rect.MinX,
                rect.MaxY - rect.MinY);
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
