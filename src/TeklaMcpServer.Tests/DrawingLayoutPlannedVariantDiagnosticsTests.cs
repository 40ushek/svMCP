using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DrawingLayoutPlannedVariantDiagnosticsTests
{
    [Fact]
    public void BuildSummary_ReportsMovedNonDetailViewsAndKeepsDetailSeparate()
    {
        var baseline = new[]
        {
            CreateView(1, "BaseProjected", 10, 10, 0, 0, 20, 20),
            CreateView(2, "Detail", 80, 80, 70, 70, 90, 90)
        };
        var variant = new[]
        {
            CreateView(1, "BaseProjected", 20, 10, 10, 0, 30, 20),
            CreateView(2, "Detail", 80, 80, 70, 70, 90, 90)
        };

        var summary = DrawingLayoutPlannedVariantDiagnostics.BuildSummary(
            baseline,
            null,
            variant,
            null);

        Assert.Equal(1, summary.MovedCount);
        Assert.Equal(0, summary.DetailMovedCount);
        Assert.Equal(10, summary.MaxDelta, 3);
        Assert.Equal(10, summary.AverageDelta, 3);
        Assert.Equal(0, summary.BoundingBoxBefore?.MinX);
        Assert.Equal(0, summary.BoundingBoxBefore?.MinY);
        Assert.Equal(20, summary.BoundingBoxBefore?.MaxX);
        Assert.Equal(20, summary.BoundingBoxBefore?.MaxY);
        Assert.Equal(10, summary.BoundingBoxAfter?.MinX);
        Assert.Equal(0, summary.BoundingBoxAfter?.MinY);
        Assert.Equal(30, summary.BoundingBoxAfter?.MaxX);
        Assert.Equal(20, summary.BoundingBoxAfter?.MaxY);
        Assert.Equal(-1, summary.ReservedOverlapBefore);
        Assert.Equal(-1, summary.ReservedOverlapAfter);
    }

    [Fact]
    public void BuildSummary_CountsMovedDetailViews_WhenVariantMovesThem()
    {
        var baseline = new[]
        {
            CreateView(1, "detail", 10, 10, 0, 0, 20, 20)
        };
        var variant = new[]
        {
            CreateView(1, "DETAIL", 15, 10, 5, 0, 25, 20)
        };

        var summary = DrawingLayoutPlannedVariantDiagnostics.BuildSummary(
            baseline,
            null,
            variant,
            null);

        Assert.Equal(1, summary.MovedCount);
        Assert.Equal(1, summary.DetailMovedCount);
        Assert.Null(summary.BoundingBoxBefore);
        Assert.Null(summary.BoundingBoxAfter);
    }

    private static DrawingLayoutPlannedView CreateView(
        int id,
        string semanticKind,
        double originX,
        double originY,
        double minX,
        double minY,
        double maxX,
        double maxY)
        => new()
        {
            Id = id,
            SemanticKind = semanticKind,
            OriginX = originX,
            OriginY = originY,
            Width = maxX - minX,
            Height = maxY - minY,
            LayoutRect = new ReservedRect(minX, minY, maxX, maxY)
        };
}
