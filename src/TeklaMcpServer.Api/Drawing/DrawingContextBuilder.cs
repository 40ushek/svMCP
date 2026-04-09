using System.Linq;
using TeklaMcpServer.Api.Drawing.ViewLayout;

namespace TeklaMcpServer.Api.Drawing;

public sealed class DrawingContextBuilder
{
    public DrawingContext Build(
        DrawingInfo drawing,
        DrawingViewsResult views,
        DrawingReservedAreasResult reservedAreas)
    {
        return new DrawingContext
        {
            Drawing = CloneDrawing(drawing),
            SheetWidth = views.SheetWidth,
            SheetHeight = views.SheetHeight,
            Margin = reservedAreas.Margin,
            SheetMargin = reservedAreas.SheetMargin,
            Views = views.Views.Select(CloneView).ToList(),
            Tables = reservedAreas.Tables.Select(CloneTable).ToList(),
            ReservedAreas = reservedAreas.MergedAreas.Select(CloneReservedRect).ToList()
        };
    }

    private static DrawingInfo CloneDrawing(DrawingInfo drawing)
    {
        return new DrawingInfo
        {
            Guid = drawing.Guid,
            Name = drawing.Name,
            Mark = drawing.Mark,
            Title1 = drawing.Title1,
            Title2 = drawing.Title2,
            Title3 = drawing.Title3,
            Type = drawing.Type,
            Status = drawing.Status
        };
    }

    private static DrawingViewInfo CloneView(DrawingViewInfo view)
    {
        return new DrawingViewInfo
        {
            Id = view.Id,
            ViewType = view.ViewType,
            SemanticKind = view.SemanticKind,
            Name = view.Name,
            OriginX = view.OriginX,
            OriginY = view.OriginY,
            Scale = view.Scale,
            Width = view.Width,
            Height = view.Height,
            BBoxMinX = view.BBoxMinX,
            BBoxMinY = view.BBoxMinY,
            BBoxMaxX = view.BBoxMaxX,
            BBoxMaxY = view.BBoxMaxY
        };
    }

    private static LayoutTableGeometryInfo CloneTable(LayoutTableGeometryInfo table)
    {
        return new LayoutTableGeometryInfo
        {
            TableId = table.TableId,
            Name = table.Name,
            OverlapWithViews = table.OverlapWithViews,
            HasGeometry = table.HasGeometry,
            Bounds = table.Bounds == null
                ? null
                : new ReservedRect(
                    table.Bounds.MinX,
                    table.Bounds.MinY,
                    table.Bounds.MaxX,
                    table.Bounds.MaxY)
        };
    }

    private static ReservedRect CloneReservedRect(ReservedRect rect) =>
        new(rect.MinX, rect.MinY, rect.MaxX, rect.MaxY);
}
