using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;

namespace TeklaMcpServer.Api.Drawing;

public sealed partial class TeklaDrawingViewApi
{
    public DrawingViewsResult GetViews()
    {
        var activeDrawing = new DrawingHandler().GetActiveDrawing();
        if (activeDrawing == null)
            throw new DrawingNotOpenException();

        double sheetW = 0;
        double sheetH = 0;
        try
        {
            var ss = activeDrawing.Layout.SheetSize;
            sheetW = ss.Width;
            sheetH = ss.Height;
        }
        catch
        {
        }

        var actualRects = DrawingViewSheetGeometry.BuildActualViewRects(activeDrawing);
        var result = new DrawingViewsResult { SheetWidth = sheetW, SheetHeight = sheetH };
        foreach (var v in EnumerateViews(activeDrawing))
            result.Views.Add(ToInfo(v, actualRects));

        return result;
    }

    public DrawingReservedAreasResult GetReservedAreas(double margin)
    {
        var drawing = new DrawingHandler().GetActiveDrawing()
            ?? throw new DrawingNotOpenException();

        var (sheetMargin, tables) = DrawingReservedAreaReader.ReadLayoutInfo();
        var merged = DrawingReservedAreaReader.Read(drawing, margin, 0.0, preloadedTables: tables);

        return new DrawingReservedAreasResult
        {
            SheetWidth  = drawing.Layout.SheetSize.Width,
            SheetHeight = drawing.Layout.SheetSize.Height,
            Margin      = margin,
            SheetMargin = sheetMargin,
            Tables      = tables,
            MergedAreas = merged
        };
    }

    public DrawingDetailMarksResult GetDetailMarks()
    {
        var drawing = new DrawingHandler().GetActiveDrawing();
        if (drawing == null)
            throw new DrawingNotOpenException();

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

        var result = new DrawingDetailMarksResult
        {
            SheetWidth = sheetW,
            SheetHeight = sheetH
        };
        var detailViewsByName = EnumerateViews(drawing)
            .Where(view => ViewSemanticClassifier.Classify(view.ViewType) == ViewSemanticKind.Detail)
            .GroupBy(view => view.Name ?? string.Empty)
            .ToDictionary(group => group.Key, group => group.First());

        foreach (var view in EnumerateViews(drawing))
        {
            var detailMarks = view.GetAllObjects(typeof(DetailMark));
            while (detailMarks.MoveNext())
            {
                if (detailMarks.Current is not DetailMark detailMark)
                    continue;

                var markName = detailMark.Attributes?.MarkName ?? string.Empty;
                detailViewsByName.TryGetValue(markName, out var detailView);
                var relatedObjects = ReadRelatedObjects(detailMark.GetRelatedObjects());

                result.DetailMarks.Add(new DetailMarkInfo
                {
                    Id = detailMark.GetIdentifier().ID,
                    OwnerViewId = view.GetIdentifier().ID,
                    OwnerViewType = view.ViewType.ToString(),
                    OwnerViewName = view.Name ?? string.Empty,
                    MarkName = markName,
                    DetailViewId = detailView?.GetIdentifier().ID,
                    DetailViewType = detailView?.ViewType.ToString() ?? string.Empty,
                    DetailViewName = detailView?.Name ?? string.Empty,
                    DetailViewScale = detailView?.Attributes.Scale,
                    RelatedObjects = relatedObjects,
                    CenterPoint = ToArray(detailMark.CenterPoint),
                    BoundaryPoint = ToArray(detailMark.BoundaryPoint),
                    LabelPoint = ToArray(detailMark.LabelPoint)
                });
            }
        }

        return result;
    }

    public DrawingSectionMarksResult GetSectionMarks()
    {
        var drawing = new DrawingHandler().GetActiveDrawing();
        if (drawing == null)
            throw new DrawingNotOpenException();

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

        var result = new DrawingSectionMarksResult
        {
            SheetWidth = sheetW,
            SheetHeight = sheetH
        };

        foreach (var view in EnumerateViews(drawing))
        {
            var sectionMarks = view.GetAllObjects(typeof(SectionMark));
            while (sectionMarks.MoveNext())
            {
                if (sectionMarks.Current is not SectionMark sectionMark)
                    continue;

                result.SectionMarks.Add(new SectionMarkInfo
                {
                    Id = sectionMark.GetIdentifier().ID,
                    OwnerViewId = view.GetIdentifier().ID,
                    OwnerViewType = view.ViewType.ToString(),
                    OwnerViewName = view.Name ?? string.Empty,
                    RelatedObjects = ReadRelatedObjects(sectionMark.GetRelatedObjects())
                });
            }
        }

        return result;
    }

