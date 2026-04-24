using System.Collections.Generic;
using System.Linq;
using TeklaMcpServer.Api.Algorithms.Marks;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class ForceDirectedMarkPlacerTests
{
    [Fact]
    public void ToPaperSpace_ScalesMarkAndPartGeometryButKeepsAxisDirection()
    {
        var item = new ForceDirectedMarkItem
        {
            Id = 7,
            OwnModelId = 11,
            Cx = 250.0,
            Cy = 500.0,
            Width = 100.0,
            Height = 50.0,
            CanMove = true,
            ConstrainToAxis = true,
            ReturnToAxisLine = true,
            AxisDx = 0.6,
            AxisDy = 0.8,
            AxisOriginX = 125.0,
            AxisOriginY = -250.0,
            LocalCorners = new List<double[]>
            {
                new[] { -50.0, -25.0 },
                new[] { 50.0, -25.0 },
                new[] { 50.0, 25.0 },
                new[] { -50.0, 25.0 }
            },
            OwnPolygon = new List<double[]>
            {
                new[] { 0.0, 0.0 },
                new[] { 250.0, 0.0 },
                new[] { 250.0, 125.0 },
                new[] { 0.0, 125.0 }
            }
        };
        var part = new PartBbox(
            modelId: 11,
            minX: 0.0,
            minY: -250.0,
            maxX: 500.0,
            maxY: 250.0,
            polygon: new List<double[]>
            {
                new[] { 0.0, 0.0 },
                new[] { 500.0, 0.0 },
                new[] { 500.0, 250.0 },
                new[] { 0.0, 250.0 }
            });

        var paperItem = ForceLayoutUnitConverter.ToPaperSpace(item, 25.0);
        var paperPart = ForceLayoutUnitConverter.ToPaperSpace(part, 25.0);

        Assert.Equal(10.0, paperItem.Cx, 6);
        Assert.Equal(20.0, paperItem.Cy, 6);
        Assert.Equal(4.0, paperItem.Width, 6);
        Assert.Equal(2.0, paperItem.Height, 6);
        Assert.Equal(5.0, paperItem.AxisOriginX, 6);
        Assert.Equal(-10.0, paperItem.AxisOriginY, 6);
        Assert.Equal(0.6, paperItem.AxisDx, 6);
        Assert.Equal(0.8, paperItem.AxisDy, 6);
        Assert.Equal(-2.0, paperItem.LocalCorners[0][0], 6);
        Assert.Equal(-1.0, paperItem.LocalCorners[0][1], 6);
        Assert.Equal(10.0, paperItem.OwnPolygon![1][0], 6);
        Assert.Equal(5.0, paperItem.OwnPolygon![2][1], 6);
        Assert.Equal(-10.0, paperPart.MinY, 6);
        Assert.Equal(20.0, paperPart.MaxX, 6);
        Assert.Equal(10.0, paperPart.Polygon![2][1], 6);
    }

    [Fact]
    public void ToPaperSpace_WithNonPositiveScale_UsesIdentityScale()
    {
        var item = CreateMark(id: 1, x: 25.0, y: 50.0, width: 10.0, height: 20.0);
        var part = new PartBbox(modelId: 1, minX: 5.0, minY: 10.0, maxX: 15.0, maxY: 30.0);

        var paperItem = ForceLayoutUnitConverter.ToPaperSpace(item, 0.0);
        var paperPart = ForceLayoutUnitConverter.ToPaperSpace(part, -10.0);

        Assert.Equal(25.0, paperItem.Cx, 6);
        Assert.Equal(50.0, paperItem.Cy, 6);
        Assert.Equal(10.0, paperItem.Width, 6);
        Assert.Equal(20.0, paperItem.Height, 6);
        Assert.Equal(5.0, paperPart.MinX, 6);
        Assert.Equal(30.0, paperPart.MaxY, 6);
    }

    [Fact]
    public void Relax_AfterPaperConversion_ProducesScaleEquivalentPositions()
    {
        var baselineItems = new List<ForceDirectedMarkItem>
        {
            CreateMark(id: 1, x: 0.0, y: 0.0),
            CreateMark(id: 2, x: 0.0, y: 1.0)
        };
        var scaledDrawingItems = new List<ForceDirectedMarkItem>
        {
            CreateMark(id: 1, x: 0.0, y: 0.0, width: 250.0, height: 250.0),
            CreateMark(id: 2, x: 0.0, y: 25.0, width: 250.0, height: 250.0)
        };
        var movableIds = new HashSet<int> { 1, 2 };

        RunPass2(baselineItems, movableIds);
        var scaledPaperItems = ForceLayoutUnitConverter.ToPaperSpace(scaledDrawingItems, 25.0);
        RunPass2(scaledPaperItems, movableIds);

        foreach (var baseline in baselineItems)
        {
            var scaled = scaledPaperItems.Single(item => item.Id == baseline.Id);
            Assert.Equal(baseline.Cx, scaled.Cx, 6);
            Assert.Equal(baseline.Cy, scaled.Cy, 6);
        }
    }

    [Fact]
    public void Relax_WhenTrackedOverlapsClear_ReturnsOverlapsCleared()
    {
        var items = new List<ForceDirectedMarkItem>
        {
            CreateMark(id: 1, x: 0.0, y: 0.0),
            CreateMark(id: 2, x: 0.0, y: 1.0)
        };
        var movableIds = new HashSet<int> { 1, 2 };
        var placer = new ForceDirectedMarkPlacer();

        var result = placer.Relax(
            items,
            [],
            ForcePassOptions.Pass2Default,
            includeMarkRepulsion: true,
            movableIds: movableIds,
            getRemainingOverlapCount: () => CountOverlappingTrackedIds(items, movableIds),
            overlapCheckInterval: 1);

        Assert.Equal(ForceRelaxStopReason.OverlapsCleared, result.StopReason);
        Assert.True(result.Iterations < ForcePassOptions.Pass2Default.MaxIterations);
        Assert.Equal(0, CountOverlappingTrackedIds(items, movableIds));
    }

    [Fact]
    public void Relax_WhenOverlapCallbackNeverClears_DoesNotReturnOverlapsCleared()
    {
        var items = new List<ForceDirectedMarkItem>
        {
            CreateMark(id: 1, x: 0.0, y: 0.0)
        };
        var placer = new ForceDirectedMarkPlacer();

        var result = placer.Relax(
            items,
            [],
            ForcePassOptions.Pass2Default,
            includeMarkRepulsion: true,
            movableIds: new HashSet<int> { 1 },
            getRemainingOverlapCount: () => 1,
            overlapCheckInterval: 1);

        Assert.NotEqual(ForceRelaxStopReason.OverlapsCleared, result.StopReason);
    }

    private static ForceDirectedMarkItem CreateMark(
        int id,
        double x,
        double y,
        double width = 10.0,
        double height = 10.0) => new()
    {
        Id = id,
        Cx = x,
        Cy = y,
        Width = width,
        Height = height,
        CanMove = true
    };

    private static ForceRelaxResult RunPass2(
        IReadOnlyList<ForceDirectedMarkItem> items,
        ISet<int> movableIds)
    {
        var placer = new ForceDirectedMarkPlacer();
        placer.PlaceInitial(items, []);
        return placer.Relax(
            items,
            [],
            ForcePassOptions.Pass2Default,
            includeMarkRepulsion: true,
            movableIds: movableIds,
            getRemainingOverlapCount: () => CountOverlappingTrackedIds(items, movableIds),
            overlapCheckInterval: 1);
    }

    private static int CountOverlappingTrackedIds(
        IReadOnlyList<ForceDirectedMarkItem> items,
        ISet<int> trackedIds)
    {
        var overlappingIds = new HashSet<int>();
        for (var i = 0; i < items.Count; i++)
        for (var j = i + 1; j < items.Count; j++)
        {
            if (!RectanglesOverlap(items[i], items[j]))
                continue;

            overlappingIds.Add(items[i].Id);
            overlappingIds.Add(items[j].Id);
        }

        return overlappingIds.Count(trackedIds.Contains);
    }

    private static bool RectanglesOverlap(ForceDirectedMarkItem a, ForceDirectedMarkItem b)
    {
        var overlapX = Math.Min(a.Cx + (a.Width / 2.0), b.Cx + (b.Width / 2.0))
            - Math.Max(a.Cx - (a.Width / 2.0), b.Cx - (b.Width / 2.0));
        var overlapY = Math.Min(a.Cy + (a.Height / 2.0), b.Cy + (b.Height / 2.0))
            - Math.Max(a.Cy - (a.Height / 2.0), b.Cy - (b.Height / 2.0));
        return overlapX > 0.0 && overlapY > 0.0;
    }
}
