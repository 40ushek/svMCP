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
    public void Score_PenalizesViewsTouchingUsableEdge()
    {
        var centered = CreateContext(
            sheetWidth: 100,
            sheetHeight: 100,
            views:
            [
                CreateView(1, "BaseProjected", 20, 50, 50, 30, 30, 35, 35, 65, 65)
            ],
            reservedMargin: 10);
        var touching = CreateContext(
            sheetWidth: 100,
            sheetHeight: 100,
            views:
            [
                CreateView(1, "BaseProjected", 20, 25, 50, 30, 30, 10, 35, 40, 65)
            ],
            reservedMargin: 10);

        var centeredScore = new DrawingLayoutScorer().Score(centered);
        var touchingScore = new DrawingLayoutScorer().Score(touching);

        Assert.True(touchingScore.Breakdown.EdgeMarginPenalty > centeredScore.Breakdown.EdgeMarginPenalty);
        Assert.True(centeredScore.TotalScore > touchingScore.TotalScore);
    }

    [Fact]
    public void Score_PenalizesCandidatePreferredSideMismatch()
    {
        var strict = CreateCandidateWithPlacementSides("Top", "Top");
        var fallback = CreateCandidateWithPlacementSides("Top", "Left");

        var scorer = new DrawingLayoutScorer();
        var strictScore = scorer.Score(strict);
        var fallbackScore = scorer.Score(fallback);

        Assert.Equal(0.0, strictScore.Breakdown.PreferredSidePenalty, 6);
        Assert.Equal(1.0, fallbackScore.Breakdown.PreferredSidePenalty, 6);
        Assert.True(strictScore.TotalScore > fallbackScore.TotalScore);
    }

    [Fact]
    public void Score_PenalizesLessCompactCandidate()
    {
        var compact = CreateCandidateWithRects(
            new ReservedRect(20, 20, 40, 40),
            new ReservedRect(45, 20, 65, 40));
        var spread = CreateCandidateWithRects(
            new ReservedRect(5, 5, 25, 25),
            new ReservedRect(75, 75, 95, 95));

        var scorer = new DrawingLayoutScorer();
        var compactScore = scorer.Score(compact);
        var spreadScore = scorer.Score(spread);

        Assert.True(spreadScore.Breakdown.CompactnessPenalty > compactScore.Breakdown.CompactnessPenalty);
        Assert.True(compactScore.TotalScore > spreadScore.TotalScore);
    }

    [Fact]
    public void Score_PenalizesCandidateFallbackStackOrderInversions()
    {
        var ordered = CreateCandidateWithStackOrder([10, 20], [10, 20]);
        var inverted = CreateCandidateWithStackOrder([10, 20], [20, 10]);

        var scorer = new DrawingLayoutScorer();
        var orderedScore = scorer.Score(ordered);
        var invertedScore = scorer.Score(inverted);

        Assert.Equal(0.0, orderedScore.Breakdown.StackOrderPenalty, 6);
        Assert.Equal(1.0, invertedScore.Breakdown.StackOrderPenalty, 6);
        Assert.True(orderedScore.TotalScore > invertedScore.TotalScore);
        Assert.Contains(invertedScore.Diagnostics, item => item.Contains("score:stack-order"));
    }

    [Fact]
    public void Score_PenalizesProjectedMainViewsOffAxis()
    {
        var aligned = CreateProjectedAxisCandidate(topCenterX: 50, backCenterY: 50);
        var shifted = CreateProjectedAxisCandidate(topCenterX: 80, backCenterY: 80);

        var scorer = new DrawingLayoutScorer();
        var alignedScore = scorer.Score(aligned);
        var shiftedScore = scorer.Score(shifted);

        Assert.Equal(0.0, alignedScore.Breakdown.ProjectedAxisPenalty, 6);
        Assert.True(shiftedScore.Breakdown.ProjectedAxisPenalty > alignedScore.Breakdown.ProjectedAxisPenalty);
        Assert.True(alignedScore.TotalScore > shiftedScore.TotalScore);
    }

    [Fact]
    public void Score_UsesTopViewAsProjectedAxisReference_WhenFrontViewIsMissing()
    {
        var aligned = CreateProjectedAxisCandidateWithoutFront(bottomCenterX: 50);
        var shifted = CreateProjectedAxisCandidateWithoutFront(bottomCenterX: 80);

        var scorer = new DrawingLayoutScorer();
        var alignedScore = scorer.Score(aligned);
        var shiftedScore = scorer.Score(shifted);

        Assert.Equal(0.0, alignedScore.Breakdown.ProjectedAxisPenalty, 6);
        Assert.True(shiftedScore.Breakdown.ProjectedAxisPenalty > alignedScore.Breakdown.ProjectedAxisPenalty);
        Assert.True(alignedScore.TotalScore > shiftedScore.TotalScore);
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
                    LayoutRect = new ReservedRect(5, 5, 35, 35)
                }
            ]
        };

        var score = new DrawingLayoutScorer().Score(candidate);

        Assert.Equal(1, score.Breakdown.ScoredViewCount);
        Assert.Equal(900, score.Breakdown.TotalViewArea, 3);
    }

    [Fact]
    public void Evaluate_ReportsCandidateFeasibilityDiagnostics()
    {
        var candidate = new DrawingLayoutCandidate
        {
            Name = "overlap",
            Sheet = new DrawingSheetContext
            {
                Width = 100,
                Height = 100
            },
            ReservedLayout = new DrawingReservedLayoutContext
            {
                Areas =
                [
                    new ReservedRect(0, 0, 20, 20)
                ]
            },
            Diagnostics =
            [
                "candidate:source=test"
            ],
            Views =
            [
                new DrawingLayoutCandidateView
                {
                    Id = 1,
                    ViewType = "FrontView",
                    SemanticKind = "BaseProjected",
                    Scale = 20,
                    Width = 30,
                    Height = 30,
                    LayoutRect = new ReservedRect(10, 10, 40, 40)
                },
                new DrawingLayoutCandidateView
                {
                    Id = 2,
                    ViewType = "TopView",
                    SemanticKind = "BaseProjected",
                    Scale = 20,
                    Width = 30,
                    Height = 30,
                    LayoutRect = new ReservedRect(25, 25, 55, 55)
                },
                new DrawingLayoutCandidateView
                {
                    Id = 3,
                    ViewType = "SectionView",
                    SemanticKind = "Section",
                    Scale = 20,
                    Width = 0,
                    Height = 30
                }
            ]
        };

        var evaluation = new DrawingLayoutScorer().Evaluate(candidate);

        Assert.False(evaluation.IsFeasible);
        Assert.Equal(candidate, evaluation.Candidate);
        Assert.Equal(1, evaluation.Validation.MissingRectCount);
        Assert.Equal(1, evaluation.Validation.ViewOverlapCount);
        Assert.Equal(1, evaluation.Validation.ReservedOverlapCount);
        Assert.Contains("candidate:source=test", evaluation.Validation.Diagnostics);
        Assert.Contains("score:view-rect-missing:view=3", evaluation.Validation.Diagnostics);
    }

    private static DrawingContext CreateContext(
        double sheetWidth,
        double sheetHeight,
        IReadOnlyList<DrawingViewInfo> views,
        IReadOnlyList<ReservedRect>? reservedAreas = null,
        double reservedMargin = 0.0)
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
                Margin = reservedMargin,
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

    private static DrawingLayoutCandidate CreateCandidateWithRects(params ReservedRect[] rects)
    {
        var candidate = new DrawingLayoutCandidate
        {
            Name = "candidate",
            Sheet = new DrawingSheetContext
            {
                Width = 100,
                Height = 100
            }
        };

        for (var i = 0; i < rects.Length; i++)
        {
            var rect = rects[i];
            candidate.Views.Add(new DrawingLayoutCandidateView
            {
                Id = i + 1,
                ViewType = "FrontView",
                SemanticKind = "BaseProjected",
                Scale = 20,
                Width = rect.Width,
                Height = rect.Height,
                LayoutRect = rect
            });
        }

        return candidate;
    }

    private static DrawingLayoutCandidate CreateCandidateWithPlacementSides(
        string preferredPlacementSide,
        string actualPlacementSide)
    {
        return new DrawingLayoutCandidate
        {
            Name = "candidate",
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
                    Width = 30,
                    Height = 30,
                    LayoutRect = new ReservedRect(35, 35, 65, 65)
                },
                new DrawingLayoutCandidateView
                {
                    Id = 2,
                    ViewType = "SectionView",
                    SemanticKind = "Section",
                    Scale = 20,
                    Width = 20,
                    Height = 10,
                    LayoutRect = new ReservedRect(40, 70, 60, 80),
                    PreferredPlacementSide = preferredPlacementSide,
                    ActualPlacementSide = actualPlacementSide
                }
            ]
        };
    }

    private static DrawingLayoutCandidate CreateCandidateWithStackOrder(
        List<int> expectedViewIds,
        List<int> actualViewIds)
    {
        var candidate = CreateCandidateWithRects(
            new ReservedRect(35, 35, 65, 65),
            new ReservedRect(10, 70, 30, 80),
            new ReservedRect(10, 50, 30, 60));

        candidate.StackOrderGroups.Add(new DrawingLayoutCandidateStackOrderGroup
        {
            PreferredPlacementSide = "Top",
            ActualPlacementSide = "Left",
            ViewType = "SectionView",
            ExpectedViewIds = expectedViewIds,
            ActualViewIds = actualViewIds
        });

        return candidate;
    }

    private static DrawingLayoutCandidate CreateProjectedAxisCandidate(double topCenterX, double backCenterY)
    {
        return new DrawingLayoutCandidate
        {
            Name = "projected-axis",
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
                    Width = 20,
                    Height = 20,
                    LayoutRect = new ReservedRect(40, 40, 60, 60)
                },
                new DrawingLayoutCandidateView
                {
                    Id = 2,
                    ViewType = "TopView",
                    SemanticKind = "BaseProjected",
                    Scale = 20,
                    Width = 20,
                    Height = 10,
                    LayoutRect = new ReservedRect(topCenterX - 10, 70, topCenterX + 10, 80),
                    PreferredPlacementSide = "Top",
                    ActualPlacementSide = "Top"
                },
                new DrawingLayoutCandidateView
                {
                    Id = 3,
                    ViewType = "BackView",
                    SemanticKind = "BaseProjected",
                    Scale = 20,
                    Width = 10,
                    Height = 20,
                    LayoutRect = new ReservedRect(20, backCenterY - 10, 30, backCenterY + 10),
                    PreferredPlacementSide = "Left",
                    ActualPlacementSide = "Left"
                }
            ]
        };
    }

    private static DrawingLayoutCandidate CreateProjectedAxisCandidateWithoutFront(double bottomCenterX)
    {
        return new DrawingLayoutCandidate
        {
            Name = "projected-axis-no-front",
            Sheet = new DrawingSheetContext
            {
                Width = 100,
                Height = 100
            },
            Views =
            [
                new DrawingLayoutCandidateView
                {
                    Id = 2,
                    ViewType = "TopView",
                    SemanticKind = "BaseProjected",
                    Scale = 20,
                    Width = 20,
                    Height = 10,
                    LayoutRect = new ReservedRect(40, 70, 60, 80),
                    PreferredPlacementSide = "Top",
                    ActualPlacementSide = "Top"
                },
                new DrawingLayoutCandidateView
                {
                    Id = 3,
                    ViewType = "BottomView",
                    SemanticKind = "BaseProjected",
                    Scale = 20,
                    Width = 20,
                    Height = 10,
                    LayoutRect = new ReservedRect(bottomCenterX - 10, 20, bottomCenterX + 10, 30),
                    PreferredPlacementSide = "Bottom",
                    ActualPlacementSide = "Bottom"
                }
            ]
        };
    }
}
