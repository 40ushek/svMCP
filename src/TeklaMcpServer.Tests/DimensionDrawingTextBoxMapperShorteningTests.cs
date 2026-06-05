using System.Collections.Generic;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DimensionDrawingTextBoxMapperShorteningTests
{
    [Fact]
    public void ToDrawingTextBox_DefaultMode_KeepsRawPolygonAndCenter()
    {
        // Even with a mapper that has shortening, default mode (None) must not convert.
        var mapper = ViewShorteningCoordinateMapper.FromVisibleBoxes(new[]
        {
            new ViewShorteningVisibleBox(0, 0, 100, 500),
            new ViewShorteningVisibleBox(300, 0, 500, 500),
            new ViewShorteningVisibleBox(800, 0, 1000, 500)
        });
        var source = BuildPresentationBox(centerX: 850, centerY: 250);
        var result = DimensionDrawingTextBoxMapper.ToDrawingTextBox(source, textIndex: 0, mapper);

        Assert.Equal(850, result.CenterX, 6);
        Assert.Equal(250, result.CenterY, 6);
        Assert.Equal(845, result.Polygon[0][0], 6);
        Assert.Equal(855, result.Polygon[2][0], 6);
    }

    [Fact]
    public void ToDrawingTextBox_WithoutShorteningInMapper_DoesNotConvertEvenInExperimentalMode()
    {
        var mapper = ViewShorteningCoordinateMapper.FromVisibleBoxes(new[]
        {
            new ViewShorteningVisibleBox(0, 0, 1000, 500)
        });
        Assert.False(mapper.HasShortening);

        var source = BuildPresentationBox(centerX: 850, centerY: 250);
        var result = DimensionDrawingTextBoxMapper.ToDrawingTextBox(
            source, textIndex: 0, mapper, DimensionTextBoxShorteningMode.ToVisual);

        Assert.Equal(850, result.CenterX, 6);
        Assert.Equal(845, result.Polygon[0][0], 6);
    }

    [Fact]
    public void ToDrawingTextBox_ToVisualMode_ConvertsPolygonAndCenter()
    {
        var mapper = ViewShorteningCoordinateMapper.FromVisibleBoxes(new[]
        {
            new ViewShorteningVisibleBox(0, 0, 100, 500),
            new ViewShorteningVisibleBox(300, 0, 500, 500),
            new ViewShorteningVisibleBox(800, 0, 1000, 500)
        });
        Assert.True(mapper.HasShortening);

        var source = BuildPresentationBox(centerX: 850, centerY: 250);
        var result = DimensionDrawingTextBoxMapper.ToDrawingTextBox(
            source, textIndex: 0, mapper, DimensionTextBoxShorteningMode.ToVisual);

        Assert.Equal(350, result.CenterX, 6);
        Assert.Equal(345, result.Polygon[0][0], 6);
        Assert.Equal(355, result.Polygon[2][0], 6);
        Assert.Equal(345, result.MinX, 6);
        Assert.Equal(355, result.MaxX, 6);
    }

    [Fact]
    public void ToDrawingTextBox_ToVisualThenToRaw_RoundTripsToOriginal()
    {
        var mapper = ViewShorteningCoordinateMapper.FromVisibleBoxes(new[]
        {
            new ViewShorteningVisibleBox(0, 0, 100, 500),
            new ViewShorteningVisibleBox(300, 0, 500, 500),
            new ViewShorteningVisibleBox(800, 0, 1000, 500)
        });

        var source = BuildPresentationBox(centerX: 850, centerY: 250);

        // Stage 1: source -> ToVisual.
        var visual = DimensionDrawingTextBoxMapper.ToDrawingTextBox(
            source, textIndex: 0, mapper, DimensionTextBoxShorteningMode.ToVisual);
        Assert.Equal(350, visual.CenterX, 6);

        // Stage 2: feed the converted polygon back as a source and apply ToRaw.
        // This simulates the inverse transform: visual coords -> raw coords.
        var visualAsSource = new DimensionPresentationTextBox
        {
            SourceObjectId = 42,
            SourceObjectKind = "segment",
            Text = "100",
            CenterX = visual.CenterX,
            CenterY = visual.CenterY,
            ViewPositionX = visual.CenterX - 5,
            ViewPositionY = visual.CenterY - 5,
            ViewWidth = 10,
            ViewHeight = 10,
            Polygon = visual.Polygon
        };
        var roundTripped = DimensionDrawingTextBoxMapper.ToDrawingTextBox(
            visualAsSource, textIndex: 0, mapper, DimensionTextBoxShorteningMode.ToRaw);

        Assert.Equal(850, roundTripped.CenterX, 6);
        Assert.Equal(250, roundTripped.CenterY, 6);
        Assert.Equal(845, roundTripped.Polygon[0][0], 6);
        Assert.Equal(855, roundTripped.Polygon[2][0], 6);
    }

    private static DimensionPresentationTextBox BuildPresentationBox(double centerX, double centerY)
    {
        return new DimensionPresentationTextBox
        {
            SourceObjectId = 42,
            SourceObjectKind = "segment",
            Text = "100",
            CenterX = centerX,
            CenterY = centerY,
            ViewPositionX = centerX - 5,
            ViewPositionY = centerY - 5,
            ViewWidth = 10,
            ViewHeight = 10,
            Polygon = new List<double[]>
            {
                new[] { centerX - 5, centerY - 5 },
                new[] { centerX + 5, centerY - 5 },
                new[] { centerX + 5, centerY + 5 },
                new[] { centerX - 5, centerY + 5 }
            }
        };
    }
}
