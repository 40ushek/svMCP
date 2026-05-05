using System;
using System.IO;
using System.Text.Json;
using TeklaMcpServer.Api.Drawing;
using TeklaMcpServer.Api.Drawing.ViewLayout;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DrawingCaseSnapshotWriterTests
{
    [Fact]
    public void Save_WritesBeforeAfterAndMetaIntoDrawingGuidFolder()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            var before = CreateContext("drawing-guid-1", "Assembly drawing");
            var after = CreateContext("drawing-guid-1", "Assembly drawing");
            var scorer = new DrawingLayoutScorer();
            var scoreBefore = scorer.Score(before);
            var scoreAfter = scorer.Score(after);

            var result = new DrawingCaseSnapshotWriter().Save(
                tempRoot,
                "assembly",
                "fit_views_to_sheet",
                before,
                after,
                "good result",
                scoreBefore,
                scoreAfter);

            Assert.Equal(Path.Combine(tempRoot, "assembly", "drawing-guid-1"), result.CaseDirectory);
            Assert.True(File.Exists(result.BeforePath));
            Assert.True(File.Exists(result.AfterPath));
            Assert.True(File.Exists(result.MetaPath));

            var metaJson = File.ReadAllText(result.MetaPath);
            var meta = JsonSerializer.Deserialize<DrawingCaseMeta>(metaJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            Assert.NotNull(meta);
            Assert.Equal("drawing-guid-1", meta.DrawingGuid);
            Assert.Equal("Assembly", meta.DrawingType);
            Assert.Equal("Assembly drawing", meta.DrawingName);
            Assert.Equal("fit_views_to_sheet", meta.Operation);
            Assert.Equal("good result", meta.Note);
            Assert.NotNull(meta.ScoreBefore);
            Assert.NotNull(meta.ScoreAfter);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void Save_Throws_WhenBeforeAndAfterHaveDifferentDrawingGuids()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            var before = CreateContext("drawing-guid-1", "Before");
            var after = CreateContext("drawing-guid-2", "After");

            var ex = Assert.Throws<InvalidOperationException>(() =>
                new DrawingCaseSnapshotWriter().Save(
                    tempRoot,
                    "assembly",
                    "fit_views_to_sheet",
                    before,
                    after));

            Assert.Contains("different drawing_guid", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void Save_WritesLayoutDiagnosticsIntoMeta()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            var before = CreateContext("drawing-guid-1", "Assembly drawing");
            var after = CreateContext("drawing-guid-1", "Assembly drawing");
            var diagnostics = new DrawingCaseLayoutDiagnostics
            {
                SelectedCandidateName = "fit_views_to_sheet:planned-centered",
                SelectedCandidateScore = 42.5,
                SelectedCandidateFeasible = true,
                ApplyPlan = new DrawingCaseApplyPlanSummary
                {
                    CanApply = true,
                    Reason = "planned-candidate",
                    MoveCount = 2
                },
                ApplyDelta = new DrawingCaseApplyDeltaSummary
                {
                    MoveCount = 2,
                    ComparableMoveCount = 2,
                    MovedCount = 1,
                    MaxDelta = 10,
                    AverageDelta = 5
                },
                ApplySafety = new DrawingCaseApplySafetySummary
                {
                    RequestedMode = "Apply",
                    EffectiveMode = "DryRun",
                    Allowed = false,
                    Reason = "scale-changed"
                }
            };

            var result = new DrawingCaseSnapshotWriter().Save(
                tempRoot,
                "assembly",
                "fit_views_to_sheet",
                before,
                after,
                layoutDiagnostics: diagnostics);

            var meta = JsonSerializer.Deserialize<DrawingCaseMeta>(
                File.ReadAllText(result.MetaPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            Assert.NotNull(meta?.LayoutDiagnostics);
            Assert.Equal("fit_views_to_sheet:planned-centered", meta.LayoutDiagnostics.SelectedCandidateName);
            Assert.Equal(2, meta.LayoutDiagnostics.ApplyPlan?.MoveCount);
            Assert.Equal(1, meta.LayoutDiagnostics.ApplyDelta?.MovedCount);
            Assert.Equal("scale-changed", meta.LayoutDiagnostics.ApplySafety?.Reason);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "svmcp-drawing-case-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static DrawingContext CreateContext(string drawingGuid, string drawingName)
    {
        return new DrawingContext
        {
            Drawing = new DrawingInfo
            {
                Guid = drawingGuid,
                Name = drawingName,
                Type = "Assembly"
            },
            Sheet = new DrawingSheetContext
            {
                Width = 200,
                Height = 100
            },
            Views =
            [
                new DrawingViewInfo
                {
                    Id = 1,
                    Name = "MAIN",
                    ViewType = "FrontView",
                    SemanticKind = ViewSemanticKind.BaseProjected.ToString(),
                    Scale = 20,
                    Width = 60,
                    Height = 30,
                    OriginX = 10,
                    OriginY = 10
                }
            ]
        };
    }
}