    public DrawingSectionPlacementSidesResult GetSectionPlacementSides()
    {
        var drawing = new DrawingHandler().GetActiveDrawing();
        if (drawing == null)
            throw new DrawingNotOpenException();

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

        var views = EnumerateViews(drawing).ToList();
        var baseViewSelection = BaseViewSelection.Select(views);
        var baseView = baseViewSelection.View;
        var result = new DrawingSectionPlacementSidesResult
        {
            SheetWidth = sheetW,
            SheetHeight = sheetH,
            BaseViewId = baseView?.GetIdentifier().ID,
            BaseViewType = baseView?.ViewType.ToString() ?? string.Empty,
            BaseViewSelectionKind = baseViewSelection.SelectionKind.ToString(),
            BaseViewReason = baseViewSelection.Reason,
            BaseViewIsFallback = baseViewSelection.IsFallback
        };

        if (baseView == null)
            return result;

        var resolver = new SectionPlacementSideResolver(new Model());
        foreach (var view in views.Where(v => v.ViewType == View.ViewTypes.SectionView))
        {
            var placementSide = resolver.Resolve(drawing, baseView, view);
            var hasCoordinateSystems = resolver.TryGetDebugCoordinateSystems(
                drawing,
                baseView,
                view,
                out var referenceCoordinateSystem,
                out var viewCoordinateSystem,
                out _,
                out _);
            result.Sections.Add(new SectionPlacementSideInfo
            {
                Id = view.GetIdentifier().ID,
                Name = view.Name ?? string.Empty,
                PlacementSide = placementSide.PlacementSide.ToString(),
                Reason = placementSide.Reason,
                IsFallback = placementSide.IsFallback,
                Scale = view.Attributes.Scale,
                Width = view.Width,
                Height = view.Height,
                ReferenceAxisX = hasCoordinateSystems ? ToArray(referenceCoordinateSystem.AxisX) : [],
                ReferenceAxisY = hasCoordinateSystems ? ToArray(referenceCoordinateSystem.AxisY) : [],
                ViewAxisX = hasCoordinateSystems ? ToArray(viewCoordinateSystem.AxisX) : [],
                ViewAxisY = hasCoordinateSystems ? ToArray(viewCoordinateSystem.AxisY) : [],
                ViewNormal = hasCoordinateSystems ? ToArray(TryGetNormal(viewCoordinateSystem)) : []
            });
        }

        return result;
    }

    private static double[] ToArray(Vector? vector)
        => vector == null ? [] : [vector.X, vector.Y, vector.Z];

    private static double[] ToArray(Point? point)
        => point == null ? [] : [point.X, point.Y, point.Z];

    private static List<RelatedDrawingObjectInfo> ReadRelatedObjects(DrawingObjectEnumerator? relatedEnumerator)
    {
        var relatedObjects = new List<RelatedDrawingObjectInfo>();
        if (relatedEnumerator == null)
            return relatedObjects;

        while (relatedEnumerator.MoveNext())
        {
            if (relatedEnumerator.Current is not DrawingObject relatedObject)
                continue;

            relatedObjects.Add(new RelatedDrawingObjectInfo
            {
                Id = relatedObject.GetIdentifier().ID,
                ObjectType = relatedObject.GetType().Name,
                ViewType = relatedObject is View relatedView ? relatedView.ViewType.ToString() : string.Empty,
                ViewName = relatedObject is View namedView ? namedView.Name ?? string.Empty : string.Empty
            });
        }

        return relatedObjects;
    }

    private static Vector? TryGetNormal(CoordinateSystem coordinateSystem)
    {
        if (coordinateSystem?.AxisX == null || coordinateSystem.AxisY == null)
            return null;

        return coordinateSystem.AxisX.Cross(coordinateSystem.AxisY);
    }
}
