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

        var result = new DrawingViewsResult { SheetWidth = sheetW, SheetHeight = sheetH };
        foreach (var v in EnumerateViews(activeDrawing))
            result.Views.Add(ToInfo(v));

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

    private static Vector? TryGetNormal(CoordinateSystem coordinateSystem)
    {
        if (coordinateSystem?.AxisX == null || coordinateSystem.AxisY == null)
            return null;

        return coordinateSystem.AxisX.Cross(coordinateSystem.AxisY);
    }
}
