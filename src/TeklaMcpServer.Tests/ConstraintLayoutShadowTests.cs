using System.Collections.Generic;
using System.Linq;
using TeklaMcpServer.Api.Drawing;
using TeklaMcpServer.Api.Drawing.ViewLayout;
using TeklaMcpServer.Api.Drawing.ViewLayout.Constraints;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class ConstraintLayoutShadowTests
{
    private const int Front = 1;
    private const int Top = 2;
    private const int SectionA = 3;
    private const int SectionB = 4;
    private const int Detail = 5;
    private const double Gap = 4;

    private static ConstraintLayoutShadowResult? Evaluate(DrawingLayoutCandidate reference, int baseViewId = Front)
        => ConstraintLayoutShadow.Evaluate(Case(reference, baseViewId));

    private static ConstraintLayoutCase Case(DrawingLayoutCandidate reference, int baseViewId = Front)
        => new()
        {
            Reference = reference,
            BaseViewId = baseViewId,
            Gap = Gap,
            SectionLineAnchors =
            [
                new ConstraintLayoutCaseAnchor { ViewId = SectionA, X = 150, Y = 100 },
                new ConstraintLayoutCaseAnchor { ViewId = SectionB, X = 80, Y = 100 }
            ]
        };

    [Fact]
    public void LaysOutSkeletonViews_AndScoresBothLayoutsOnTheSameViews()
    {
        var result = Evaluate(Reference());

        Assert.NotNull(result);
        Assert.Equal(5, result.IncludedViews);
        Assert.Equal(0, result.SkippedViews);
        Assert.Equal(5, result.Reference.Candidate.Views.Count);

        Assert.NotNull(result.Best);
        Assert.True(result.Best.IsFeasible);
        Assert.Equal(ConstraintLayoutShadow.CandidateName, result.Best.Candidate.Name);
        Assert.Equal(5, result.Best.Candidate.Views.Count);
        Assert.Equal(0, result.BestPlan!.Deviations);
    }

    [Fact]
    public void SectionsFollowTheirSectionLines_AndKeepPreferredSide()
    {
        var best = Evaluate(Reference())!.Best!.Candidate;

        var front = best.Views.Single(static view => view.Id == Front).LayoutRect!;
        var a = best.Views.Single(static view => view.Id == SectionA);
        var b = best.Views.Single(static view => view.Id == SectionB);

        Assert.Equal("Right", a.ActualPlacementSide);
        Assert.False(a.PlacementFallbackUsed);
        // B's line is further left on the base view, so B stands next to it.
        Assert.Equal(Gap, b.LayoutRect!.MinX - front.MaxX, 9);
        Assert.Equal(Gap, a.LayoutRect!.MinX - b.LayoutRect.MaxX, 9);
    }

    [Fact]
    public void OriginKeepsItsOffsetFromTheFrame()
    {
        var reference = Reference();
        var before = reference.Views.Single(static view => view.Id == SectionA);
        var after = Evaluate(reference)!
            .Best!.Candidate.Views.Single(static view => view.Id == SectionA);

        Assert.Equal(before.OriginX - before.LayoutRect!.MinX, after.OriginX - after.LayoutRect!.MinX, 9);
        Assert.Equal(before.OriginY - before.LayoutRect.MinY, after.OriginY - after.LayoutRect.MinY, 9);
    }

    [Fact]
    public void SheetTooSmall_ReportsNoFit_AndStillScoresTheReference()
    {
        var reference = Reference();
        reference.Sheet = new DrawingSheetContext { Width = 150, Height = 120 };

        var result = Evaluate(reference);

        Assert.NotNull(result);
        Assert.Null(result.Best);
        Assert.Empty(result.Plan.Candidates);
        Assert.True(result.Plan.PreferredRequiredWidth > 130);
        Assert.Equal(5, result.Reference.Candidate.Views.Count);
    }

    [Fact]
    public void SheetTooNarrowForBothSectionsInLine_DefaultPolicyMovesOne_StrictPolicyGivesNoLayout()
    {
        // Usable width 300; base view and two sections in line need 308.
        var reference = Reference();
        reference.Sheet = new DrawingSheetContext { Width = 320, Height = 297 };

        var byDefault = ConstraintLayoutShadow.Evaluate(Case(reference))!;
        var strict = ConstraintLayoutShadow.Evaluate(Case(reference), ConstraintLayoutPolicy.KeepProjection)!;

        Assert.NotNull(byDefault.Best);
        Assert.True(byDefault.BestPlan!.Cost > 0);
        Assert.Null(strict.Best);
    }

    [Fact]
    public void Variant_ReturnsTheNextLayouts_CheapestFirst_AndNothingPastTheLast()
    {
        var layoutCase = Case(Reference());

        var chosen = ConstraintLayoutShadow.Evaluate(layoutCase)!;
        var next = ConstraintLayoutShadow.Evaluate(layoutCase, variant: 1)!;
        var pastTheEnd = ConstraintLayoutShadow.Evaluate(layoutCase, variant: 1000)!;

        Assert.Equal(0, chosen.BestPlan!.Cost);
        Assert.True(next.BestPlan!.Cost > 0);
        Assert.Same(next.Plan.Candidates[1], next.BestPlan);
        Assert.NotEqual(
            chosen.BestPlan.Slots.Select(static slot => slot.Value.ToString()),
            next.BestPlan.Slots.Select(static slot => slot.Value.ToString()));
        Assert.True(next.Best!.IsFeasible);
        Assert.Null(pastTheEnd.Best);
        Assert.NotEmpty(pastTheEnd.Plan.Candidates);
    }

    [Fact]
    public void BaseViewNotInReference_ReturnsNull()
        => Assert.Null(Evaluate(Reference(), baseViewId: 99));

    [Fact]
    public void ModelAnchors_SetTheAlignmentOffset_AndReportHowFarTheCurrentLayoutIsFromIt()
    {
        var layoutCase = Case(Reference());
        layoutCase.ModelAnchors =
        [
            new ConstraintLayoutCaseModelAnchor { ViewId = Front, X = 1, Y = 5 },
            new ConstraintLayoutCaseModelAnchor { ViewId = SectionA, X = 40, Y = 8 },
            new ConstraintLayoutCaseModelAnchor { ViewId = Top, X = 1, Y = 70 }
        ];

        var result = ConstraintLayoutShadow.Evaluate(layoutCase)!;

        var front = result.Best!.Candidate.Views.Single(static view => view.Id == Front).LayoutRect!;
        var a = result.Best.Candidate.Views.Single(static view => view.Id == SectionA).LayoutRect!;
        var b = result.Best.Candidate.Views.Single(static view => view.Id == SectionB).LayoutRect!;
        var top = result.Best.Candidate.Views.Single(static view => view.Id == Top).LayoutRect!;
        // A is beside the base view: only the Y difference of the anchors applies.
        Assert.Equal(3, ((a.MinY + a.MaxY) - (front.MinY + front.MaxY)) / 2, 9);
        // B has no anchor and falls back to frame centers.
        Assert.Equal(0, ((b.MinY + b.MaxY) - (front.MinY + front.MaxY)) / 2, 9);
        // Top is above the base view: only the X difference applies, and it is zero.
        Assert.Equal(0, ((top.MinX + top.MaxX) - (front.MinX + front.MaxX)) / 2, 9);

        // In the current layout A and Top are centered on the base view; the anchors
        // ask for 3 and 0, so the worst disagreement is 3. B is not checked.
        Assert.Equal(2, result.AlignmentCheckedViews);
        Assert.Equal(1, result.AlignmentMatchedViews);
        Assert.Equal(3, result.AlignmentResidualMax, 9);
        // The case does not say the current path aligned its layout, so the residual proves nothing.
        Assert.False(result.AlignmentCheckValid);

        layoutCase.ReferenceProjectionMode = "assembly";
        Assert.True(ConstraintLayoutShadow.Evaluate(layoutCase)!.AlignmentCheckValid);
        layoutCase.ReferenceProjectionMode = "disabled-mixed-scales";
        Assert.False(ConstraintLayoutShadow.Evaluate(layoutCase)!.AlignmentCheckValid);
    }

    [Fact]
    public void ProjectionSideFromTheCase_OverridesTheGuessFromViewType()
    {
        var reference = Reference();
        reference.Views.Add(View(
            6, "EndView", "BaseProjected", "MainProjected", new ReservedRect(350, 20, 380, 120), "", "Strong"));
        reference.Views.Add(View(
            7, "EndView", "BaseProjected", "MainProjected", new ReservedRect(385, 20, 410, 120), "", "Strong"));
        var layoutCase = Case(reference);
        layoutCase.ViewSides = [new ConstraintLayoutCaseSide { ViewId = 6, Side = "Left" }];

        var slots = ConstraintLayoutShadow.Evaluate(layoutCase)!.BestPlan!.Slots;

        Assert.Equal("inline-Left", slots[6].ToString());
        // No side supplied: an end view is assumed to belong on the right.
        Assert.Equal("inline-Right", slots[7].ToString());
        // A side the current path recorded on the view itself is kept.
        Assert.Equal("inline-Right", slots[SectionA].ToString());
    }

    [Fact]
    public void SectionDrawnLargerThanTheBaseView_IsAlignedByFrameCenter_NotByItsModelAnchor()
    {
        var reference = Reference();
        reference.Views.Single(static view => view.Id == SectionA).Scale = 5; // base view is 1:10
        var layoutCase = Case(reference);
        layoutCase.ModelAnchors =
        [
            new ConstraintLayoutCaseModelAnchor { ViewId = Front, X = 1, Y = 5 },
            new ConstraintLayoutCaseModelAnchor { ViewId = SectionA, X = 40, Y = 500 },
            new ConstraintLayoutCaseModelAnchor { ViewId = SectionB, X = 40, Y = 8 }
        ];

        var result = ConstraintLayoutShadow.Evaluate(layoutCase)!;

        var front = result.Best!.Candidate.Views.Single(static view => view.Id == Front).LayoutRect!;
        var a = result.Best.Candidate.Views.Single(static view => view.Id == SectionA);
        var b = result.Best.Candidate.Views.Single(static view => view.Id == SectionB).LayoutRect!;
        Assert.Equal(0, ((a.LayoutRect!.MinY + a.LayoutRect.MaxY) - (front.MinY + front.MaxY)) / 2, 9);
        Assert.Equal(3, ((b.MinY + b.MaxY) - (front.MinY + front.MaxY)) / 2, 9);
        // The section keeps its own scale in the layout that would be applied.
        Assert.Equal(5, a.Scale);
        // Only the view at the base view's scale is compared with the current layout.
        Assert.Equal(1, result.AlignmentCheckedViews);
    }

    [Fact]
    public void EndViewWithASectionMark_IsASection_AndMayLeaveItsSide_WhileOneWithoutStaysPut()
    {
        // Usable width 300. In line on the right the base view (200) and two end
        // views (2 x 30) need 268; with both sections as well, 376.
        var reference = Reference();
        reference.Sheet = new DrawingSheetContext { Width = 320, Height = 420 };
        reference.Views.Add(View(
            6, "EndView", "BaseProjected", "MainProjected", new ReservedRect(350, 20, 380, 120), "", "Strong"));
        reference.Views.Add(View(
            7, "EndView", "BaseProjected", "MainProjected", new ReservedRect(385, 20, 415, 120), "", "Strong"));
        var layoutCase = Case(reference);
        layoutCase.SectionLineAnchors.Add(new ConstraintLayoutCaseAnchor { ViewId = 6, X = 200, Y = 100 });

        var slots = ConstraintLayoutShadow.Evaluate(layoutCase)!.BestPlan!.Slots;

        // View 7 has no section mark: a projection, it keeps its side whatever happens.
        Assert.Equal("inline-Right", slots[7].ToString());
        // View 6 has one: a section like A and B, so something had to give among the three.
        Assert.Contains(new[] { SectionA, SectionB, 6 }, id => slots[id].ToString() != "inline-Right");
    }

    [Fact]
    public void SideFromTheCase_WinsOverTheSideTheCurrentPathRecorded()
    {
        var layoutCase = Case(Reference());
        layoutCase.ViewSides = [new ConstraintLayoutCaseSide { ViewId = SectionA, Side = "Left" }];

        var slots = ConstraintLayoutShadow.Evaluate(layoutCase)!.BestPlan!.Slots;

        Assert.Equal("inline-Left", slots[SectionA].ToString());
        Assert.Equal("inline-Right", slots[SectionB].ToString());
    }

    [Fact]
    public void CutOffset_ComesFromTheSectionLineOnTheBaseView_AtTheBaseViewsScale()
    {
        var reference = Reference();
        var front = reference.Views.Single(static view => view.Id == Front);
        var layoutCase = Case(reference);
        // Both sections look down. A's line is 300 model mm above the base view's
        // origin; B's mark is on a view the planner does not lay out, so B has no place at its cut.
        layoutCase.ViewSides =
        [
            new ConstraintLayoutCaseSide { ViewId = SectionA, Side = "Bottom" },
            new ConstraintLayoutCaseSide { ViewId = SectionB, Side = "Bottom" }
        ];
        layoutCase.SectionLineAnchors =
        [
            new ConstraintLayoutCaseAnchor { ViewId = SectionA, OwnerViewId = Front, OwnerLocalX = 0, OwnerLocalY = 300 },
            new ConstraintLayoutCaseAnchor { ViewId = SectionB, OwnerViewId = Detail, OwnerLocalX = 0, OwnerLocalY = 300 }
        ];

        var plan = ConstraintLayoutShadow.Evaluate(layoutCase)!.Plan;

        var atCut = plan.Candidates.First(static candidate => candidate.Slots[SectionA].IsAtCut);
        var frame = atCut.RectsById[Front];
        var section = atCut.RectsById[SectionA];
        var expected = front.OriginY + (300 / front.Scale) - ((front.LayoutRect!.MinY + front.LayoutRect.MaxY) / 2);
        Assert.Equal(expected, ((section.MinY + section.MaxY) - (frame.MinY + frame.MaxY)) / 2, 9);
        Assert.DoesNotContain(plan.Candidates, static candidate => candidate.Slots[SectionB].IsAtCut);
    }

    [Fact]
    public void View3D_IsLaidOutToo_InTheRoomLeft()
    {
        var reference = Reference();
        reference.Views.Add(View(
            8, "_3DView", "Model3D", "Model3D", new ReservedRect(0, 0, 100, 60), "", "Off"));

        var result = Evaluate(reference)!;

        Assert.Equal(6, result.IncludedViews);
        Assert.Equal(0, result.SkippedViews);
        Assert.NotNull(result.Best);
        Assert.True(result.Best.IsFeasible);
        var placed = result.Best.Candidate.Views.Single(static view => view.Id == 8).LayoutRect!;
        Assert.Equal(100, placed.Width, 9);
        Assert.All(
            result.Best.Candidate.Views.Where(static view => view.Id != 8),
            view => Assert.False(ViewPlacementValidator.Intersects(placed, view.LayoutRect!)));
    }

    [Fact]
    public void ViewsAFewMillimetersShortOfFitting_AreDrawnCloserTogether_NotMovedAway()
    {
        // In line: 200 + 50 + 50 and two gaps. Usable width 305: 3 short at a gap
        // of 4, fits at a gap of 2.
        var reference = Reference();
        reference.Sheet = new DrawingSheetContext { Width = 325, Height = 420 };

        var best = Evaluate(reference)!;

        Assert.Equal(0, best.BestPlan!.Cost);
        var front = best.Best!.Candidate.Views.Single(static view => view.Id == Front).LayoutRect!;
        var b = best.Best.Candidate.Views.Single(static view => view.Id == SectionB).LayoutRect!;
        Assert.Equal(2, b.MinX - front.MaxX, 9);
    }

    [Fact]
    public void ModelAnchor_IsFrameCenterMinusWhereTheModelOriginLands()
    {
        // View looking along -Y: view X is model X, view Y is model Z.
        var anchor = ConstraintLayoutCaseModelAnchor.FromViewCoordinateSystem(
            viewId: 7,
            frame: new ReservedRect(100, 50, 300, 150),
            viewOriginX: 120,
            viewOriginY: 60,
            scale: 10,
            coordinateSystemOrigin: (-500, 0, -200),
            axisX: (2, 0, 0),
            axisY: (0, 0, 1));

        // The model origin is at (500, 200) in view coordinates, 50 x 20 on paper.
        Assert.NotNull(anchor);
        Assert.Equal(200 - (120 + 50), anchor.X, 9);
        Assert.Equal(100 - (60 + 20), anchor.Y, 9);
        Assert.Null(ConstraintLayoutCaseModelAnchor.FromViewCoordinateSystem(
            7, new ReservedRect(0, 0, 1, 1), 0, 0, scale: 0, (0, 0, 0), (1, 0, 0), (0, 1, 0)));
    }

    [Fact]
    public void RunningAgainOnItsOwnResult_MovesNothing()
    {
        var first = Evaluate(Reference())!.Best!.Candidate;
        var second = Evaluate(first)!.Best!.Candidate;

        foreach (var view in first.Views)
        {
            var again = second.Views.Single(other => other.Id == view.Id);
            Assert.Equal(view.LayoutRect!.MinX, again.LayoutRect!.MinX, 9);
            Assert.Equal(view.LayoutRect.MinY, again.LayoutRect.MinY, 9);
            Assert.Equal(view.OriginX, again.OriginX, 9);
            Assert.Equal(view.OriginY, again.OriginY, 9);
        }
    }

    [Fact]
    public void SavedCase_ReplaysToTheSameLayout()
    {
        var directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "svmcp-layout-case-" + System.Guid.NewGuid().ToString("N"));
        try
        {
            var reference = Reference();
            reference.Drawing = new DrawingInfo { Guid = "11111111-2222-3333-4444-555555555555", Name = "M.1" };
            reference.ReservedLayout.Areas.Add(new ReservedRect(300, 10, 410, 60));
            var original = Case(reference);

            var path = ConstraintLayoutCaseStore.Save(directory, original);
            var loaded = ConstraintLayoutCaseStore.Load(path);

            Assert.EndsWith("11111111-2222-3333-4444-555555555555.json", path);
            var expected = ConstraintLayoutShadow.Evaluate(original)!.Best!.Candidate;
            var actual = ConstraintLayoutShadow.Evaluate(loaded)!.Best!.Candidate;
            Assert.Equal(
                expected.Views.Select(static view => (view.Id, view.LayoutRect!.MinX, view.LayoutRect.MinY, view.ActualPlacementSide)),
                actual.Views.Select(static view => (view.Id, view.LayoutRect!.MinX, view.LayoutRect.MinY, view.ActualPlacementSide)));
        }
        finally
        {
            if (System.IO.Directory.Exists(directory))
                System.IO.Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void WhereTheViewsStoodBefore_AndWhatSideWasRecordedOnThem_DoNotChangeTheLayout()
    {
        // The planner reads the views as they stand on the drawing. The same views
        // standing elsewhere, with no side recorded on them, must come out alike --
        // or running the command twice would give two layouts.
        DrawingLayoutCandidate Build(bool movedAndBare)
        {
            var reference = Reference();
            reference.Views.Add(View(
                8, "_3DView", "Model3D", "Model3D", new ReservedRect(0, 0, 100, 60), "", "Off"));
            if (!movedAndBare)
                return reference;

            var index = 0;
            foreach (var view in reference.Views)
            {
                var dx = 13.37 * ++index;
                var dy = -7.11 * index;
                var rect = view.LayoutRect!;
                view.LayoutRect = new ReservedRect(rect.MinX + dx, rect.MinY + dy, rect.MaxX + dx, rect.MaxY + dy);
                view.OriginX += dx;
                view.OriginY += dy;
                view.PreferredPlacementSide = string.Empty;
                view.ActualPlacementSide = string.Empty;
            }

            return reference;
        }

        // The sides come with the case, as the planner's reader supplies them.
        ConstraintLayoutCase WithSides(DrawingLayoutCandidate reference)
        {
            var layoutCase = Case(reference);
            layoutCase.ViewSides =
            [
                new ConstraintLayoutCaseSide { ViewId = Top, Side = "Top" },
                new ConstraintLayoutCaseSide { ViewId = SectionA, Side = "Right" },
                new ConstraintLayoutCaseSide { ViewId = SectionB, Side = "Right" }
            ];
            return layoutCase;
        }

        var asLaidOut = ConstraintLayoutShadow.Evaluate(WithSides(Build(movedAndBare: false)))!.Best!.Candidate;
        var movedAbout = ConstraintLayoutShadow.Evaluate(WithSides(Build(movedAndBare: true)))!.Best!.Candidate;

        foreach (var view in asLaidOut.Views)
        {
            var other = movedAbout.Views.Single(candidate => candidate.Id == view.Id);
            Assert.Equal(view.LayoutRect!.MinX, other.LayoutRect!.MinX, 6);
            Assert.Equal(view.LayoutRect.MinY, other.LayoutRect.MinY, 6);
            Assert.Equal(view.PreferredPlacementSide, other.PreferredPlacementSide);
            Assert.Equal(view.ActualPlacementSide, other.ActualPlacementSide);
        }
    }

    private static DrawingLayoutCandidate Reference()
        => new()
        {
            Name = "fit_views_to_sheet:planned-final",
            Sheet = new DrawingSheetContext { Width = 420, Height = 297 },
            ReservedLayout = new DrawingReservedLayoutContext { Margin = 10 },
            Views =
            [
                View(Front, "FrontView", "BaseProjected", "MainProjected", new ReservedRect(20, 20, 220, 120), "", "Strong"),
                View(Top, "TopView", "BaseProjected", "MainProjected", new ReservedRect(20, 130, 220, 190), "Top", "Strong"),
                View(SectionA, "SectionView", "Section", "StandardSection", new ReservedRect(230, 20, 280, 120), "Right", "Relaxed"),
                View(SectionB, "SectionView", "Section", "StandardSection", new ReservedRect(290, 20, 340, 120), "Right", "Relaxed"),
                View(Detail, "DetailView", "Detail", "Detail", new ReservedRect(350, 200, 400, 250), "", "Weak")
            ]
        };

    private static DrawingLayoutCandidateView View(
        int id,
        string viewType,
        string semanticKind,
        string layoutViewKind,
        ReservedRect rect,
        string preferredSide,
        string projectionStrength)
        => new()
        {
            Id = id,
            ViewType = viewType,
            SemanticKind = semanticKind,
            LayoutViewKind = layoutViewKind,
            Scale = 10,
            // An origin that is not the frame center, as on a real view.
            OriginX = rect.MinX + 7,
            OriginY = rect.MinY + 3,
            Width = rect.Width,
            Height = rect.Height,
            LayoutRect = rect,
            PreferredPlacementSide = preferredSide,
            ActualPlacementSide = preferredSide,
            ProjectionStrength = projectionStrength
        };
}
