using System.Collections.Generic;
using TeklaMcpServer.Api.Algorithms.Marks;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class ForeignPartOverlapAnalyzerTests
{
    [Fact]
    public void ForeignPartOverlap_WhenMarkPolygonIntersectsForeignPart_ReturnsConflict()
    {
        var mark = CreateMark(
            id: 1,
            ownModelId: 10,
            x: 0.0,
            y: 0.0,
            localCorners: CreateRectangle(-5.0, -5.0, 5.0, 5.0));
        var foreignPart = new PartBbox(20, -2.0, -2.0, 8.0, 8.0, CreateRectangle(-2.0, -2.0, 8.0, 8.0));

        var result = ForeignPartOverlapAnalyzer.Analyze([mark], [foreignPart], threshold: 0.0);

        Assert.Equal(1, result.Conflicts);
        Assert.True(result.Severity > 0.0);
        Assert.Equal(1, result.Overlaps[0].MarkId);
        Assert.Equal(20, result.Overlaps[0].PartModelId);
        Assert.Equal(ForeignPartOverlapKind.PartialForeignPartOverlap, result.Overlaps[0].Kind);
        Assert.Equal(1, result.PartialConflicts);
        Assert.Equal(0, result.MarkInsideConflicts);
        Assert.Equal(0, result.PartInsideConflicts);
    }

    [Fact]
    public void ForeignPartOverlap_WhenMarkIsInsideForeignPart_ReturnsInsideKind()
    {
        var mark = CreateMark(
            id: 1,
            ownModelId: 10,
            x: 0.0,
            y: 0.0,
            localCorners: CreateRectangle(-5.0, -5.0, 5.0, 5.0));
        var foreignPart = new PartBbox(20, -20.0, -20.0, 20.0, 20.0, CreateRectangle(-20.0, -20.0, 20.0, 20.0));

        var result = ForeignPartOverlapAnalyzer.Analyze([mark], [foreignPart], threshold: 0.0);

        Assert.Equal(1, result.Conflicts);
        Assert.Equal(ForeignPartOverlapKind.MarkInsideForeignPart, result.Overlaps[0].Kind);
        Assert.Equal(1, result.MarkInsideConflicts);
        Assert.Equal(result.Severity, result.MarkInsideSeverity, 6);
        Assert.Equal(0, result.PartialConflicts);
        Assert.Equal(0, result.PartInsideConflicts);
    }

    [Fact]
    public void ForeignPartOverlap_WhenForeignPartIsInsideMark_ReturnsPartInsideKind()
    {
        var mark = CreateMark(
            id: 1,
            ownModelId: 10,
            x: 0.0,
            y: 0.0,
            localCorners: CreateRectangle(-20.0, -20.0, 20.0, 20.0));
        var foreignPart = new PartBbox(20, -5.0, -5.0, 5.0, 5.0, CreateRectangle(-5.0, -5.0, 5.0, 5.0));

        var result = ForeignPartOverlapAnalyzer.Analyze([mark], [foreignPart], threshold: 0.0);

        Assert.Equal(1, result.Conflicts);
        Assert.Equal(ForeignPartOverlapKind.ForeignPartInsideMark, result.Overlaps[0].Kind);
        Assert.Equal(1, result.PartInsideConflicts);
        Assert.Equal(result.Severity, result.PartInsideSeverity, 6);
        Assert.Equal(0, result.MarkInsideConflicts);
        Assert.Equal(0, result.PartialConflicts);
    }

    [Fact]
    public void ForeignPartOverlap_WhenPartIsOwnModel_IsExcluded()
    {
        var mark = CreateMark(
            id: 1,
            ownModelId: 10,
            x: 0.0,
            y: 0.0,
            localCorners: CreateRectangle(-5.0, -5.0, 5.0, 5.0));
        var ownPart = new PartBbox(10, -2.0, -2.0, 8.0, 8.0, CreateRectangle(-2.0, -2.0, 8.0, 8.0));

        var result = ForeignPartOverlapAnalyzer.Analyze([mark], [ownPart], threshold: 0.0);

        Assert.Equal(0, result.Conflicts);
        Assert.Equal(0.0, result.Severity, 6);
    }

    [Fact]
    public void ForeignPartOverlap_WhenSeparated_ReturnsNoConflict()
    {
        var mark = CreateMark(
            id: 1,
            ownModelId: 10,
            x: 0.0,
            y: 0.0,
            localCorners: CreateRectangle(-5.0, -5.0, 5.0, 5.0));
        var foreignPart = new PartBbox(20, 20.0, 20.0, 30.0, 30.0, CreateRectangle(20.0, 20.0, 30.0, 30.0));

        var result = ForeignPartOverlapAnalyzer.Analyze([mark], [foreignPart], threshold: 0.0);

        Assert.Equal(0, result.Conflicts);
        Assert.Equal(0.0, result.Severity, 6);
    }

    [Fact]
    public void ForeignPartOverlap_WhenDepthBelowThreshold_IsIgnored()
    {
        var mark = CreateMark(
            id: 1,
            ownModelId: 10,
            x: 0.0,
            y: 0.0,
            localCorners: CreateRectangle(-5.0, -5.0, 5.0, 5.0));
        var foreignPart = new PartBbox(20, 4.75, -5.0, 10.0, 5.0, CreateRectangle(4.75, -5.0, 10.0, 5.0));

        var result = ForeignPartOverlapAnalyzer.Analyze([mark], [foreignPart], threshold: 0.5);

        Assert.Equal(0, result.Conflicts);
        Assert.Equal(0.0, result.Severity, 6);
    }

    [Fact]
    public void ForeignPartOverlap_WhenPolygonsUnavailable_UsesBoundsFallback()
    {
        var mark = CreateMark(id: 1, ownModelId: 10, x: 0.0, y: 0.0);
        var foreignPart = new PartBbox(20, -2.0, -2.0, 8.0, 8.0);

        var result = ForeignPartOverlapAnalyzer.Analyze([mark], [foreignPart], threshold: 0.0);

        Assert.Equal(1, result.Conflicts);
        Assert.True(result.Severity > 0.0);
    }

    private static ForceDirectedMarkItem CreateMark(
        int id,
        int ownModelId,
        double x,
        double y,
        List<double[]>? localCorners = null) => new()
    {
        Id = id,
        OwnModelId = ownModelId,
        Cx = x,
        Cy = y,
        Width = 10.0,
        Height = 10.0,
        CanMove = true,
        LocalCorners = localCorners ?? new List<double[]>()
    };

    private static List<double[]> CreateRectangle(double minX, double minY, double maxX, double maxY) =>
        new()
        {
            new[] { minX, minY },
            new[] { maxX, minY },
            new[] { maxX, maxY },
            new[] { minX, maxY }
        };
}
