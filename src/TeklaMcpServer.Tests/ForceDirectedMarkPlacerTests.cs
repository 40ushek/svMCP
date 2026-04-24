using System.Collections.Generic;
using System.Linq;
using TeklaMcpServer.Api.Algorithms.Marks;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class ForceDirectedMarkPlacerTests
{
    [Fact]
    public void CreatePass1ForViewScale_ScalesDistancePolicyOnly()
    {
        var options = ForcePassOptions.CreatePass1ForViewScale(25.0);

        Assert.Equal(100.0, options.IdealDist, 6);
        Assert.Equal(50.0, options.MarkGapMm, 6);
        Assert.Equal(200.0, options.PartRepelRadius, 6);
        Assert.Equal(18.75, options.PartRepelSoftening, 6);
        Assert.Equal(6.25, options.StopEpsilon, 6);
        AssertForceCoefficientsEqual(ForcePassOptions.Pass1Default, options);
    }

    [Fact]
    public void CreatePass2ForViewScale_ScalesDistancePolicyOnly()
    {
        var options = ForcePassOptions.CreatePass2ForViewScale(25.0);

        Assert.Equal(100.0, options.IdealDist, 6);
        Assert.Equal(50.0, options.MarkGapMm, 6);
        Assert.Equal(200.0, options.PartRepelRadius, 6);
        Assert.Equal(18.75, options.PartRepelSoftening, 6);
        Assert.Equal(6.25, options.StopEpsilon, 6);
        AssertForceCoefficientsEqual(ForcePassOptions.Pass2Default, options);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-25.0)]
    public void CreatePassOptionsForNonPositiveScale_UsesIdentityScale(double viewScale)
    {
        var pass1 = ForcePassOptions.CreatePass1ForViewScale(viewScale);
        var pass2 = ForcePassOptions.CreatePass2ForViewScale(viewScale);

        Assert.Equal(4.0, pass1.IdealDist, 6);
        Assert.Equal(2.0, pass1.MarkGapMm, 6);
        Assert.Equal(8.0, pass1.PartRepelRadius, 6);
        Assert.Equal(0.75, pass1.PartRepelSoftening, 6);
        Assert.Equal(0.25, pass1.StopEpsilon, 6);
        AssertForceCoefficientsEqual(ForcePassOptions.Pass1Default, pass1);

        Assert.Equal(4.0, pass2.IdealDist, 6);
        Assert.Equal(2.0, pass2.MarkGapMm, 6);
        Assert.Equal(8.0, pass2.PartRepelRadius, 6);
        Assert.Equal(0.75, pass2.PartRepelSoftening, 6);
        Assert.Equal(0.25, pass2.StopEpsilon, 6);
        AssertForceCoefficientsEqual(ForcePassOptions.Pass2Default, pass2);
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

    [Fact]
    public void CleanupForeignPartOverlaps_MovesPartialOverlapAndReducesSeverity()
    {
        var items = new List<ForceDirectedMarkItem>
        {
            CreateMark(id: 1, ownModelId: 10, x: 0.0, y: 0.0, localCorners: CreateRectangle(-5.0, -5.0, 5.0, 5.0))
        };
        var parts = new List<PartBbox>
        {
            new(20, 0.0, -5.0, 10.0, 5.0, CreateRectangle(0.0, -5.0, 10.0, 5.0))
        };
        var before = ForeignPartOverlapAnalyzer.Analyze(items, parts, threshold: 0.0);
        var placer = new ForceDirectedMarkPlacer();

        var result = placer.CleanupForeignPartOverlaps(items, parts, threshold: 0.0, maxStep: 5.0, maxStepsPerMark: 1);
        var after = ForeignPartOverlapAnalyzer.Analyze(items, parts, threshold: 0.0);

        Assert.Equal(1, before.PartialConflicts);
        Assert.True(result.MovedMarks > 0);
        Assert.True(items[0].Cx < 0.0);
        Assert.True(after.PartialSeverity < before.PartialSeverity);
    }

    [Fact]
    public void CleanupForeignPartOverlaps_AllowsGradualStepsBeforeFinalImprovement()
    {
        var items = new List<ForceDirectedMarkItem>
        {
            CreateMark(id: 1, ownModelId: 10, x: 0.0, y: 0.0, localCorners: CreateRectangle(-5.0, -5.0, 5.0, 5.0))
        };
        var parts = new List<PartBbox>
        {
            new(20, 0.0, -5.0, 12.0, 5.0, CreateRectangle(0.0, -5.0, 12.0, 5.0))
        };
        var before = ForeignPartOverlapAnalyzer.Analyze(items, parts, threshold: 0.0);
        var placer = new ForceDirectedMarkPlacer();

        var result = placer.CleanupForeignPartOverlaps(items, parts, threshold: 0.0, maxStep: 1.0, maxStepsPerMark: 6);
        var after = ForeignPartOverlapAnalyzer.Analyze(items, parts, threshold: 0.0);

        Assert.True(result.Iterations > 1);
        Assert.True(result.MovedMarks > 0);
        Assert.True(after.PartialSeverity < before.PartialSeverity);
    }

    [Fact]
    public void CleanupForeignPartOverlaps_ContinuesSameMarkWhileSeverityImproves()
    {
        var items = new List<ForceDirectedMarkItem>
        {
            CreateMark(id: 1, ownModelId: 10, x: 0.0, y: 0.0, localCorners: CreateRectangle(-5.0, -5.0, 5.0, 5.0)),
            CreateMark(id: 2, ownModelId: 30, x: 40.0, y: 0.0, localCorners: CreateRectangle(-5.0, -5.0, 5.0, 5.0))
        };
        var parts = new List<PartBbox>
        {
            new(20, 0.0, -5.0, 12.0, 5.0, CreateRectangle(0.0, -5.0, 12.0, 5.0)),
            new(40, 42.0, -5.0, 50.0, 5.0, CreateRectangle(42.0, -5.0, 50.0, 5.0))
        };
        var placer = new ForceDirectedMarkPlacer();

        var result = placer.CleanupForeignPartOverlaps(items, parts, threshold: 0.0, maxStep: 1.0, maxStepsPerMark: 4);

        Assert.True(result.Iterations > result.MovedMarks);
        Assert.Equal(-4.0, items[0].Cx, 6);
        Assert.Equal(37.0, items[1].Cx, 6);
    }

    [Fact]
    public void CleanupForeignPartOverlaps_CanUseOffAxisStepWhenAxisStepDoesNotImprove()
    {
        var mark = CreateMark(id: 1, ownModelId: 10, x: 0.0, y: 0.0, localCorners: CreateRectangle(-5.0, -5.0, 5.0, 5.0));
        mark.ConstrainToAxis = true;
        mark.AxisDx = 0.0;
        mark.AxisDy = 1.0;
        var items = new List<ForceDirectedMarkItem> { mark };
        var parts = new List<PartBbox>
        {
            new(20, 0.0, -5.0, 12.0, 5.0, CreateRectangle(0.0, -5.0, 12.0, 5.0))
        };
        var before = ForeignPartOverlapAnalyzer.Analyze(items, parts, threshold: 0.0);
        var placer = new ForceDirectedMarkPlacer();

        var result = placer.CleanupForeignPartOverlaps(items, parts, threshold: 0.0, maxStep: 1.0, maxStepsPerMark: 3);
        var after = ForeignPartOverlapAnalyzer.Analyze(items, parts, threshold: 0.0);

        Assert.True(result.MovedMarks > 0);
        Assert.True(mark.Cx < 0.0);
        Assert.True(after.PartialSeverity < before.PartialSeverity);
    }

    [Fact]
    public void CleanupForeignPartOverlaps_DoesNotMoveMarkInsideForeignPart()
    {
        var items = new List<ForceDirectedMarkItem>
        {
            CreateMark(id: 1, ownModelId: 10, x: 0.0, y: 0.0, localCorners: CreateRectangle(-5.0, -5.0, 5.0, 5.0))
        };
        var parts = new List<PartBbox>
        {
            new(20, -20.0, -20.0, 20.0, 20.0, CreateRectangle(-20.0, -20.0, 20.0, 20.0))
        };
        var placer = new ForceDirectedMarkPlacer();

        var result = placer.CleanupForeignPartOverlaps(items, parts, threshold: 0.0, maxStep: 5.0, maxStepsPerMark: 3);

        Assert.Equal(0, result.MovedMarks);
        Assert.Equal(0.0, items[0].Cx, 6);
        Assert.Equal(0.0, items[0].Cy, 6);
    }

    [Fact]
    public void CleanupForeignPartOverlaps_DoesNotCreateMarkOverlap()
    {
        var items = new List<ForceDirectedMarkItem>
        {
            CreateMark(id: 1, ownModelId: 10, x: 0.0, y: 0.0, localCorners: CreateRectangle(-5.0, -5.0, 5.0, 5.0)),
            CreateMark(id: 2, ownModelId: 30, x: -12.0, y: 0.0, localCorners: CreateRectangle(-5.0, -5.0, 5.0, 5.0))
        };
        var parts = new List<PartBbox>
        {
            new(20, 0.0, -5.0, 10.0, 5.0, CreateRectangle(0.0, -5.0, 10.0, 5.0))
        };
        var placer = new ForceDirectedMarkPlacer();

        placer.CleanupForeignPartOverlaps(items, parts, threshold: 0.0, maxStep: 5.0, maxStepsPerMark: 1);

        Assert.False(RectanglesOverlap(items[0], items[1]));
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

    private static ForceDirectedMarkItem CreateMark(
        int id,
        int ownModelId,
        double x,
        double y,
        List<double[]> localCorners) => new()
    {
        Id = id,
        OwnModelId = ownModelId,
        Cx = x,
        Cy = y,
        Width = 10.0,
        Height = 10.0,
        CanMove = true,
        LocalCorners = localCorners
    };

    private static void AssertForceCoefficientsEqual(ForcePassOptions expected, ForcePassOptions actual)
    {
        Assert.Equal(expected.KAttract, actual.KAttract, 6);
        Assert.Equal(expected.KFarAttract, actual.KFarAttract, 6);
        Assert.Equal(expected.MaxAttract, actual.MaxAttract, 6);
        Assert.Equal(expected.KReturnToAxisLine, actual.KReturnToAxisLine, 6);
        Assert.Equal(expected.KPerpRestoreAxis, actual.KPerpRestoreAxis, 6);
        Assert.Equal(expected.KRepelPart, actual.KRepelPart, 6);
        Assert.Equal(expected.KRepelMark, actual.KRepelMark, 6);
        Assert.Equal(expected.InitialDt, actual.InitialDt, 6);
        Assert.Equal(expected.DtDecay, actual.DtDecay, 6);
        Assert.Equal(expected.MaxIterations, actual.MaxIterations);
    }

    private static List<double[]> CreateRectangle(double minX, double minY, double maxX, double maxY) =>
        new()
        {
            new[] { minX, minY },
            new[] { maxX, minY },
            new[] { maxX, maxY },
            new[] { minX, maxY }
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
