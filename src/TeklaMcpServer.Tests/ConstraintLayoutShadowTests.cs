using System.Collections.Generic;
using System.Linq;
using TeklaMcpServer.Api.Drawing;
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
        Assert.Equal(4, result.IncludedViews);
        Assert.Equal(1, result.SkippedViews);
        Assert.Equal(4, result.Reference.Candidate.Views.Count);

        Assert.NotNull(result.Best);
        Assert.True(result.Best.IsFeasible);
        Assert.Equal(ConstraintLayoutShadow.CandidateName, result.Best.Candidate.Name);
        Assert.Equal(4, result.Best.Candidate.Views.Count);
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
        Assert.Equal(4, result.Reference.Candidate.Views.Count);
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
        Assert.Equal(3, result.AlignmentResidualMax, 9);
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
