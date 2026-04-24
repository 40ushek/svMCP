using System.Collections.Generic;
using System.Linq;
using TeklaMcpServer.Api.Algorithms.Marks;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class ForceDirectedMarkPlacerTests
{
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

    private static ForceDirectedMarkItem CreateMark(int id, double x, double y) => new()
    {
        Id = id,
        Cx = x,
        Cy = y,
        Width = 10.0,
        Height = 10.0,
        CanMove = true
    };

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
