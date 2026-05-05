using TeklaMcpServer.Api.Drawing;
using TeklaMcpServer.Api.Drawing.ViewLayout;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DrawingLayoutStabilityAnalyzerTests
{
    [Fact]
    public void Analyze_ReportsStable_WhenRepeatedContextsMatch()
    {
        var first = CreateContext(
            "drawing-guid-1",
            CreateView(1, 10, 10, scale: 20),
            CreateView(2, 40, 10, scale: 20));
        var second = CreateContext(
            "drawing-guid-1",
            CreateView(1, 10, 10, scale: 20),
            CreateView(2, 40, 10, scale: 20));

        var report = new DrawingLayoutStabilityAnalyzer().Analyze(first, second);

        Assert.True(report.IsStable);
        Assert.True(report.SameDrawing);
        Assert.Equal(2, report.ComparableViewCount);
        Assert.Equal(0, report.MovedCount);
        Assert.Equal(0, report.ScaleChangedCount);
        Assert.Equal(0, report.ScoreDelta);
        Assert.Empty(report.Diagnostics);
    }

    [Fact]
    public void Analyze_ReportsMovementAndScoreDelta_WhenSecondRunMovesView()
    {
        var first = CreateContext(
            "drawing-guid-1",
            CreateView(1, 10, 10, scale: 20),
            CreateView(2, 40, 10, scale: 20));
        var second = CreateContext(
            "drawing-guid-1",
            CreateView(1, 20, 10, scale: 20),
            CreateView(2, 40, 10, scale: 20));

        var report = new DrawingLayoutStabilityAnalyzer().Analyze(first, second);

        Assert.False(report.IsStable);
        Assert.Equal(1, report.MovedCount);
        Assert.Equal(10, report.MaxOriginDelta, 3);
        Assert.Contains("layout-stability:moved:view=1:distance=10", report.Diagnostics);
    }

    [Fact]
    public void Analyze_ReportsMissingAddedAndScaleChanges()
    {
        var first = CreateContext(
            "drawing-guid-1",
            CreateView(1, 10, 10, scale: 20),
            CreateView(2, 40, 10, scale: 20));
        var second = CreateContext(
            "drawing-guid-1",
            CreateView(1, 10, 10, scale: 25),
            CreateView(3, 70, 10, scale: 20));

        var report = new DrawingLayoutStabilityAnalyzer().Analyze(first, second);

        Assert.False(report.IsStable);
        Assert.Equal(1, report.MissingFromSecondCount);
        Assert.Equal(1, report.AddedInSecondCount);
        Assert.Equal(1, report.ScaleChangedCount);
        Assert.Contains("layout-stability:missing-second:view=2", report.Diagnostics);
        Assert.Contains("layout-stability:added-second:view=3", report.Diagnostics);
        Assert.Contains("layout-stability:scale-changed:view=1", report.Diagnostics);
    }

    private static DrawingContext CreateContext(
        string drawingGuid,
        params DrawingViewInfo[] views)
        => new()
        {
            Drawing = new DrawingInfo
            {
                Guid = drawingGuid,
                Name = "Assembly drawing",
                Type = "Assembly"
            },
            Sheet = new DrawingSheetContext
            {
                Width = 200,
                Height = 100
            },
            Views = views.ToList()
        };

    private static DrawingViewInfo CreateView(
        int id,
        double originX,
        double originY,
        double scale)
        => new()
        {
            Id = id,
            Name = $"view-{id}",
            ViewType = "FrontView",
            SemanticKind = ViewSemanticKind.BaseProjected.ToString(),
            OriginX = originX,
            OriginY = originY,
            Scale = scale,
            Width = 20,
            Height = 10
        };
}
