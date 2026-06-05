using System;
using System.Collections.Generic;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class ViewShorteningCoordinateMapperTests
{
    [Fact]
    public void ConvertPoint_SubtractsPriorGapsOnX()
    {
        var mapper = ViewShorteningCoordinateMapper.FromVisibleBoxes(new[]
        {
            new ViewShorteningVisibleBox(0, 0, 100, 500),
            new ViewShorteningVisibleBox(300, 0, 500, 500),
            new ViewShorteningVisibleBox(800, 0, 1000, 500)
        });

        var converted = mapper.ConvertPoint(850, 250);

        Assert.True(mapper.HasShorteningX);
        Assert.False(mapper.HasShorteningY);
        Assert.Equal(350, converted[0], 6);
        Assert.Equal(250, converted[1], 6);

        mapper.ConvertPoint(850, 250, out var convertedX, out var convertedY);
        Assert.Equal(350, convertedX, 6);
        Assert.Equal(250, convertedY, 6);
    }

    [Fact]
    public void ConvertPointToRaw_AddsPriorGapsOnX()
    {
        var mapper = ViewShorteningCoordinateMapper.FromVisibleBoxes(new[]
        {
            new ViewShorteningVisibleBox(0, 0, 100, 500),
            new ViewShorteningVisibleBox(300, 0, 500, 500),
            new ViewShorteningVisibleBox(800, 0, 1000, 500)
        });

        var raw = mapper.ConvertPointToRaw(350, 250);

        Assert.True(mapper.HasShorteningX);
        Assert.False(mapper.HasShorteningY);
        Assert.Equal(850, raw[0], 6);
        Assert.Equal(250, raw[1], 6);

        mapper.ConvertPointToRaw(350, 250, out var rawX, out var rawY);
        Assert.Equal(850, rawX, 6);
        Assert.Equal(250, rawY, 6);
    }

    [Fact]
    public void ConvertPointToRaw_ForSecondVisualInterval_AddsFirstGap()
    {
        var mapper = ViewShorteningCoordinateMapper.FromVisibleBoxes(new[]
        {
            new ViewShorteningVisibleBox(0, 0, 100, 500),
            new ViewShorteningVisibleBox(300, 0, 500, 500)
        });

        var raw = mapper.ConvertPointToRaw(150, 250);

        Assert.Equal(350, raw[0], 6);
        Assert.Equal(250, raw[1], 6);
    }

    [Fact]
    public void ConvertPointToVisual_WithSpaceBetweenCutParts_LeavesSpaceInVisualGap()
    {
        var mapper = ViewShorteningCoordinateMapper.FromVisibleBoxes(new[]
        {
            new ViewShorteningVisibleBox(0, 0, 100, 500),
            new ViewShorteningVisibleBox(300, 0, 500, 500)
        }, spaceBetweenCutParts: 1.0);

        var visual = mapper.ConvertPointToVisual(300, 250);

        Assert.Equal(101, visual[0], 6);
        Assert.Equal(250, visual[1], 6);
    }

    [Fact]
    public void ConvertPointToRaw_WithSpaceBetweenCutParts_AddsOnlyRemovedPartOfGap()
    {
        var mapper = ViewShorteningCoordinateMapper.FromVisibleBoxes(new[]
        {
            new ViewShorteningVisibleBox(0, 0, 100, 500),
            new ViewShorteningVisibleBox(300, 0, 500, 500)
        }, spaceBetweenCutParts: 1.0);

        var raw = mapper.ConvertPointToRaw(101, 250);

        Assert.Equal(300, raw[0], 6);
        Assert.Equal(250, raw[1], 6);
    }

    [Fact]
    public void ConvertPoint_SubtractsPriorGapsOnBothAxes()
    {
        var mapper = ViewShorteningCoordinateMapper.FromVisibleBoxes(new[]
        {
            new ViewShorteningVisibleBox(0, 0, 100, 100),
            new ViewShorteningVisibleBox(300, 0, 500, 100),
            new ViewShorteningVisibleBox(300, 400, 500, 700)
        });

        var converted = mapper.ConvertPoint(350, 450);

        Assert.True(mapper.HasShorteningX);
        Assert.True(mapper.HasShorteningY);
        Assert.Equal(150, converted[0], 6);
        Assert.Equal(150, converted[1], 6);
    }

    [Fact]
    public void ConvertPolygon_ConvertsEveryCorner()
    {
        var mapper = ViewShorteningCoordinateMapper.FromVisibleBoxes(new[]
        {
            new ViewShorteningVisibleBox(0, 0, 100, 500),
            new ViewShorteningVisibleBox(300, 0, 500, 500)
        });

        var converted = mapper.ConvertPolygon(new List<double[]>
        {
            new[] { 320.0, 10.0 },
            new[] { 360.0, 10.0 },
            new[] { 360.0, 30.0 },
            new[] { 320.0, 30.0 }
        });

        Assert.Equal(4, converted.Count);
        Assert.Equal(120, converted[0][0], 6);
        Assert.Equal(160, converted[1][0], 6);
        Assert.Equal(30, converted[2][1], 6);
    }

    [Fact]
    public void ConvertPolygonToRaw_ConvertsEveryCorner()
    {
        var mapper = ViewShorteningCoordinateMapper.FromVisibleBoxes(new[]
        {
            new ViewShorteningVisibleBox(0, 0, 100, 500),
            new ViewShorteningVisibleBox(300, 0, 500, 500)
        });

        var raw = mapper.ConvertPolygonToRaw(new List<double[]>
        {
            new[] { 120.0, 10.0 },
            new[] { 160.0, 10.0 },
            new[] { 160.0, 30.0 },
            new[] { 120.0, 30.0 }
        });

        Assert.Equal(4, raw.Count);
        Assert.Equal(320, raw[0][0], 6);
        Assert.Equal(360, raw[1][0], 6);
        Assert.Equal(30, raw[2][1], 6);
    }

    [Fact]
    public void ConvertPoint_WithSingleVisibleInterval_ReturnsOriginalCoordinates()
    {
        var mapper = ViewShorteningCoordinateMapper.FromVisibleBoxes(new[]
        {
            new ViewShorteningVisibleBox(0, 0, 500, 500)
        });

        var converted = mapper.ConvertPoint(350, 450);

        Assert.False(mapper.HasShortening);
        Assert.Equal(350, converted[0], 6);
        Assert.Equal(450, converted[1], 6);
    }

    [Fact]
    public void ConvertPoint_ForPointInsideGap_CollapsesIntoShortenedCoordinateSpace()
    {
        var mapper = ViewShorteningCoordinateMapper.FromVisibleBoxes(new[]
        {
            new ViewShorteningVisibleBox(0, 0, 100, 500),
            new ViewShorteningVisibleBox(300, 0, 500, 500)
        });

        var converted = mapper.ConvertPoint(200, 250);

        Assert.Equal(0, converted[0], 6);
        Assert.Equal(250, converted[1], 6);
    }

    [Fact]
    public void ConvertPolygon_ThrowsForInvalidPoint()
    {
        var mapper = ViewShorteningCoordinateMapper.FromVisibleBoxes(new[]
        {
            new ViewShorteningVisibleBox(0, 0, 500, 500)
        });

        Assert.Throws<ArgumentException>(() => mapper.ConvertPolygon(new List<double[]>
        {
            new[] { 10.0, 20.0 },
            new[] { 30.0 }
        }));
    }
}
