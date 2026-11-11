using System.Collections.Generic;
using System.Linq;
using TeklaMcpServer.Api.Drawing;
using TeklaMcpServer.Api.Drawing.ViewLayout.Constraints;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class ConstraintScaleVariantsTests
{
    private const int Front = 1;
    private const int Top = 2;
    private const int Section = 3;

    [Fact]
    public void OneMeasurement_GivesAProportionalSize()
    {
        var estimator = new ConstraintFrameSizeEstimator();
        estimator.Record(Front, 10, 200, 100);

        var size = estimator.Estimate(Front, 5)!.Value;

        Assert.Equal(400, size.Width, 6);
        Assert.Equal(200, size.Height, 6);
    }

    [Fact]
    public void TwoMeasurements_SeparateThePartThatDoesNotScale()
    {
        // The end view of drawing M.503: 91 mm wide at 1:5 and 69 mm at 1:10, where
        // a proportional estimate said 45. That is 47 mm of marks and 220 mm of model.
        var estimator = new ConstraintFrameSizeEstimator();
        estimator.Record(Front, 5, 91, 91);
        estimator.Record(Front, 10, 69, 69);

        Assert.Equal(58, estimator.Estimate(Front, 20)!.Value.Width, 6);
        Assert.Equal(157, estimator.Estimate(Front, 2)!.Value.Width, 6);
    }

    [Fact]
    public void OneMeasurement_WithTheModelExtentKnown_KeepsTheFixedPartFixed()
    {
        // The same end view from one measurement: 91 mm at 1:5 of which 220 / 5 = 44 mm
        // is model. At 1:10 the model part halves and the 47 mm of marks stay.
        var estimator = new ConstraintFrameSizeEstimator();
        estimator.RecordModelExtent(Front, 220, 220);
        estimator.Record(Front, 5, 91, 91);

        Assert.Equal(69, estimator.Estimate(Front, 10)!.Value.Width, 6);
        Assert.Equal(157, estimator.Estimate(Front, 2)!.Value.Width, 6);
    }

    [Fact]
    public void AModelExtentLargerThanTheFrame_IsNotTrusted()
    {
        var estimator = new ConstraintFrameSizeEstimator();
        estimator.RecordModelExtent(Front, 5000, 5000);
        estimator.Record(Front, 10, 200, 100);

        Assert.Equal(400, estimator.Estimate(Front, 5)!.Value.Width, 6);
    }

    [Fact]
    public void AMeasuredScale_IsReturnedAsMeasured()
    {
        var estimator = new ConstraintFrameSizeEstimator();
        estimator.Record(Front, 5, 91, 80);
        estimator.Record(Front, 10, 69, 50);

        Assert.Equal((69, 50), estimator.Estimate(Front, 10)!.Value);
    }

    [Fact]
    public void AmongVariantsAboutAsFull_TheOneThatGivesUpLeastComesFirst()
    {
        var ordered = ConstraintScaleVariants.Order(new[]
        {
            new ConstraintScaleVariant { MainScale = 15, Fill = 0.30, Cost = 0 },
            new ConstraintScaleVariant { MainScale = 10, SectionScale = 5, Fill = 0.62, Cost = 1.5 },
            new ConstraintScaleVariant { MainScale = 10, SectionScale = 10, Fill = 0.59, Cost = 0 }
        }, ConstraintLayoutPolicy.Balanced.ScaleFillTie);

        Assert.Equal(new[] { 0.59, 0.62, 0.30 }, ordered.Select(static variant => variant.Fill));
    }

    [Fact]
    public void AmongVariantsAboutAsFull_TheLargerAssemblyComesFirst()
    {
        // Drawing M.179: end views at 1:2 beside a base view at 1:10 fill the sheet
        // as much as everything at 1:5 does. The user had chosen 1:5.
        var ordered = ConstraintScaleVariants.Order(new[]
        {
            new ConstraintScaleVariant { MainScale = 10, SectionScale = 2, Fill = 0.39, Cost = 0.25 },
            new ConstraintScaleVariant { MainScale = 5, SectionScale = 5, Fill = 0.37, Cost = 0.25 }
        }, ConstraintLayoutPolicy.Balanced.ScaleFillTie);

        Assert.Equal(5, ordered[0].MainScale);
    }

    [Fact]
    public void SectionsMayBeMoreThanOneStepLargerThanTheAssembly()
    {
        var variants = ConstraintScaleVariants.Rank(
            Case(sectionScale: 20),
            new[] { Front, Top },
            new[] { Section },
            ConstraintLayoutPolicy.Balanced,
            Estimator(sectionScale: 20));

        // Assembly 1:15 with the section at 1:5: two steps of the series apart.
        Assert.Contains(variants, static variant => variant.MainScale == 15 && variant.ScalesById[Section] == 5);
    }

    [Fact]
    public void ScalesLargerThanOneToFive_AreNotOffered()
    {
        var variants = ConstraintScaleVariants.Rank(
            Case(), new[] { Front, Top }, new[] { Section }, ConstraintLayoutPolicy.Balanced, Estimator());

        Assert.All(variants, static variant => Assert.All(variant.ScalesById.Values, static scale => Assert.True(scale >= 5)));
    }

    [Fact]
    public void AClearlyFullerSheet_ComesFirst_WhateverItGivesUp()
    {
        var ordered = ConstraintScaleVariants.Order(new[]
        {
            new ConstraintScaleVariant { MainScale = 15, Fill = 0.26, Cost = 0 },
            new ConstraintScaleVariant { MainScale = 10, Fill = 0.59, Cost = 4.5 }
        }, ConstraintLayoutPolicy.Balanced.ScaleFillTie);

        Assert.Equal(10, ordered[0].MainScale);
    }

    [Fact]
    public void TheLargestAssemblyScaleThatFits_IsRankedFirst_AndSectionsAreNotShrunk()
    {
        // Sheet 420x297 with a 10 mm margin. At 1:10 the front view is 150x60; at 1:5
        // it is 300x120 and still fits with the top view above and the section beside.
        var variants = ConstraintScaleVariants.Rank(
            Case(), new[] { Front, Top }, new[] { Section }, ConstraintLayoutPolicy.Balanced, Estimator());

        Assert.NotEmpty(variants);
        Assert.Equal(5, variants[0].MainScale);
        Assert.All(variants, static variant => Assert.True(variant.ScalesById[Section] <= 5));
        Assert.All(variants, static variant => Assert.True(variant.ScalesById[Section] <= variant.MainScale));
    }

    [Fact]
    public void AScaleAtWhichAViewIsLargerThanTheSheet_IsNotOffered()
    {
        var variants = ConstraintScaleVariants.Rank(
            Case(), new[] { Front, Top }, new[] { Section }, ConstraintLayoutPolicy.Balanced, Estimator());

        // 1:2 would make the front view 750 mm wide.
        Assert.DoesNotContain(variants, static variant => variant.MainScale < 5);
    }

    [Fact]
    public void Rescale_KeepsTheFrameCenter_AndLeavesTheSourceAlone()
    {
        var source = Case();
        var rescaled = ConstraintScaleVariants.Rescale(source, new Dictionary<int, double> { [Front] = 5 }, Estimator());

        var before = source.Reference.Views.Single(static view => view.Id == Front);
        var after = rescaled.Reference.Views.Single(static view => view.Id == Front);
        Assert.Equal(10, before.Scale);
        Assert.Equal(150, before.LayoutRect!.Width, 6);
        Assert.Equal(5, after.Scale);
        Assert.Equal(300, after.LayoutRect!.Width, 6);
        Assert.Equal(
            (before.LayoutRect.MinX + before.LayoutRect.MaxX) / 2.0,
            (after.LayoutRect.MinX + after.LayoutRect.MaxX) / 2.0,
            6);
    }

    private static ConstraintFrameSizeEstimator Estimator(double sectionScale = 5)
    {
        var estimator = new ConstraintFrameSizeEstimator();
        foreach (var view in Case(sectionScale).Reference.Views)
            estimator.Record(view.Id, view.Scale, view.LayoutRect!.Width, view.LayoutRect.Height);
        return estimator;
    }

    private static ConstraintLayoutCase Case(double sectionScale = 5)
        => new()
        {
            BaseViewId = Front,
            Gap = 4,
            SectionLineAnchors = [new ConstraintLayoutCaseAnchor { ViewId = Section, X = 100, Y = 100 }],
            Reference = new DrawingLayoutCandidate
            {
                Name = "fit_views_to_sheet:planned-final",
                Sheet = new DrawingSheetContext { Width = 420, Height = 297 },
                ReservedLayout = new DrawingReservedLayoutContext { Margin = 10 },
                Views =
                [
                    View(Front, "FrontView", "BaseProjected", "MainProjected", new ReservedRect(100, 100, 250, 160), "", 10),
                    View(Top, "TopView", "BaseProjected", "MainProjected", new ReservedRect(100, 170, 250, 200), "Top", 10),
                    View(Section, "SectionView", "Section", "StandardSection", new ReservedRect(260, 100, 290, 160), "Right", sectionScale)
                ]
            }
        };

    private static DrawingLayoutCandidateView View(
        int id, string viewType, string semanticKind, string layoutViewKind, ReservedRect rect, string side, double scale)
        => new()
        {
            Id = id,
            ViewType = viewType,
            SemanticKind = semanticKind,
            LayoutViewKind = layoutViewKind,
            Scale = scale,
            OriginX = rect.MinX + 7,
            OriginY = rect.MinY + 3,
            Width = rect.Width,
            Height = rect.Height,
            LayoutRect = rect,
            PreferredPlacementSide = side,
            ActualPlacementSide = side,
            ProjectionStrength = semanticKind == "Section" ? "Relaxed" : "Strong"
        };
}
