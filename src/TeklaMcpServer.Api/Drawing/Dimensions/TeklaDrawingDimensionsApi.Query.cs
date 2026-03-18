using System.Collections.Generic;
using System.Linq;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;

namespace TeklaMcpServer.Api.Drawing;

public sealed partial class TeklaDrawingDimensionsApi
{
    internal List<DimensionGroup> GetDimensionGroups(int? viewId) => DimensionGroupFactory.BuildGroups(GetDimensions(viewId));

    public GetDimensionsResult GetDimensions(int? viewId)
    {
        var activeDrawing = new DrawingHandler().GetActiveDrawing();
        if (activeDrawing == null)
            throw new DrawingNotOpenException();

        var previousAutoFetch = DrawingEnumeratorBase.AutoFetch;
        DrawingEnumeratorBase.AutoFetch = false;
        try
        {
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
                if (dimObjects.Current is not StraightDimensionSet dimSet)
                    continue;

                dimensions.Add(BuildDimensionInfo(dimSet));
            }

            return new GetDimensionsResult
            {
                Total = dimensions.Count,
                Dimensions = dimensions
            };
        }
        finally
        {
            DrawingEnumeratorBase.AutoFetch = previousAutoFetch;
        }
    }

    private static DrawingDimensionInfo BuildDimensionInfo(StraightDimensionSet dimSet)
    {
        var (ownerViewId, ownerViewType) = GetOwnerViewInfo(dimSet);
        var info = new DrawingDimensionInfo
        {
            Id = dimSet.GetIdentifier().ID,
            Type = dimSet.GetType().Name,
            ViewId = ownerViewId,
            ViewType = ownerViewType,
            Distance = dimSet.Distance,
            Bounds = TryGetBounds(dimSet)
        };

        var segEnum = dimSet.GetObjects();
        while (segEnum.MoveNext())
        {
            if (segEnum.Current is not StraightDimension segment)
                continue;

            info.Segments.Add(BuildSegmentInfo(segment));
        }

        info.Bounds ??= CombineBounds(info.Segments.Select(static s => s.Bounds));
        info.Orientation = DetermineDimensionOrientation(info.Segments);
        return info;
    }

    private static DimensionSegmentInfo BuildSegmentInfo(StraightDimension segment)
    {
        var start = segment.StartPoint;
        var end = segment.EndPoint;

        return new DimensionSegmentInfo
        {
            Id = segment.GetIdentifier().ID,
            StartX = System.Math.Round(start.X, 1),
            StartY = System.Math.Round(start.Y, 1),
            EndX = System.Math.Round(end.X, 1),
            EndY = System.Math.Round(end.Y, 1),
            Bounds = TryGetBounds(segment) ?? CreateBoundsFromSegmentPoints(start, end),
            TextBounds = TryGetTextBounds(segment)
        };
    }
}
