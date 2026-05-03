using TeklaMcpServer.Api.Drawing;
using TeklaMcpServer.Api.Drawing.ViewLayout;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DrawingLayoutWorkspaceTests
{
    [Fact]
    public void From_BuildsViewLookupAndSheetShortcuts()
    {
        var context = CreateContext(
            sheetWidth: 420,
            sheetHeight: 297,
            margin: 10,
            sheetMargin: 7,
            views:
            [
                CreateView(10, "FrontView", "BaseProjected", 20, 100, 80, 50, 30),
                CreateView(20, "SectionView", "Section", 20, 180, 80, 40, 20)
            ],
            reservedTables:
            [
                new LayoutTableGeometryInfo()
            ],
            reservedAreas:
            [
                new ReservedRect(300, 0, 420, 80)
            ]);

        var workspace = DrawingLayoutWorkspace.From(context);

        Assert.Same(context, workspace.Source);
        Assert.Equal(420, workspace.SheetWidth);
        Assert.Equal(297, workspace.SheetHeight);
        Assert.Equal(10, workspace.Margin);
        Assert.Equal(7, workspace.SheetMargin);
        Assert.Single(workspace.ReservedTables);
        Assert.Single(workspace.ReservedAreas);
        Assert.Equal([10, 20], workspace.Views.Select(static view => view.Id));
        Assert.Equal(ViewSemanticKind.BaseProjected, workspace.GetSemanticKind(10));
        Assert.Equal(ViewSemanticKind.Section, workspace.GetSemanticKind(20));
        Assert.Equal(ViewSemanticKind.Other, workspace.GetSemanticKind(999));
        Assert.Same(workspace.Views[1], workspace.TryGetView(20));
        Assert.Null(workspace.TryGetView(999));
    }

    [Fact]
    public void Workspace_StoresMutablePlanningGeometry()
    {
        var workspace = DrawingLayoutWorkspace.From(CreateContext(
            views:
            [
                CreateView(10, "FrontView", "BaseProjected", 20, 100, 80, 50, 30)
            ]));
        var actualRects = new Dictionary<int, ReservedRect>
        {
            [10] = new ReservedRect(70, 65, 130, 95)
        };
        var frameSizes = new Dictionary<int, (double Width, double Height)>
        {
            [10] = (60, 30)
        };
        var offsets = new Dictionary<int, (double X, double Y)>
        {
            [10] = (2, -1)
        };

        workspace.SetActualViewRects(actualRects);
        workspace.SetSelectedFrameSizes(frameSizes);
        workspace.SetFrameOffsets(offsets);

        Assert.Same(actualRects, workspace.ActualViewRectsById);
        Assert.Same(frameSizes, workspace.SelectedFrameSizesById);
        Assert.Same(offsets, workspace.FrameOffsetsById);
    }

    [Fact]
    public void ViewItem_UsesBoundingBoxAsLayoutRect_WhenAvailable()
    {
        var workspace = DrawingLayoutWorkspace.From(CreateContext(
            views:
            [
                CreateView(
                    id: 10,
                    viewType: "FrontView",
                    semanticKind: "BaseProjected",
                    scale: 20,
                    originX: 100,
                    originY: 80,
                    width: 60,
                    height: 30,
                    bboxMinX: 70,
                    bboxMinY: 60,
                    bboxMaxX: 140,
                    bboxMaxY: 95)
            ]));

        var view = workspace.Views.Single();

        Assert.Equal("bbox", view.LayoutRectSource);
        Assert.NotNull(view.BBoxRect);
        Assert.NotNull(view.LayoutRect);
        Assert.Equal(70, view.LayoutRect!.MinX);
        Assert.Equal(60, view.LayoutRect.MinY);
        Assert.Equal(140, view.LayoutRect.MaxX);
        Assert.Equal(95, view.LayoutRect.MaxY);
    }

    [Fact]
    public void ViewItem_FallsBackToOriginAndSize_WhenBoundingBoxIsMissing()
    {
        var workspace = DrawingLayoutWorkspace.From(CreateContext(
            views:
            [
                CreateView(
                    id: 10,
                    viewType: "FrontView",
                    semanticKind: "BaseProjected",
                    scale: 20,
                    originX: 100,
                    originY: 80,
                    width: 60,
                    height: 30)
            ]));

        var view = workspace.Views.Single();

        Assert.Equal("origin-size", view.LayoutRectSource);
        Assert.Null(view.BBoxRect);
        Assert.NotNull(view.LayoutRect);
        Assert.Equal(70, view.LayoutRect!.MinX);
        Assert.Equal(65, view.LayoutRect.MinY);
        Assert.Equal(130, view.LayoutRect.MaxX);
        Assert.Equal(95, view.LayoutRect.MaxY);
    }

    [Fact]
    public void ViewItem_LeavesLayoutRectMissing_WhenNoUsableGeometryExists()
    {
        var workspace = DrawingLayoutWorkspace.From(CreateContext(
            views:
            [
                CreateView(
                    id: 10,
                    viewType: "FrontView",
                    semanticKind: "BaseProjected",
                    scale: 20,
                    originX: 100,
                    originY: 80,
                    width: 0,
                    height: 30)
            ]));

        var view = workspace.Views.Single();

        Assert.False(view.HasLayoutRect);
        Assert.Equal(string.Empty, view.LayoutRectSource);
        Assert.Null(view.LayoutRect);
    }

    private static DrawingContext CreateContext(
        double sheetWidth = 200,
        double sheetHeight = 100,
        double margin = 0,
        double? sheetMargin = null,
        IReadOnlyList<DrawingViewInfo>? views = null,
        IReadOnlyList<LayoutTableGeometryInfo>? reservedTables = null,
        IReadOnlyList<ReservedRect>? reservedAreas = null)
    {
        return new DrawingContext
        {
            Sheet = new DrawingSheetContext
            {
                Width = sheetWidth,
                Height = sheetHeight
            },
            Views = views?.ToList() ?? [],
            ReservedLayout = new DrawingReservedLayoutContext
            {
                Margin = margin,
                SheetMargin = sheetMargin,
                Tables = reservedTables?.ToList() ?? [],
                Areas = reservedAreas?.ToList() ?? []
            }
        };
    }

    private static DrawingViewInfo CreateView(
        int id,
        string viewType,
        string semanticKind,
        double scale,
        double originX,
        double originY,
        double width,
        double height,
        double? bboxMinX = null,
        double? bboxMinY = null,
        double? bboxMaxX = null,
        double? bboxMaxY = null)
    {
        return new DrawingViewInfo
        {
            Id = id,
            ViewType = viewType,
            SemanticKind = semanticKind,
            Name = $"view-{id}",
            Scale = scale,
            OriginX = originX,
            OriginY = originY,
            Width = width,
            Height = height,
            BBoxMinX = bboxMinX,
            BBoxMinY = bboxMinY,
            BBoxMaxX = bboxMaxX,
            BBoxMaxY = bboxMaxY
        };
    }
}
