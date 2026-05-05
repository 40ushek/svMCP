using System;
using System.Collections.Generic;
using System.Linq;
using TeklaMcpServer.Api.Drawing.ViewLayout;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class DrawingLayoutStabilityOptions
{
    public double MovementTolerance { get; set; } = DrawingLayoutCandidateApplyTolerances.Movement;

    public double ScaleTolerance { get; set; } = DrawingLayoutCandidateApplyTolerances.Scale;

    public double ScoreTolerance { get; set; } = 0.001;

    public double OverlapAreaTolerance { get; set; } = 0.001;
}

internal sealed class DrawingLayoutStabilityReport
{
    public string DrawingGuid { get; set; } = string.Empty;

    public bool SameDrawing { get; set; }

    public int FirstViewCount { get; set; }

    public int SecondViewCount { get; set; }

    public int ComparableViewCount { get; set; }

    public int MissingFromSecondCount { get; set; }

    public int AddedInSecondCount { get; set; }

    public int MovedCount { get; set; }

    public int ScaleChangedCount { get; set; }

    public double MaxOriginDelta { get; set; }

    public double AverageOriginDelta { get; set; }

    public double FirstScore { get; set; }

    public double SecondScore { get; set; }

    public double ScoreDelta { get; set; }

    public double ViewOverlapAreaDelta { get; set; }

    public double ReservedOverlapAreaDelta { get; set; }

    public bool IsStable { get; set; }

    public List<string> Diagnostics { get; set; } = new();
}

internal sealed class DrawingLayoutStabilityAnalyzer
{
    private readonly DrawingLayoutScorer scorer;

    public DrawingLayoutStabilityAnalyzer()
        : this(new DrawingLayoutScorer())
    {
    }

    public DrawingLayoutStabilityAnalyzer(DrawingLayoutScorer scorer)
    {
        this.scorer = scorer ?? throw new ArgumentNullException(nameof(scorer));
    }

    public DrawingLayoutStabilityReport Analyze(
        DrawingContext firstRunAfter,
        DrawingContext secondRunAfter,
        DrawingLayoutStabilityOptions? options = null)
    {
        if (firstRunAfter == null)
            throw new ArgumentNullException(nameof(firstRunAfter));
        if (secondRunAfter == null)
            throw new ArgumentNullException(nameof(secondRunAfter));

        var effectiveOptions = options ?? new DrawingLayoutStabilityOptions();
        var firstScore = scorer.Score(firstRunAfter);
        var secondScore = scorer.Score(secondRunAfter);
        var firstWorkspace = DrawingLayoutWorkspace.From(firstRunAfter);
        var secondWorkspace = DrawingLayoutWorkspace.From(secondRunAfter);
        var firstViews = firstWorkspace.ViewsById;
        var secondViews = secondWorkspace.ViewsById;

        var report = new DrawingLayoutStabilityReport
        {
            DrawingGuid = ResolveDrawingGuid(firstRunAfter, secondRunAfter),
            SameDrawing = HaveSameDrawingGuid(firstRunAfter, secondRunAfter),
            FirstViewCount = firstViews.Count,
            SecondViewCount = secondViews.Count,
            FirstScore = firstScore.TotalScore,
            SecondScore = secondScore.TotalScore,
            ScoreDelta = secondScore.TotalScore - firstScore.TotalScore,
            ViewOverlapAreaDelta = secondScore.Breakdown.ViewOverlapArea - firstScore.Breakdown.ViewOverlapArea,
            ReservedOverlapAreaDelta = secondScore.Breakdown.ReservedOverlapArea - firstScore.Breakdown.ReservedOverlapArea
        };

        if (!report.SameDrawing)
            report.Diagnostics.Add("layout-stability:different-drawing-guid");

        var missingFromSecond = firstViews.Keys
            .Where(viewId => !secondViews.ContainsKey(viewId))
            .OrderBy(static viewId => viewId)
            .ToList();
        var addedInSecond = secondViews.Keys
            .Where(viewId => !firstViews.ContainsKey(viewId))
            .OrderBy(static viewId => viewId)
            .ToList();
        report.MissingFromSecondCount = missingFromSecond.Count;
        report.AddedInSecondCount = addedInSecond.Count;
        foreach (var viewId in missingFromSecond)
            report.Diagnostics.Add($"layout-stability:missing-second:view={viewId}");
        foreach (var viewId in addedInSecond)
            report.Diagnostics.Add($"layout-stability:added-second:view={viewId}");

        var distances = new List<double>();
        foreach (var viewId in firstViews.Keys.Intersect(secondViews.Keys).OrderBy(static id => id))
        {
            var first = firstViews[viewId];
            var second = secondViews[viewId];
            var dx = second.OriginX - first.OriginX;
            var dy = second.OriginY - first.OriginY;
            var distance = Math.Sqrt((dx * dx) + (dy * dy));
            distances.Add(distance);
            report.ComparableViewCount++;

            if (distance >= effectiveOptions.MovementTolerance)
            {
                report.MovedCount++;
                report.Diagnostics.Add($"layout-stability:moved:view={viewId}:distance={distance:0.###}");
            }

            if (Math.Abs(second.Scale - first.Scale) >= effectiveOptions.ScaleTolerance)
            {
                report.ScaleChangedCount++;
                report.Diagnostics.Add($"layout-stability:scale-changed:view={viewId}");
            }
        }

        report.MaxOriginDelta = distances.Count == 0 ? 0.0 : distances.Max();
        report.AverageOriginDelta = distances.Count == 0 ? 0.0 : distances.Average();
        if (Math.Abs(report.ScoreDelta) > effectiveOptions.ScoreTolerance)
            report.Diagnostics.Add($"layout-stability:score-delta={report.ScoreDelta:0.###}");
        if (report.ViewOverlapAreaDelta > effectiveOptions.OverlapAreaTolerance)
            report.Diagnostics.Add($"layout-stability:view-overlap-increased:area={report.ViewOverlapAreaDelta:0.###}");
        if (report.ReservedOverlapAreaDelta > effectiveOptions.OverlapAreaTolerance)
            report.Diagnostics.Add($"layout-stability:reserved-overlap-increased:area={report.ReservedOverlapAreaDelta:0.###}");

        report.IsStable =
            report.SameDrawing &&
            report.MissingFromSecondCount == 0 &&
            report.AddedInSecondCount == 0 &&
            report.MovedCount == 0 &&
            report.ScaleChangedCount == 0 &&
            Math.Abs(report.ScoreDelta) <= effectiveOptions.ScoreTolerance &&
            report.ViewOverlapAreaDelta <= effectiveOptions.OverlapAreaTolerance &&
            report.ReservedOverlapAreaDelta <= effectiveOptions.OverlapAreaTolerance;

        return report;
    }

    private static string ResolveDrawingGuid(DrawingContext first, DrawingContext second)
        => second.Drawing?.Guid?.Trim() ?? first.Drawing?.Guid?.Trim() ?? string.Empty;

    private static bool HaveSameDrawingGuid(DrawingContext first, DrawingContext second)
    {
        var firstGuid = first.Drawing?.Guid?.Trim();
        var secondGuid = second.Drawing?.Guid?.Trim();
        return !string.IsNullOrWhiteSpace(firstGuid) &&
            !string.IsNullOrWhiteSpace(secondGuid) &&
            string.Equals(firstGuid, secondGuid, StringComparison.OrdinalIgnoreCase);
    }
}
