using System;
using System.Collections.Generic;
using System.Linq;
using TeklaMcpServer.Api.Drawing;

namespace TeklaMcpServer.Api.Drawing.ViewLayout;

internal sealed class DrawingLayoutWorkspace
{
    private DrawingLayoutWorkspace(
        DrawingContext source,
        IReadOnlyList<DrawingLayoutViewItem> views)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Views = views ?? throw new ArgumentNullException(nameof(views));
        ViewsById = views.ToDictionary(static view => view.Id);
    }

    public DrawingContext Source { get; }

    public IReadOnlyList<DrawingLayoutViewItem> Views { get; }

    public IReadOnlyDictionary<int, DrawingLayoutViewItem> ViewsById { get; }

    public double SheetWidth => Source.Sheet.Width;

    public double SheetHeight => Source.Sheet.Height;

    public double Margin => Source.ReservedLayout.Margin;

    public double? SheetMargin => Source.ReservedLayout.SheetMargin;

    public IReadOnlyList<LayoutTableGeometryInfo> ReservedTables => Source.ReservedLayout.Tables;

    public IReadOnlyList<ReservedRect> ReservedAreas => Source.ReservedLayout.Areas;

    public List<string> Diagnostics { get; } = new();

    public DrawingLayoutViewItem? TryGetView(int viewId)
        => ViewsById.TryGetValue(viewId, out var view) ? view : null;

    public static DrawingLayoutWorkspace From(DrawingContext source)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));

        var views = source.Views
            .Select(DrawingLayoutViewItem.From)
            .ToList();

        return new DrawingLayoutWorkspace(source, views);
    }
}

internal sealed class DrawingLayoutViewItem
{
    private DrawingLayoutViewItem(
        int id,
        string viewType,
        string semanticKind,
        string name,
        double scale,
        double originX,
        double originY,
        double width,
        double height,
        ReservedRect? bboxRect,
        ReservedRect? layoutRect,
        string layoutRectSource)
    {
        Id = id;
        ViewType = viewType;
        SemanticKind = semanticKind;
        Name = name;
        Scale = scale;
        OriginX = originX;
        OriginY = originY;
        Width = width;
        Height = height;
        BBoxRect = bboxRect;
        LayoutRect = layoutRect;
        LayoutRectSource = layoutRectSource;
    }

    public int Id { get; }

    public string ViewType { get; }

    public string SemanticKind { get; }

    public string Name { get; }

    public double Scale { get; }

    public double OriginX { get; }

    public double OriginY { get; }

    public double Width { get; }

    public double Height { get; }

    public ReservedRect? BBoxRect { get; }

    public ReservedRect? LayoutRect { get; }

    public string LayoutRectSource { get; }

    public bool HasLayoutRect => LayoutRect != null;

    public NeighborRole NeighborRole { get; set; } = NeighborRole.Unknown;

    public SectionPlacementSide SectionPlacementSide { get; set; } = SectionPlacementSide.Unknown;

    public bool IsBaseView { get; set; }

    public int? ParentViewId { get; set; }

    public List<string> Warnings { get; } = new();

    public static DrawingLayoutViewItem From(DrawingViewInfo view)
    {
        if (view == null)
            throw new ArgumentNullException(nameof(view));

        var bboxRect = TryBuildBBoxRect(view);
        var layoutRect = bboxRect ?? TryBuildOriginSizeRect(view);
        var layoutRectSource = bboxRect != null
            ? "bbox"
            : layoutRect != null
                ? "origin-size"
                : string.Empty;

        return new DrawingLayoutViewItem(
            view.Id,
            view.ViewType,
            view.SemanticKind,
            view.Name,
            view.Scale,
            view.OriginX,
            view.OriginY,
            view.Width,
            view.Height,
            bboxRect,
            layoutRect,
            layoutRectSource);
    }

    private static ReservedRect? TryBuildBBoxRect(DrawingViewInfo view)
    {
        if (view.BBoxMinX.HasValue &&
            view.BBoxMinY.HasValue &&
            view.BBoxMaxX.HasValue &&
            view.BBoxMaxY.HasValue &&
            view.BBoxMaxX.Value > view.BBoxMinX.Value &&
            view.BBoxMaxY.Value > view.BBoxMinY.Value)
        {
            return new ReservedRect(
                view.BBoxMinX.Value,
                view.BBoxMinY.Value,
                view.BBoxMaxX.Value,
                view.BBoxMaxY.Value);
        }

        return null;
    }

    private static ReservedRect? TryBuildOriginSizeRect(DrawingViewInfo view)
    {
        if (view.Width <= 0 || view.Height <= 0)
            return null;

        var halfWidth = view.Width * 0.5;
        var halfHeight = view.Height * 0.5;
        return new ReservedRect(
            view.OriginX - halfWidth,
            view.OriginY - halfHeight,
            view.OriginX + halfWidth,
            view.OriginY + halfHeight);
    }
}
