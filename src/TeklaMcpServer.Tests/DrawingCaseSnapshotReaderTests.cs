using System;
using System.IO;
using TeklaMcpServer.Api.Drawing;
using TeklaMcpServer.Api.Drawing.ViewLayout;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DrawingCaseSnapshotReaderTests
{
    [Fact]
    public void Load_ReadsBeforeAfterAndMeta()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            var before = CreateContext("drawing-guid-1", "Before");
            var after = CreateContext("drawing-guid-1", "After");
            var saveResult = new DrawingCaseSnapshotWriter().Save(
                tempRoot,
                "assembly",
                "fit_views_to_sheet",
                before,
                after,
                layoutStability: new DrawingLayoutStabilityReport
                {
                    DrawingGuid = "drawing-guid-1",
                    SameDrawing = true,
                    IsStable = true
                });

            var snapshot = new DrawingCaseSnapshotReader().Load(saveResult.CaseDirectory);

            Assert.Equal(saveResult.CaseDirectory, snapshot.CaseDirectory);
            Assert.Equal("drawing-guid-1", snapshot.Before.Drawing.Guid);
            Assert.Equal("drawing-guid-1", snapshot.After.Drawing.Guid);
            Assert.Equal("fit_views_to_sheet", snapshot.Meta.Operation);
            Assert.True(snapshot.Meta.LayoutStability?.IsStable);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void Load_Throws_WhenCaseFileIsMissing()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            var ex = Assert.Throws<FileNotFoundException>(() =>
                new DrawingCaseSnapshotReader().Load(tempRoot));

            Assert.Contains("before.json", ex.FileName, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "svmcp-drawing-case-reader-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static DrawingContext CreateContext(string drawingGuid, string drawingName)
        => new()
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
