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

    private static readonly Dictionary<int, (double? X, double? Y)> Anchors = new()
    {
        [SectionA] = (150.0, 100.0),
        [SectionB] = (80.0, 100.0)
    };

    [Fact]
    public void LaysOutSkeletonViews_AndScoresBothLayoutsOnTheSameViews()
    {
        var result = ConstraintLayoutShadow.Evaluate(Reference(), Front, Gap, Anchors);

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
        var best = ConstraintLayoutShadow.Evaluate(Reference(), Front, Gap, Anchors)!.Best!.Candidate;

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
        var after = ConstraintLayoutShadow.Evaluate(reference, Front, Gap, Anchors)!
            .Best!.Candidate.Views.Single(static view => view.Id == SectionA);

        Assert.Equal(before.OriginX - before.LayoutRect!.MinX, after.OriginX - after.LayoutRect!.MinX, 9);
        Assert.Equal(before.OriginY - before.LayoutRect.MinY, after.OriginY - after.LayoutRect.MinY, 9);
    }

    [Fact]
    public void SheetTooSmall_ReportsNoFit_AndStillScoresTheReference()
    {
        var reference = Reference();
        reference.Sheet = new DrawingSheetContext { Width = 150, Height = 120 };

        var result = ConstraintLayoutShadow.Evaluate(reference, Front, Gap, Anchors);

        Assert.NotNull(result);
        Assert.Null(result.Best);
        Assert.Empty(result.Plan.Candidates);
        Assert.True(result.Plan.PreferredRequiredWidth > 130);
        Assert.Equal(4, result.Reference.Candidate.Views.Count);
    }

    [Fact]
    public void BaseViewNotInReference_ReturnsNull()
        => Assert.Null(ConstraintLayoutShadow.Evaluate(Reference(), baseViewId: 99, Gap, Anchors));

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
