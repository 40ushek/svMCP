using System.Linq;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class MarksViewContextBuilderTests
{
    [Fact]
    public void CreateViewBounds_BuildsCenteredBounds()
    {
        var bounds = MarksViewContextBuilder.CreateViewBounds(120, 80);

        Assert.Equal(-60, bounds.MinX);
        Assert.Equal(-40, bounds.MinY);
        Assert.Equal(60, bounds.MaxX);
        Assert.Equal(40, bounds.MaxY);
    }

    [Fact]
    public void CreateGeometryContext_MapsResolvedGeometry()
    {
        var geometry = new MarkGeometryInfo
        {
            CenterX = 50,
            CenterY = 25,
            Width = 30,
            Height = 10,
            MinX = 35,
            MinY = 20,
            MaxX = 65,
            MaxY = 30,
            Source = "Resolved",
            IsReliable = true,
            Corners =
            [
                [35.0, 20.0],
                [35.0, 30.0],
                [65.0, 30.0],
                [65.0, 20.0],
            ],
        };

        var result = MarksViewContextBuilder.CreateGeometryContext(geometry);

        Assert.Equal(35, result.Bounds!.MinX);
        Assert.Equal(30, result.Bounds.MaxY);
        Assert.Equal(50, result.Center!.X);
        Assert.Equal(25, result.Center.Y);
        Assert.Equal("Resolved", result.Source);
        Assert.True(result.IsReliable);
        Assert.Equal(4, result.Corners.Count);
        Assert.Equal([0, 1, 2, 3], result.Corners.Select(static p => p.Order).ToArray());
    }

    [Fact]
    public void CreateAxisContextFromGeometry_UsesResolvedAxisAndWidth()
    {
        var geometry = new MarkGeometryInfo
        {
            CenterX = 100,
            CenterY = 50,
            Width = 40,
            AxisDx = 1,
            AxisDy = 0,
            HasAxis = true,
            IsReliable = true,
        };

        var axis = MarksViewContextBuilder.CreateAxisContextFromGeometry(geometry);

        Assert.NotNull(axis);
        Assert.Equal(80, axis!.Start!.X);
        Assert.Equal(50, axis.Start.Y);
        Assert.Equal(120, axis.End!.X);
        Assert.Equal(50, axis.End.Y);
        Assert.Equal(1, axis.Direction!.X);
        Assert.Equal(0, axis.Direction.Y);
        Assert.Equal(40, axis.Length);
        Assert.Equal(0, axis.AngleDeg);
        Assert.True(axis.IsReliable);
    }

    [Fact]
    public void CreateAxisContextFromLine_NormalizesDirectionAndLength()
    {
        var axis = MarksViewContextBuilder.CreateAxisContextFromLine(10, 20, 13, 24);

        Assert.Equal(10, axis.Start!.X);
        Assert.Equal(20, axis.Start.Y);
        Assert.Equal(13, axis.End!.X);
        Assert.Equal(24, axis.End.Y);
        Assert.Equal(5, axis.Length, 6);
        Assert.Equal(0.6, axis.Direction!.X, 6);
        Assert.Equal(0.8, axis.Direction.Y, 6);
        Assert.True(axis.IsReliable);
    }

    [Fact]
    public void CreateAnchor_UsesLeaderAnchor_WhenLeaderLine()
    {
        var geometry = new MarkGeometryContext
        {
            Center = new DrawingPointInfo { X = 50, Y = 25 },
        };

        var anchor = MarksViewContextBuilder.CreateAnchor(geometry, true, 10, 20);

        Assert.Equal(10, anchor.X);
        Assert.Equal(20, anchor.Y);
    }

    [Fact]
    public void CreateAnchor_UsesGeometryCenter_WhenNotLeaderLine()
    {
        var geometry = new MarkGeometryContext
        {
            Center = new DrawingPointInfo { X = 50, Y = 25 },
        };

        var anchor = MarksViewContextBuilder.CreateAnchor(geometry, false, 10, 20);

        Assert.Equal(50, anchor.X);
        Assert.Equal(25, anchor.Y);
    }
}
