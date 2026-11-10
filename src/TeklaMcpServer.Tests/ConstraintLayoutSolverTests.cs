using System;
using System.Collections.Generic;
using TeklaMcpServer.Api.Drawing;
using TeklaMcpServer.Api.Drawing.ViewLayout.Constraints;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class ConstraintLayoutSolverTests
{
    private const int Front = 1;
    private const int Top = 2;
    private const int SectionA = 3;
    private const int SectionB = 4;
    private const double Gap = 4;

    private static readonly AxisSeparation[] NoSeparations = Array.Empty<AxisSeparation>();
    private static readonly AxisAlignment[] NoAlignments = Array.Empty<AxisAlignment>();

    // ── One axis ─────────────────────────────────────────────────────────────

    [Fact]
    public void Axis_ChainIsPackedWithGapAndCentered()
    {
        var solution = AxisConstraintSolver.Solve(
            0, 100,
            new[] { new AxisItem(1, 30), new AxisItem(2, 20) },
            new[] { new AxisSeparation(1, 2, Gap) },
            NoAlignments);

        Assert.True(solution.IsFeasible);
        Assert.Equal(54, solution.RequiredSize, 9);
        // 46 of slack split evenly: block spans 23..77.
        Assert.Equal(38, solution.Centers[1], 9);
        Assert.Equal(67, solution.Centers[2], 9);
    }

    [Fact]
    public void Axis_Oversize_ReportsRequiredSizeInsteadOfThrowing()
    {
        var solution = AxisConstraintSolver.Solve(
            0, 40,
            new[] { new AxisItem(1, 30), new AxisItem(2, 20) },
            new[] { new AxisSeparation(1, 2, Gap) },
            NoAlignments);

        Assert.Equal(AxisFailure.Oversize, solution.Failure);
        Assert.Equal(54, solution.RequiredSize, 9);
        Assert.Equal(40, solution.AvailableSize, 9);
    }

    [Fact]
    public void Axis_AlignedItemsShareCenter_AndWidestSetsTheSpan()
    {
        var solution = AxisConstraintSolver.Solve(
            0, 300,
            new[] { new AxisItem(1, 100), new AxisItem(2, 200) },
            NoSeparations,
            new[] { new AxisAlignment(1, 2) });

        Assert.True(solution.IsFeasible);
        Assert.Equal(200, solution.RequiredSize, 9);
        Assert.Equal(150, solution.Centers[1], 9);
        Assert.Equal(150, solution.Centers[2], 9);
    }

    [Fact]
    public void Axis_Cycle_IsContradiction()
    {
        var solution = AxisConstraintSolver.Solve(
            0, 100,
            new[] { new AxisItem(1, 10), new AxisItem(2, 10) },
            new[] { new AxisSeparation(1, 2, Gap), new AxisSeparation(2, 1, Gap) },
            NoAlignments);

        Assert.Equal(AxisFailure.Contradiction, solution.Failure);
    }

    [Fact]
    public void Axis_SeparationInsideAlignedGroup_IsContradiction()
    {
        var solution = AxisConstraintSolver.Solve(
            0, 100,
            new[] { new AxisItem(1, 10), new AxisItem(2, 10) },
            new[] { new AxisSeparation(1, 2, Gap) },
            new[] { new AxisAlignment(1, 2) });

        Assert.Equal(AxisFailure.Contradiction, solution.Failure);
    }

    [Fact]
    public void Axis_Anchor_PullsPrecedingItemUpToIt()
    {
        // Item 2 is aligned with the anchor and twice as wide, so the anchor cannot
        // start at the lower bound. Item 3 must precede the anchor.
        var items = new[] { new AxisItem(1, 100), new AxisItem(2, 200), new AxisItem(3, 20) };
        var separations = new[] { new AxisSeparation(3, 1, Gap) };
        var alignments = new[] { new AxisAlignment(1, 2) };

        var loose = AxisConstraintSolver.Solve(0, 300, items, separations, alignments);
        var gathered = AxisConstraintSolver.Solve(0, 300, items, separations, alignments, anchorId: 1);

        // Without an anchor item 3 stays at the lower bound, 30 away from the anchor.
        Assert.Equal(30, (loose.Centers[1] - 50) - (loose.Centers[3] + 10), 9);
        Assert.Equal(Gap, (gathered.Centers[1] - 50) - (gathered.Centers[3] + 10), 9);
        Assert.Equal(loose.RequiredSize, gathered.RequiredSize, 9);
    }

    [Fact]
    public void Axis_UnknownItemInConstraint_Throws()
    {
        Assert.Throws<ArgumentException>(() => AxisConstraintSolver.Solve(
            0, 100,
            new[] { new AxisItem(1, 10) },
            new[] { new AxisSeparation(1, 99, Gap) },
            NoAlignments));
    }

    // ── Sheet ────────────────────────────────────────────────────────────────

    [Fact]
    public void Sheet_FrontTopAndTwoSectionsOnTheRight_AreAlignedAndSeparated()
    {
        var solution = ConstraintLayoutSolver.Solve(FrontTopTwoSections(new ReservedRect(10, 10, 410, 287)));

        Assert.True(solution.IsFeasible);
        Assert.Equal(308, solution.RequiredWidth, 9);
        Assert.Equal(164, solution.RequiredHeight, 9);

        var front = solution.RectsById[Front];
        var top = solution.RectsById[Top];
        var sectionA = solution.RectsById[SectionA];
        var sectionB = solution.RectsById[SectionB];

        Assert.Equal(CenterX(front), CenterX(top), 9);
        Assert.Equal(CenterY(front), CenterY(sectionA), 9);
        Assert.Equal(CenterY(front), CenterY(sectionB), 9);
        Assert.Equal(Gap, top.MinY - front.MaxY, 9);
        Assert.Equal(Gap, sectionA.MinX - front.MaxX, 9);
        Assert.Equal(Gap, sectionB.MinX - sectionA.MaxX, 9);

        // Slack is split evenly between opposite sheet edges.
        Assert.Equal(front.MinX - 10, 410 - sectionB.MaxX, 9);
        Assert.Equal(front.MinY - 10, 287 - top.MaxY, 9);
    }

    [Fact]
    public void Sheet_TooNarrow_ReportsRequiredWidth()
    {
        var solution = ConstraintLayoutSolver.Solve(FrontTopTwoSections(new ReservedRect(10, 10, 210, 287)));

        Assert.False(solution.IsFeasible);
        Assert.Equal(AxisFailure.Oversize, solution.HorizontalFailure);
        Assert.Equal(AxisFailure.None, solution.VerticalFailure);
        Assert.Equal(308, solution.RequiredWidth, 9);
    }

    [Fact]
    public void Sheet_PairWithNoSeparatingRelation_IsReported()
    {
        var problem = new ConstraintLayoutProblem(
            new ReservedRect(0, 0, 400, 300),
            Gap,
            new[] { new ConstraintLayoutView(Front, 200, 100), new ConstraintLayoutView(Top, 200, 60) },
            Array.Empty<LayoutRelation>());

        var solution = ConstraintLayoutSolver.Solve(problem);

        Assert.False(solution.IsFeasible);
        Assert.Equal(new[] { (Front, Top) }, solution.UnseparatedPairs);
    }

    private static ConstraintLayoutProblem FrontTopTwoSections(ReservedRect bounds)
        => new(
            bounds,
            Gap,
            new[]
            {
                new ConstraintLayoutView(Front, 200, 100),
                new ConstraintLayoutView(Top, 200, 60),
                new ConstraintLayoutView(SectionA, 50, 100),
                new ConstraintLayoutView(SectionB, 50, 100)
            },
            new List<LayoutRelation>
            {
                new(LayoutRelationKind.Below, Front, Top),
                new(LayoutRelationKind.AlignCenterX, Front, Top),
                new(LayoutRelationKind.LeftOf, Front, SectionA),
                new(LayoutRelationKind.LeftOf, SectionA, SectionB),
                new(LayoutRelationKind.AlignCenterY, Front, SectionA),
                new(LayoutRelationKind.AlignCenterY, Front, SectionB)
            },
            anchorViewId: Front);

    private static double CenterX(ReservedRect rect) => (rect.MinX + rect.MaxX) / 2.0;

    private static double CenterY(ReservedRect rect) => (rect.MinY + rect.MaxY) / 2.0;
}
