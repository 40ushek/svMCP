using System.Collections.Generic;
using System.Linq;
using TeklaMcpServer.Api.Drawing;
using TeklaMcpServer.Api.Drawing.ViewLayout;
using TeklaMcpServer.Api.Drawing.ViewLayout.Constraints;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class ConstraintLayoutPlannerTests
{
    private const int Front = 1;
    private const int Top = 2;
    private const int SectionA = 3;
    private const int SectionB = 4;
    private const double Gap = 4;

    [Fact]
    public void EverythingFits_FirstCandidateKeepsPreferredSides_InSectionLineOrder()
    {
        var plan = ConstraintLayoutPlanner.Plan(Input(
            new ReservedRect(0, 0, 400, 300),
            Section(SectionA, orderKey: 20),
            Section(SectionB, orderKey: 10)));

        var best = plan.Candidates[0];
        Assert.Equal(0, best.Deviations);
        Assert.Equal("inline-Right", best.Slots[SectionA].ToString());
        Assert.Equal("inline-Right", best.Slots[SectionB].ToString());
        // B's section line comes first on the base view, so B is the one next to it.
        Assert.Equal(Gap, best.RectsById[SectionB].MinX - best.RectsById[Front].MaxX, 9);
        Assert.Equal(Gap, best.RectsById[SectionA].MinX - best.RectsById[SectionB].MaxX, 9);
        AssertValid(best, new ReservedRect(0, 0, 400, 300));
    }

    [Fact]
    public void PreferredSideTooNarrow_FirstCandidateMovesOneSection()
    {
        // 200 + 4 + 50 fits in 270, a second section in line does not.
        var bounds = new ReservedRect(0, 0, 270, 250);
        var plan = ConstraintLayoutPlanner.Plan(Input(
            bounds,
            Section(SectionA, orderKey: 10),
            Section(SectionB, orderKey: 20)));

        Assert.Equal(308, plan.PreferredRequiredWidth, 9);
        var best = plan.Candidates[0];
        Assert.Equal(1, best.Deviations);
        Assert.Contains(best.Slots.Values, static slot => slot.ToString() == "inline-Right");
        Assert.Contains(best.Slots.Values, static slot => slot.ToString() == "stack-Bottom");
        AssertValid(best, bounds);
    }

    [Fact]
    public void KeepProjectionPolicy_RefusesToMoveASection_WhereTheDefaultPolicyMovesIt()
    {
        var input = Input(
            new ReservedRect(0, 0, 270, 250),
            Section(SectionA, orderKey: 10),
            Section(SectionB, orderKey: 20));
        Assert.NotEmpty(ConstraintLayoutPlanner.Plan(input).Candidates);

        input.Policy = ConstraintLayoutPolicy.KeepProjection;
        var strict = ConstraintLayoutPlanner.Plan(input);

        Assert.Empty(strict.Candidates);
        Assert.Equal(1, strict.EvaluatedTopologies);
        Assert.Equal(308, strict.PreferredRequiredWidth, 9);
    }

    [Fact]
    public void Candidates_ComeCheapestFirst_ByThePolicyPrices()
    {
        var bounds = new ReservedRect(0, 0, 400, 300);

        var byDefault = ConstraintLayoutPlanner.Plan(Input(bounds, Section(SectionA, orderKey: 10)));
        // Default: a wrong side (0.5) is given up before a projection link (1.5).
        Assert.Equal(new[] { 0.0, 0.5, 1.5, 1.5, 1.5 }, byDefault.Candidates.Select(static c => c.Cost));
        Assert.Equal("inline-Right", byDefault.Candidates[0].Slots[SectionA].ToString());
        Assert.Equal("inline-Left", byDefault.Candidates[1].Slots[SectionA].ToString());

        var input = Input(bounds, Section(SectionA, orderKey: 10));
        input.Policy = new ConstraintLayoutPolicy { SideDeviationCost = 2, LinkBreakCost = 1 };
        var linkCheaper = ConstraintLayoutPlanner.Plan(input);
        Assert.Equal(new[] { 0.0, 1.0, 1.0, 1.0, 2.0 }, linkCheaper.Candidates.Select(static c => c.Cost));
        Assert.Equal("stack-Left", linkCheaper.Candidates[1].Slots[SectionA].ToString());
        Assert.Equal("inline-Left", linkCheaper.Candidates[4].Slots[SectionA].ToString());
    }

    [Fact]
    public void PolicyCannotLoosenAStrongProjectionLink()
    {
        var input = Input(
            new ReservedRect(0, 0, 230, 250),
            Section(SectionA, orderKey: 10, strength: ProjectionStrength.Strong));
        input.Policy = new ConstraintLayoutPolicy { SideDeviationCost = 0, LinkBreakCost = 0 };

        Assert.Empty(ConstraintLayoutPlanner.Plan(input).Candidates);
    }

    [Fact]
    public void StrongViewThatDoesNotFit_GivesNoCandidate_AndReportsRequiredSize()
    {
        var plan = ConstraintLayoutPlanner.Plan(Input(
            new ReservedRect(0, 0, 230, 250),
            Section(SectionA, orderKey: 10, strength: ProjectionStrength.Strong)));

        Assert.Empty(plan.Candidates);
        Assert.Equal(1, plan.EvaluatedTopologies);
        Assert.Equal(254, plan.PreferredRequiredWidth, 9);
    }

    [Fact]
    public void ViewsWithoutProjectionLink_AreStacked_InSectionLineOrder()
    {
        var bounds = new ReservedRect(0, 0, 400, 300);
        var plan = ConstraintLayoutPlanner.Plan(Input(
            bounds,
            Section(SectionA, orderKey: 20, strength: ProjectionStrength.Off),
            Section(SectionB, orderKey: 10, strength: ProjectionStrength.Off)));

        var best = plan.Candidates[0];
        Assert.Equal("stack-Left", best.Slots[SectionA].ToString());
        Assert.Equal("stack-Left", best.Slots[SectionB].ToString());

        var a = best.RectsById[SectionA];
        var b = best.RectsById[SectionB];
        Assert.Equal(Gap, b.MinY - a.MaxY, 9);
        Assert.Equal(a.MinX, b.MinX, 9);
        Assert.True(a.MaxX <= best.RectsById[Front].MinX - Gap + 1e-9);
        AssertValid(best, bounds);
    }

    [Fact]
    public void WideTopViewAndTallSideSection_AreSeparatedAtTheCorner()
    {
        var bounds = new ReservedRect(0, 0, 500, 400);
        var input = Input(
            bounds,
            new ConstraintLayoutViewInput
            {
                Id = Top, Width = 300, Height = 60, IsProjection = true,
                PreferredSide = SectionPlacementSide.Top, ProjectionStrength = ProjectionStrength.Strong
            },
            new ConstraintLayoutViewInput
            {
                Id = SectionA, Width = 50, Height = 200,
                PreferredSide = SectionPlacementSide.Right, ProjectionStrength = ProjectionStrength.Strong
            });
        input.BaseView = new ConstraintLayoutView(Front, 100, 100);

        var best = Assert.Single(ConstraintLayoutPlanner.Plan(input).Candidates);

        var front = best.RectsById[Front];
        var top = best.RectsById[Top];
        var section = best.RectsById[SectionA];
        // Both projection links survive; the top view is lifted clear of the section.
        Assert.Equal((front.MinX + front.MaxX) / 2, (top.MinX + top.MaxX) / 2, 9);
        Assert.Equal((front.MinY + front.MaxY) / 2, (section.MinY + section.MaxY) / 2, 9);
        Assert.Equal(Gap, top.MinY - section.MaxY, 9);
        AssertValid(best, bounds);
    }

    [Fact]
    public void AlignmentOffset_PlacesInLineViewOffTheBaseViewCenterLine()
    {
        var bounds = new ReservedRect(0, 0, 400, 300);
        var section = Section(SectionA, orderKey: 10, strength: ProjectionStrength.Strong);
        section.AlignmentOffsetY = 12;
        section.AlignmentOffsetX = 99; // not used for a view beside the base view

        var best = Assert.Single(ConstraintLayoutPlanner.Plan(Input(bounds, section)).Candidates);

        var front = best.RectsById[Front];
        var placed = best.RectsById[SectionA];
        Assert.Equal(12, ((placed.MinY + placed.MaxY) - (front.MinY + front.MaxY)) / 2, 9);
        Assert.Equal(Gap, placed.MinX - front.MaxX, 9);
        AssertValid(best, bounds);
    }

    [Fact]
    public void ReservedAreaUnderCenteredLayout_LayoutMovesAside()
    {
        var bounds = new ReservedRect(0, 0, 400, 300);
        var titleBlock = new ReservedRect(300, 0, 400, 80);
        var input = Input(bounds);
        input.ReservedAreas = new[] { titleBlock };

        var best = ConstraintLayoutPlanner.Plan(input).Candidates[0];

        var front = best.RectsById[Front];
        Assert.True(front.MaxX <= titleBlock.MinX - Gap || front.MinY >= titleBlock.MaxY + Gap);
        AssertValid(best, bounds);
    }

    [Fact]
    public void ReservedAreaWithNoClearPosition_GivesNoCandidate()
    {
        var input = Input(new ReservedRect(0, 0, 220, 120));
        input.ReservedAreas = new[] { new ReservedRect(100, 50, 120, 70) };

        Assert.Empty(ConstraintLayoutPlanner.Plan(input).Candidates);
    }

    [Fact]
    public void TopologyLimit_StopsSearchAndSaysSo()
    {
        var input = Input(
            new ReservedRect(0, 0, 100, 100),
            Section(SectionA, orderKey: 10),
            Section(SectionB, orderKey: 20));
        input.MaxTopologies = 3;

        var plan = ConstraintLayoutPlanner.Plan(input);

        Assert.Equal(3, plan.EvaluatedTopologies);
        Assert.True(plan.Truncated);
    }

    private static ConstraintLayoutPlannerInput Input(ReservedRect bounds, params ConstraintLayoutViewInput[] views)
        => new()
        {
            Bounds = bounds,
            Gap = Gap,
            BaseView = new ConstraintLayoutView(Front, 200, 100),
            Views = views
        };

    private static ConstraintLayoutViewInput Section(
        int id,
        double orderKey,
        ProjectionStrength strength = ProjectionStrength.Relaxed)
        => new()
        {
            Id = id,
            Width = 50,
            Height = 100,
            PreferredSide = SectionPlacementSide.Right,
            ProjectionStrength = strength,
            OrderKey = orderKey
        };

    private static void AssertValid(ConstraintLayoutPlanCandidate candidate, ReservedRect bounds)
    {
        var rects = candidate.RectsById.Values.ToList();
        Assert.All(rects, rect => Assert.True(
            ViewPlacementValidator.IsWithinArea(
                rect, bounds.MinX - 1e-9, bounds.MaxX + 1e-9, bounds.MinY - 1e-9, bounds.MaxY + 1e-9)));

        for (var i = 0; i < rects.Count; i++)
        for (var j = i + 1; j < rects.Count; j++)
            Assert.False(ViewPlacementValidator.Intersects(rects[i], rects[j]));
    }
}
