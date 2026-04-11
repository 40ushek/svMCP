using System.Collections.Generic;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class MarkSourceResolverTests
{
    [Fact]
    public void TryResolvePartCenter_UsesBoundingBox_WhenAvailable()
    {
        var parts = new List<PartGeometryInViewResult>
        {
            new()
            {
                Success = true,
                ModelId = 42,
                BboxMin = [10.0, 20.0],
                BboxMax = [30.0, 60.0]
            }
        };

        var resolved = MarkSourceResolver.TryResolvePartCenter(parts, 42, out var centerX, out var centerY);

        Assert.True(resolved);
        Assert.Equal(20.0, centerX, 3);
        Assert.Equal(40.0, centerY, 3);
    }

    [Fact]
    public void TryResolvePartCenter_FallsBackToSolidVertices()
    {
        var parts = new List<PartGeometryInViewResult>
        {
            new()
            {
                Success = true,
                ModelId = 42,
                SolidVertices =
                {
                    new[] { 10.0, 20.0 },
                    new[] { 30.0, 50.0 },
                    new[] { 20.0, 60.0 }
                }
            }
        };

        var resolved = MarkSourceResolver.TryResolvePartCenter(parts, 42, out var centerX, out var centerY);

        Assert.True(resolved);
        Assert.Equal(20.0, centerX, 3);
        Assert.Equal(40.0, centerY, 3);
    }

    [Fact]
    public void TryResolveBoltCenter_UsesBoundingBox_WhenAvailable()
    {
        var bolts = new List<BoltGroupGeometry>
        {
            new()
            {
                ModelId = 77,
                BboxMin = [100.0, 200.0],
                BboxMax = [140.0, 260.0]
            }
        };

        var resolved = MarkSourceResolver.TryResolveBoltCenter(bolts, 77, out var centerX, out var centerY);

        Assert.True(resolved);
        Assert.Equal(120.0, centerX, 3);
        Assert.Equal(230.0, centerY, 3);
    }

    [Fact]
    public void TryResolveBoltCenter_FallsBackToAveragePosition()
    {
        var bolts = new List<BoltGroupGeometry>
        {
            new()
            {
                ModelId = 77,
                Positions =
                {
                    new BoltPointGeometry { Point = [0.0, 0.0] },
                    new BoltPointGeometry { Point = [10.0, 20.0] },
                    new BoltPointGeometry { Point = [20.0, 40.0] }
                }
            }
        };

        var resolved = MarkSourceResolver.TryResolveBoltCenter(bolts, 77, out var centerX, out var centerY);

        Assert.True(resolved);
        Assert.Equal(10.0, centerX, 3);
        Assert.Equal(20.0, centerY, 3);
    }

    [Fact]
    public void TryResolveCenter_UsesPartLookup_ForUnknownSourceKind()
    {
        var viewContext = new DrawingViewContext();
        viewContext.Parts.Add(new PartGeometryInViewResult
        {
            Success = true,
            ModelId = 7,
            BboxMin = [0.0, 10.0],
            BboxMax = [20.0, 30.0]
        });

        var resolved = MarkSourceResolver.TryResolveCenter(
            new MarkSourceReference(MarkLayoutSourceKind.Unknown, 7),
            viewContext,
            out var centerX,
            out var centerY);

        Assert.True(resolved);
        Assert.Equal(10.0, centerX, 3);
        Assert.Equal(20.0, centerY, 3);
    }

    [Fact]
    public void TryResolvePartPolygon_UsesSolidVerticesHull_WhenAvailable()
    {
        var parts = new List<PartGeometryInViewResult>
        {
            new()
            {
                Success = true,
                ModelId = 42,
                SolidVertices =
                {
                    new[] { 0.0, 0.0 },
                    new[] { 10.0, 0.0 },
                    new[] { 10.0, 10.0 },
                    new[] { 0.0, 10.0 },
                    new[] { 5.0, 5.0 }
                }
            }
        };

        var resolved = MarkSourceResolver.TryResolvePartPolygon(parts, 42, out var polygon);

        Assert.True(resolved);
        Assert.Equal(4, polygon.Count);
    }

    [Fact]
    public void TryResolvePartPolygon_FallsBackToBoundingBox()
    {
        var parts = new List<PartGeometryInViewResult>
        {
            new()
            {
                Success = true,
                ModelId = 42,
                BboxMin = [10.0, 20.0],
                BboxMax = [30.0, 60.0]
            }
        };

        var resolved = MarkSourceResolver.TryResolvePartPolygon(parts, 42, out var polygon);

        Assert.True(resolved);
        Assert.Equal(4, polygon.Count);
    }
}
