using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using TeklaMcpServer.Api.Drawing;
using TeklaMcpServer.Api.Drawing.ViewLayout.Constraints;
using Xunit;
using Xunit.Abstractions;

namespace TeklaMcpServer.Tests;

/// <summary>
/// Replays captured fit_views_to_sheet runs through the constraint planner
/// (ROADMAP_CONSTRAINT_LAYOUT.md, phase 3). Cases are local data: copy them from
/// %LOCALAPPDATA%\svMCP\LayoutCases into cases/layout_cases at the repository root.
/// With no cases the test passes and says so.
/// </summary>
public sealed class ConstraintLayoutCorpusTests
{
    private readonly ITestOutputHelper _output;

    public ConstraintLayoutCorpusTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void EveryCase_ReplaysTheSameWayTwice_AndSettlesOnItsOwnResult()
    {
        var directory = FindCorpusDirectory();
        var files = directory == null
            ? Array.Empty<string>()
            : Directory.GetFiles(directory, "*.json").OrderBy(static path => path, StringComparer.Ordinal).ToArray();
        _output.WriteLine($"corpus={directory ?? "not found"} cases={files.Length}");

        var fitsWhereReferenceFits = 0;
        var referenceFits = 0;
        foreach (var file in files)
        {
            var layoutCase = ConstraintLayoutCaseStore.Load(file);
            var result = ConstraintLayoutShadow.Evaluate(layoutCase);
            if (result == null)
            {
                _output.WriteLine($"{Path.GetFileName(file)}: skipped, base view {layoutCase.BaseViewId} has no frame");
                continue;
            }

            _output.WriteLine(Describe(file, layoutCase, result));
            if (result.Reference.IsFeasible)
            {
                referenceFits++;
                if (result.Best?.IsFeasible == true)
                    fitsWhereReferenceFits++;
            }

            if (result.Best == null)
                continue;

            // Same input, same layout.
            Assert.Equal(Rects(result.Best.Candidate), Rects(ConstraintLayoutShadow.Evaluate(layoutCase)!.Best!.Candidate));

            // A second run that starts from the planner's own result moves nothing.
            var settled = ConstraintLayoutShadow.Evaluate(new ConstraintLayoutCase
            {
                Reference = result.Best.Candidate,
                BaseViewId = layoutCase.BaseViewId,
                Gap = layoutCase.Gap,
                SectionLineAnchors = layoutCase.SectionLineAnchors
            });
            Assert.Equal(Rects(result.Best.Candidate), Rects(settled!.Best!.Candidate));
        }

        // Reported, not asserted: this is the switch-over criterion, and the planner
        // is not expected to meet it on every drawing yet.
        _output.WriteLine($"fits where the current layout fits: {fitsWhereReferenceFits}/{referenceFits}");
    }

    private static string Describe(string file, ConstraintLayoutCase layoutCase, ConstraintLayoutShadowResult result)
        => string.Format(
            CultureInfo.InvariantCulture,
            "{0} [{1}]: views={2} skipped={3} | current: feasible={4} fallbackViews={5} score={6:0.###} | " +
            "planner: {7} deviations={8} score={9:0.###} topologies={10}{11} preferredNeeds={12:0.#}x{13:0.#}",
            Path.GetFileName(file),
            layoutCase.Reference.Drawing.Name ?? "unnamed",
            result.IncludedViews,
            result.SkippedViews,
            result.Reference.IsFeasible ? 1 : 0,
            result.Reference.Candidate.Views.Count(static view => view.PlacementFallbackUsed),
            result.Reference.Score.TotalScore,
            result.Best == null ? "no-fit" : result.Best.IsFeasible ? "fits" : "infeasible",
            result.BestPlan?.Deviations ?? 0,
            result.Best?.Score.TotalScore ?? 0.0,
            result.Plan.EvaluatedTopologies,
            result.Plan.Truncated ? "(truncated)" : string.Empty,
            result.Plan.PreferredRequiredWidth,
            result.Plan.PreferredRequiredHeight);

    private static List<(int Id, double MinX, double MinY, double MaxX, double MaxY)> Rects(DrawingLayoutCandidate candidate)
        => candidate.Views
            .OrderBy(static view => view.Id)
            .Select(static view => (
                view.Id,
                Math.Round(view.LayoutRect!.MinX, 6),
                Math.Round(view.LayoutRect.MinY, 6),
                Math.Round(view.LayoutRect.MaxX, 6),
                Math.Round(view.LayoutRect.MaxY, 6)))
            .ToList();

    private static string? FindCorpusDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "cases", "layout_cases");
            if (Directory.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
