using System.Collections.Generic;
using System.Linq;
using TeklaMcpServer.Api.Algorithms.Marks;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class ForceDirectedMarkPlacerTests
{
    [Fact]
    public void CreateEquilibriumForViewScale_ScalesDistancePolicyOnly()
    {
        var options = ForcePassOptions.CreateEquilibriumForViewScale(25.0);

        Assert.Equal(100.0, options.IdealDist, 6);
        Assert.Equal(50.0, options.MarkGapMm, 6);
        Assert.Equal(200.0, options.PartRepelRadius, 6);
        Assert.Equal(18.75, options.PartRepelSoftening, 6);
        Assert.Equal(6.25, options.StopEpsilon, 6);
        AssertForceCoefficientsEqual(ForcePassOptions.EquilibriumDefault, options);
    }

    [Fact]
    public void CreateMarkSeparationForViewScale_ScalesDistancePolicyOnly()
    {
        var options = ForcePassOptions.CreateMarkSeparationForViewScale(25.0);

        Assert.Equal(100.0, options.IdealDist, 6);
        Assert.Equal(50.0, options.MarkGapMm, 6);
        Assert.Equal(200.0, options.PartRepelRadius, 6);
        Assert.Equal(18.75, options.PartRepelSoftening, 6);
        Assert.Equal(6.25, options.StopEpsilon, 6);
        AssertForceCoefficientsEqual(ForcePassOptions.MarkSeparationDefault, options);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-25.0)]
    public void CreateForceOptionsForNonPositiveScale_UsesIdentityScale(double viewScale)
    {
        var equilibrium = ForcePassOptions.CreateEquilibriumForViewScale(viewScale);
        var markSeparation = ForcePassOptions.CreateMarkSeparationForViewScale(viewScale);

        Assert.Equal(4.0, equilibrium.IdealDist, 6);
        Assert.Equal(2.0, equilibrium.MarkGapMm, 6);
        Assert.Equal(8.0, equilibrium.PartRepelRadius, 6);
        Assert.Equal(0.75, equilibrium.PartRepelSoftening, 6);
        Assert.Equal(0.25, equilibrium.StopEpsilon, 6);
        AssertForceCoefficientsEqual(ForcePassOptions.EquilibriumDefault, equilibrium);

        Assert.Equal(4.0, markSeparation.IdealDist, 6);
        Assert.Equal(2.0, markSeparation.MarkGapMm, 6);
        Assert.Equal(8.0, markSeparation.PartRepelRadius, 6);
        Assert.Equal(0.75, markSeparation.PartRepelSoftening, 6);
        Assert.Equal(0.25, markSeparation.StopEpsilon, 6);
        AssertForceCoefficientsEqual(ForcePassOptions.MarkSeparationDefault, markSeparation);
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
            ForcePassOptions.MarkSeparationDefault,
            includeMarkRepulsion: true,
            movableIds: movableIds,
            getRemainingOverlapCount: () => CountOverlappingTrackedIds(items, movableIds),
            overlapCheckInterval: 1);

        Assert.Equal(ForceRelaxStopReason.OverlapsCleared, result.StopReason);
        Assert.True(result.Iterations < ForcePassOptions.MarkSeparationDefault.MaxIterations);
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
            ForcePassOptions.MarkSeparationDefault,
            includeMarkRepulsion: true,
            movableIds: new HashSet<int> { 1 },
            getRemainingOverlapCount: () => 1,
            overlapCheckInterval: 1);

        Assert.NotEqual(ForceRelaxStopReason.OverlapsCleared, result.StopReason);
    }

    [Fact]
    public void Relax_PrefersAxisStepForReturnToAxisMarksWhenItSeparatesOverlap()
    {
        var mark = CreateMark(id: 1, x: 0.0, y: 10.0);
        mark.ReturnToAxisLine = true;
        mark.AxisDx = 1.0;
        mark.AxisDy = 0.0;
        mark.AxisOriginX = 0.0;
        mark.AxisOriginY = 0.0;
        var other = CreateMark(id: 2, x: 8.0, y: 10.0);
        var items = new List<ForceDirectedMarkItem> { mark, other };
        var options = new ForcePassOptions(
            kAttract: 0.0,
            idealDist: 0.0,
            kFarAttract: 0.0,
            maxAttract: 0.0,
            kReturnToAxisLine: 0.12,
            kPerpRestoreAxis: 0.0,
            kRepelPart: 0.0,
            partRepelRadius: 0.0,
            partRepelSoftening: 1.0,
            kRepelMark: 1.0,
            markGapMm: 2.0,
            initialDt: 1.0,
            dtDecay: 1.0,
            stopEpsilon: 0.0,
            maxIterations: 1);
        var placer = new ForceDirectedMarkPlacer();

        placer.Relax(
            items,
            [],
            options,
            includeMarkRepulsion: true,
            movableIds: new HashSet<int> { mark.Id },
            preferAxisStepForReturnToAxisMarks: true);

        Assert.True(mark.Cx < 0.0);
        Assert.Equal(10.0, mark.Cy, 6);
        Assert.False(RectanglesOverlap(mark, other));
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

    [Fact]
    public void AxisMarkSeparationCleanup_SeparatesAxisForceMarks()
    {
        var first = CreateAxisMark(id: 1, x: 0.0, y: 0.0, axisDx: 0.0, axisDy: 1.0);
        var second = CreateAxisMark(id: 2, x: 0.0, y: 30.0, axisDx: 0.0, axisDy: -1.0);
        var items = new List<ForceDirectedMarkItem> { first, second };

        var result = AxisMarkSeparationCleanup.Resolve(items, gap: 2.0);

        Assert.Equal(1, result.BeforeOverlaps);
        Assert.Equal(0, result.AfterOverlaps);
        Assert.Equal(2, result.MovedMarks);
        Assert.True(result.Iterations >= 1);
        Assert.Equal(0.0, first.Cx, 6);
        Assert.Equal(0.0, second.Cx, 6);
        Assert.True(first.Cy < 0.0);
        Assert.True(second.Cy > 30.0);
    }

    [Fact]
    public void AxisMarkSeparationCleanup_RepeatsAcrossOverlapChain()
    {
        var items = new List<ForceDirectedMarkItem>
        {
            CreateAxisMark(id: 1, x: 0.0, y: 0.0, axisDx: 0.0, axisDy: 1.0),
            CreateAxisMark(id: 2, x: 0.0, y: 30.0, axisDx: 0.0, axisDy: 1.0),
            CreateAxisMark(id: 3, x: 0.0, y: 60.0, axisDx: 0.0, axisDy: 1.0)
        };

        var result = AxisMarkSeparationCleanup.Resolve(items, gap: 2.0, maxIterations: 10);

        Assert.Equal(2, result.BeforeOverlaps);
        Assert.Equal(0, result.AfterOverlaps);
        Assert.Equal(3, result.MovedMarks);
        Assert.True(result.Iterations > 1);
    }

    [Fact]
    public void AxisMarkSeparationCleanup_IgnoresNonAxisForceMarks()
    {
        var first = CreateAxisMark(id: 1, x: 0.0, y: 0.0, axisDx: 0.0, axisDy: 1.0);
        var second = CreateAxisMark(id: 2, x: 0.0, y: 30.0, axisDx: 0.0, axisDy: -1.0);
        first.ConstrainToAxis = false;
        second.ConstrainToAxis = false;
        var items = new List<ForceDirectedMarkItem> { first, second };

        var result = AxisMarkSeparationCleanup.Resolve(items, gap: 2.0);

        Assert.Equal(1, result.BeforeOverlaps);
        Assert.Equal(1, result.AfterOverlaps);
        Assert.Equal(0, result.MovedMarks);
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

    private static ForceDirectedMarkItem CreateAxisMark(
        int id,
        double x,
        double y,
        double axisDx,
        double axisDy) => new()
    {
        Id = id,
        Cx = x,
        Cy = y,
        Width = 10.0,
        Height = 40.0,
        CanMove = true,
        ConstrainToAxis = true,
        AxisDx = axisDx,
        AxisDy = axisDy,
        LocalCorners = CreateRectangle(-5.0, -20.0, 5.0, 20.0)
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
