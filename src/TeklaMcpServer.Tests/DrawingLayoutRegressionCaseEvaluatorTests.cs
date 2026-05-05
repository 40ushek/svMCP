using System;
using System.IO;
using TeklaMcpServer.Api.Drawing;
using TeklaMcpServer.Api.Drawing.ViewLayout;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DrawingLayoutRegressionCaseEvaluatorTests
{
    [Fact]
    public void EvaluateRepeatedRun_ComparesAfterContextsFromSavedCases()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            var firstCase = SaveCase(
                tempRoot,
                "run-1",
                CreateContext("drawing-guid-1", originX: 10));
            var secondCase = SaveCase(
                tempRoot,
                "run-2",
                CreateContext("drawing-guid-1", originX: 20));

            var evaluation = new DrawingLayoutRegressionCaseEvaluator()
                .EvaluateRepeatedRun(firstCase.CaseDirectory, secondCase.CaseDirectory);

            Assert.Equal("fit_views_to_sheet", evaluation.FirstRunOperation);
            Assert.Equal("fit_views_to_sheet", evaluation.SecondRunOperation);
            Assert.False(evaluation.Stability.IsStable);
            Assert.Equal(1, evaluation.Stability.MovedCount);
            Assert.Equal(10, evaluation.Stability.MaxOriginDelta, 3);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void EvaluateRepeatedRun_ReportsStable_WhenAfterContextsMatch()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            var firstCase = SaveCase(
                tempRoot,
                "run-1",
                CreateContext("drawing-guid-1", originX: 10));
            var secondCase = SaveCase(
                tempRoot,
                "run-2",
                CreateContext("drawing-guid-1", originX: 10));

            var evaluation = new DrawingLayoutRegressionCaseEvaluator()
                .EvaluateRepeatedRun(firstCase.CaseDirectory, secondCase.CaseDirectory);

            Assert.True(evaluation.Stability.IsStable);
            Assert.Equal(0, evaluation.Stability.MovedCount);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static DrawingCaseSnapshotSaveResult SaveCase(
        string rootDirectory,
        string note,
        DrawingContext after)
    {
        var before = CreateContext(after.Drawing.Guid, originX: 0);
        return new DrawingCaseSnapshotWriter().Save(
            rootDirectory,
            note,
            "fit_views_to_sheet",
            before,
            after,
            note);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "svmcp-drawing-regression-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static DrawingContext CreateContext(string drawingGuid, double originX)
        => new()
        {
            Drawing = new DrawingInfo
            {
                Guid = drawingGuid,
                Name = "Assembly drawing",
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
                    OriginX = originX,
                    OriginY = 10
                }
            ]
        };
}
