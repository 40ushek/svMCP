using System;
using System.IO;
using System.Text.Json;
using TeklaMcpServer.Api.Drawing;
using TeklaMcpServer.Api.Drawing.ViewLayout;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DrawingCaseCaptureServiceTests
{
    [Fact]
    public void CaptureDrawingContext_ReturnsContext_WhenCaptureSucceeds()
    {
        var expected = CreateContext("drawing-guid-1", "Captured drawing");
        var service = new DrawingCaseCaptureService(
            () => new GetDrawingLayoutContextResult
            {
                Success = true,
                Context = expected
            },
            new DrawingLayoutScorer(),
            new DrawingCaseSnapshotWriter());

        var actual = service.CaptureDrawingContext();

        Assert.Same(expected, actual);
    }

    [Fact]
    public void CaptureDrawingContext_Throws_WhenCaptureFails()
    {
        var service = new DrawingCaseCaptureService(
            () => new GetDrawingLayoutContextResult
            {
                Success = false,
                Error = "drawing not open"
            },
            new DrawingLayoutScorer(),
            new DrawingCaseSnapshotWriter());

        var ex = Assert.Throws<InvalidOperationException>(() => service.CaptureDrawingContext());

        Assert.Contains("drawing not open", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SaveCase_ComputesScoresAndWritesCaseFiles()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            var before = CreateContext("drawing-guid-1", "Assembly drawing");
            var after = CreateContext("drawing-guid-1", "Assembly drawing");
            after.Views[0].OriginX = 30;

            var service = new DrawingCaseCaptureService(
                () => throw new InvalidOperationException("Capture should not be used in SaveCase test."),
                new DrawingLayoutScorer(),
                new DrawingCaseSnapshotWriter());

            var result = service.SaveCase(
                before,
                after,
                tempRoot,
                "assembly",
                "fit_views_to_sheet",
                "captured example");

            var meta = JsonSerializer.Deserialize<DrawingCaseMeta>(
                File.ReadAllText(result.MetaPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            Assert.NotNull(meta);
            Assert.NotNull(meta.ScoreBefore);
            Assert.NotNull(meta.ScoreAfter);
            Assert.Equal("drawing-guid-1", meta.DrawingGuid);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void SaveCase_Throws_WhenDrawingGuidsDiffer()
    {
        var service = new DrawingCaseCaptureService(
            () => throw new InvalidOperationException("Capture should not be used in SaveCase test."),
            new DrawingLayoutScorer(),
            new DrawingCaseSnapshotWriter());

        var before = CreateContext("drawing-guid-1", "Before");
        var after = CreateContext("drawing-guid-2", "After");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            service.SaveCase(
                before,
                after,
                Path.GetTempPath(),
                "assembly",
                "fit_views_to_sheet"));

        Assert.Contains("same drawing_guid", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SaveCase_PassesLayoutDiagnosticsToWriter()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            var before = CreateContext("drawing-guid-1", "Assembly drawing");
            var after = CreateContext("drawing-guid-1", "Assembly drawing");
            var service = new DrawingCaseCaptureService(
                () => throw new InvalidOperationException("Capture should not be used in SaveCase test."),
                new DrawingLayoutScorer(),
                new DrawingCaseSnapshotWriter());

            var result = service.SaveCase(
                before,
                after,
                tempRoot,
                "assembly",
                "fit_views_to_sheet",
                layoutDiagnostics: new DrawingCaseLayoutDiagnostics
                {
                    SelectedCandidateName = "fit_views_to_sheet:final"
                });

            var meta = JsonSerializer.Deserialize<DrawingCaseMeta>(
                File.ReadAllText(result.MetaPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            Assert.Equal("fit_views_to_sheet:final", meta?.LayoutDiagnostics?.SelectedCandidateName);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "svmcp-drawing-case-capture-tests", Guid.NewGuid().ToString("N"));
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
