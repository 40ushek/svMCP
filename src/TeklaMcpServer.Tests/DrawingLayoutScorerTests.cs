using TeklaMcpServer.Api.Drawing;
using TeklaMcpServer.Api.Drawing.ViewLayout;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DrawingLayoutScorerTests
{
    [Fact]
    public void Score_UsesBoundingBox_WhenAvailable()
    {
        var context = CreateContext(
            sheetWidth: 200,
            sheetHeight: 100,
            views:
            [
                CreateView(
                    id: 1,
                    semanticKind: "BaseProjected",
                    scale: 20,
                    originX: 100,
                    originY: 100,
                    width: 80,
                    height: 40,
                    bboxMinX: 10,
                    bboxMinY: 20,
                    bboxMaxX: 30,
                    bboxMaxY: 50)
            ]);

        var score = new DrawingLayoutScorer().Score(context);

        Assert.Equal(1, score.Breakdown.ScoredViewCount);
        Assert.Equal(1, score.Breakdown.BBoxRectCount);
        Assert.Equal(0, score.Breakdown.FallbackRectCount);
        Assert.Equal(600, score.Breakdown.TotalViewArea, 3);
    }

    [Fact]
    public void Score_FallsBackToOriginAndSize_WhenBoundingBoxIsMissing()
    {
        var context = CreateContext(
            sheetWidth: 200,
            sheetHeight: 100,
            views:
            [
                CreateView(
                    id: 1,
                    semanticKind: "BaseProjected",
                    scale: 20,
                    originX: 50,
                    originY: 40,
                    width: 20,
                    height: 10)
            ]);

        var score = new DrawingLayoutScorer().Score(context);

        Assert.Equal(1, score.Breakdown.ScoredViewCount);
        Assert.Equal(0, score.Breakdown.BBoxRectCount);
        Assert.Equal(1, score.Breakdown.FallbackRectCount);
        Assert.Equal(200, score.Breakdown.TotalViewArea, 3);
    }

    [Fact]
    public void Score_UsesUnionArea_ForReservedLayoutInFillRatio()
    {
        var context = CreateContext(
            sheetWidth: 100,
            sheetHeight: 100,
            views:
            [
                CreateView(
                    id: 1,
                    semanticKind: "BaseProjected",
                    scale: 20,
                    originX: 35,
                    originY: 25,
                    width: 70,
                    height: 50,
                    bboxMinX: 0,
                    bboxMinY: 0,
                    bboxMaxX: 70,
                    bboxMaxY: 50)
            ],
            reservedAreas:
            [
                new ReservedRect(0, 0, 20, 100),
                new ReservedRect(10, 0, 30, 100)
            ]);

        var score = new DrawingLayoutScorer().Score(context);

        Assert.Equal(10_000, score.Breakdown.SheetArea, 3);
        Assert.Equal(3_000, score.Breakdown.ReservedAreaUnion, 3);
        Assert.Equal(7_000, score.Breakdown.AvailableSheetArea, 3);
        Assert.Equal(3_500, score.Breakdown.TotalViewArea, 3);
        Assert.Equal(0.5, score.Breakdown.FillRatioRaw, 3);
        Assert.Equal(0.5, score.Breakdown.FillRatioScore, 3);
    }

    [Fact]
    public void Score_ExcludesDetailViews_FromUniformScale()
    {
        var context = CreateContext(
            sheetWidth: 200,
            sheetHeight: 100,
            views:
            [
                CreateView(1, "BaseProjected", 10, 10, 10, 20, 20, 0, 0, 20, 20),
                CreateView(2, "Section", 10, 40, 10, 20, 20, 30, 0, 50, 20),
                CreateView(3, "Detail", 2, 70, 10, 20, 20, 60, 0, 80, 20)
            ]);

        var score = new DrawingLayoutScorer().Score(context);

        Assert.Equal(2, score.Breakdown.NonDetailViewCount);
        Assert.Equal(1.0, score.Breakdown.UniformScaleScore, 6);
    }

    [Fact]
    public void Score_Detects_ViewAndReservedOverlaps()
    {
        var context = CreateContext(
            sheetWidth: 100,
            sheetHeight: 100,
            views:
            [
                CreateView(1, "BaseProjected", 10, 20, 20, 30, 30, 10, 10, 40, 40),
                CreateView(2, "BaseProjected", 10, 35, 35, 30, 30, 25, 25, 55, 55)
            ],
            reservedAreas:
            [
                new ReservedRect(0, 0, 15, 15),
                new ReservedRect(5, 5, 20, 20)
            ]);

        var score = new DrawingLayoutScorer().Score(context);

        Assert.Equal(1, score.Breakdown.ViewOverlapCount);
        Assert.Equal(225, score.Breakdown.ViewOverlapArea, 3);
        Assert.Equal(1, score.Breakdown.ReservedOverlapCount);
        Assert.Equal(100, score.Breakdown.ReservedOverlapArea, 3);
        Assert.True(score.Breakdown.ViewOverlapPenalty > 0);
        Assert.True(score.Breakdown.ReservedOverlapPenalty > 0);
    }

    [Fact]
    public void Score_ReportsMissingViewRect_WhenWorkspaceCannotBuildLayoutRect()
    {
        var context = CreateContext(
            sheetWidth: 100,
            sheetHeight: 100,
            views:
            [
                CreateView(1, "BaseProjected", 10, 20, 20, 0, 30)
            ]);

        var score = new DrawingLayoutScorer().Score(context);

        Assert.Equal(0, score.Breakdown.ScoredViewCount);
        Assert.Contains("score:view-rect-missing:view=1", score.Diagnostics);
    }

    [Fact]
    public void Score_AcceptsLayoutCandidate()
    {
        var candidate = new DrawingLayoutCandidate
        {
            Name = "passive",
            Sheet = new DrawingSheetContext
            {
                Width = 100,
                Height = 100
            },
            Views =
            [
                new DrawingLayoutCandidateView
                {
                    Id = 1,
                    ViewType = "FrontView",
                    SemanticKind = "BaseProjected",
                    Scale = 20,
                    OriginX = 20,
                    OriginY = 20,
                    Width = 30,
                    Height = 30,
                    BBoxMinX = 5,
                    BBoxMinY = 5,
                    BBoxMaxX = 35,
                    BBoxMaxY = 35
                }
            ]
        };

        var score = new DrawingLayoutScorer().Score(candidate);

        Assert.Equal(1, score.Breakdown.ScoredViewCount);
        Assert.Equal(900, score.Breakdown.TotalViewArea, 3);
    }

    private static DrawingContext CreateContext(
        double sheetWidth,
        double sheetHeight,
        IReadOnlyList<DrawingViewInfo> views,
        IReadOnlyList<ReservedRect>? reservedAreas = null)
    {
        return new DrawingContext
        {
            Sheet = new DrawingSheetContext
            {
                Width = sheetWidth,
                Height = sheetHeight
            },
            Views = views.ToList(),
            ReservedLayout = new DrawingReservedLayoutContext
            {
                Areas = reservedAreas?.ToList() ?? []
            }
        };
    }

    private static DrawingViewInfo CreateView(
        int id,
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
            ViewType = "FrontView",
            SemanticKind = semanticKind,
            Name = $"view-{id}",
            OriginX = originX,
            OriginY = originY,
            Scale = scale,
            Width = width,
            Height = height,
            BBoxMinX = bboxMinX,
            BBoxMinY = bboxMinY,
            BBoxMaxX = bboxMaxX,
            BBoxMaxY = bboxMaxY
        };
    }
}
