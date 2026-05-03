using System;
using System.Collections.Generic;
using System.Linq;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using TeklaMcpServer.Api.Drawing;

namespace TeklaMcpServer.Api.Drawing.ViewLayout;

internal sealed class DrawingLayoutWorkspace
{
    private DrawingLayoutWorkspace(
        DrawingContext source,
        IReadOnlyList<DrawingLayoutViewItem> views,
        IReadOnlyList<View> runtimeViews)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Views = views ?? throw new ArgumentNullException(nameof(views));
        RuntimeViews = runtimeViews ?? throw new ArgumentNullException(nameof(runtimeViews));
        ViewsById = views.ToDictionary(static view => view.Id);
        RuntimeViewsById = runtimeViews.ToDictionary(static view => view.GetIdentifier().ID);
        SemanticKindsById = views.ToDictionary(static view => view.Id, static view => view.SemanticKindValue);
    }

    public DrawingContext Source { get; }

    public IReadOnlyList<DrawingLayoutViewItem> Views { get; }

    public IReadOnlyList<View> RuntimeViews { get; private set; }

    public IReadOnlyDictionary<int, DrawingLayoutViewItem> ViewsById { get; }

    public IReadOnlyDictionary<int, View> RuntimeViewsById { get; private set; }

    public IReadOnlyDictionary<int, ViewSemanticKind> SemanticKindsById { get; }

    public IReadOnlyDictionary<int, double> OriginalScalesById { get; private set; } =
        new Dictionary<int, double>();

    public IReadOnlyDictionary<int, ReservedRect> ActualViewRectsById { get; private set; } =
        new Dictionary<int, ReservedRect>();

    public IReadOnlyDictionary<int, (double Width, double Height)> SelectedFrameSizesById { get; private set; } =
        new Dictionary<int, (double Width, double Height)>();

    public IReadOnlyDictionary<int, (double X, double Y)> FrameOffsetsById { get; private set; } =
        new Dictionary<int, (double X, double Y)>();

    public IReadOnlyDictionary<int, IReadOnlyList<GridAxisInfo>> GridAxesByViewId { get; private set; } =
        new Dictionary<int, IReadOnlyList<GridAxisInfo>>();

    private ViewTopologyGraph? _runtimeTopology;

    public double SheetWidth => Source.Sheet.Width;

    public double SheetHeight => Source.Sheet.Height;

    public double Margin => Source.ReservedLayout.Margin;

    public double? SheetMargin => Source.ReservedLayout.SheetMargin;

    public IReadOnlyList<LayoutTableGeometryInfo> ReservedTables => Source.ReservedLayout.Tables;

    public IReadOnlyList<ReservedRect> ReservedAreas => Source.ReservedLayout.Areas;

    public List<string> Diagnostics { get; } = new();

    public DrawingLayoutViewItem? TryGetView(int viewId)
        => ViewsById.TryGetValue(viewId, out var view) ? view : null;

    public View? TryGetRuntimeView(int viewId)
        => RuntimeViewsById.TryGetValue(viewId, out var view) ? view : null;

    public ViewSemanticKind GetSemanticKind(int viewId)
        => SemanticKindsById.TryGetValue(viewId, out var kind) ? kind : ViewSemanticKind.Other;

    public void SetOriginalScales(IReadOnlyDictionary<int, double> originalScales)
    {
        OriginalScalesById = originalScales ?? throw new ArgumentNullException(nameof(originalScales));
    }

    public void SetActualViewRects(IReadOnlyDictionary<int, ReservedRect> actualRects)
    {
        ActualViewRectsById = actualRects ?? throw new ArgumentNullException(nameof(actualRects));
    }

    public void SetSelectedFrameSizes(IReadOnlyDictionary<int, (double Width, double Height)> frameSizes)
    {
        SelectedFrameSizesById = frameSizes ?? throw new ArgumentNullException(nameof(frameSizes));
    }

    public void SetFrameOffsets(IReadOnlyDictionary<int, (double X, double Y)> frameOffsets)
    {
        FrameOffsetsById = frameOffsets ?? throw new ArgumentNullException(nameof(frameOffsets));
    }

    public void SetRuntimeViews(IReadOnlyList<View> runtimeViews)
    {
        RuntimeViews = runtimeViews ?? throw new ArgumentNullException(nameof(runtimeViews));
        RuntimeViewsById = runtimeViews.ToDictionary(static view => view.GetIdentifier().ID);
        _runtimeTopology = null;
    }

    public void SetGridAxes(IReadOnlyDictionary<int, IReadOnlyList<GridAxisInfo>> gridAxesByViewId)
    {
        GridAxesByViewId = gridAxesByViewId ?? throw new ArgumentNullException(nameof(gridAxesByViewId));
    }

    public ViewTopologyGraph GetTopology(IReadOnlyList<View>? views = null)
    {
        var effectiveViews = views ?? RuntimeViews;
        if (HasSameViewIds(effectiveViews, RuntimeViews))
            return _runtimeTopology ??= ViewTopologyGraph.Build(RuntimeViews);

        return ViewTopologyGraph.Build(effectiveViews);
    }

    public static DrawingLayoutWorkspace From(DrawingContext source)
        => From(source, Array.Empty<View>());

    public static DrawingLayoutWorkspace From(DrawingContext source, IReadOnlyList<View> runtimeViews)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));
        if (runtimeViews == null)
            throw new ArgumentNullException(nameof(runtimeViews));

        var views = source.Views
            .Select(DrawingLayoutViewItem.From)
            .ToList();

        return new DrawingLayoutWorkspace(source, views, runtimeViews);
    }

    private static bool HasSameViewIds(IReadOnlyList<View> left, IReadOnlyList<View> right)
    {
        if (left.Count != right.Count)
            return false;

        for (var i = 0; i < left.Count; i++)
        {
            if (left[i].GetIdentifier().ID != right[i].GetIdentifier().ID)
                return false;
        }

        return true;
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
        SemanticKindValue = Enum.TryParse<ViewSemanticKind>(semanticKind, ignoreCase: true, out var parsed)
            ? parsed
            : ViewSemanticKind.Other;
    }

    public int Id { get; }

    public string ViewType { get; }

    public string SemanticKind { get; }

    public ViewSemanticKind SemanticKindValue { get; }

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
