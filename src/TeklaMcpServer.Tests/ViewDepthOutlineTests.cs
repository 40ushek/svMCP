using System.Collections.Generic;
using System.Linq;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

/// <summary>
/// A view shows of a part only what lies within its depth. Found on M.53: a section cut square
/// to a tilted end drew the whole 9 m girder, smeared 1131 high, and offered 948 as a dimension.
/// </summary>
public sealed class ViewDepthOutlineTests
{
    // A bar 200 x 190 in section whose axis runs away from the viewer and sinks 100 per 1000 of depth.
    private static PartSolidGeometryInViewResult Bar(double nearDepth, double farDepth)
    {
        var geometry = new PartSolidGeometryInViewResult { Success = true, ModelId = 10, ViewId = 7 };
        var index = 0;
        foreach (var depth in new[] { nearDepth, farDepth })
        {
            var sink = -0.1 * depth;
            foreach (var (x, y) in new[] { (-100d, -95d), (100d, -95d), (100d, 95d), (-100d, 95d) })
                geometry.Solid.Vertices.Add(new PartVertexGeometry { Index = index++, Point = [x, y + sink, depth] });
        }
        void Face(params int[] loop)
        {
            var face = new PartFaceGeometry { Index = geometry.Solid.Faces.Count };
            face.Loops.Add(new PartLoopGeometry { Index = 0, VertexIndexes = loop.ToList() });
            geometry.Solid.Faces.Add(face);
        }
        Face(0, 1, 2, 3);
        Face(4, 5, 6, 7);
        Face(0, 1, 5, 4);
        Face(1, 2, 6, 5);
        Face(2, 3, 7, 6);
        Face(3, 0, 4, 7);
        return geometry;
    }

    private static (double MinY, double MaxY) Height(PartSolidGeometryInViewResult geometry,
        (double, double, double?)? depth)
    {
        var outline = TeklaDrawingAssemblyOutlineApi.Build(7, [10], new Solids(geometry), depthRange: depth);
        var ys = outline.PartNodes[10].SelectMany(node => node.Polygon).Select(point => point[1]).ToArray();
        return (System.Math.Round(ys.Min(), 6), System.Math.Round(ys.Max(), 6));
    }

    [Fact]
    public void PartCutByTheViewPlaneIsDrawnByTheCut()
    {
        var bar = Bar(0, 9000);
        // Unlimited, the far end smears the outline 900 down.
        Assert.Equal((-995d, 95d), Height(bar, null));
        // The view plane cuts it at its near end: the true section.
        Assert.Equal((-95d, 95d), Height(bar, (-60, 1, 0)));
        // With no known view plane it is drawn by its faces within the depth.
        Assert.Equal((-95d, 95d), Height(bar, (-60, 1, null)));
        // The stored solid is untouched: contacts and other readers still get all of it.
        Assert.Equal(6, bar.Solid.Faces.Count);
    }

    [Fact]
    public void PartWhollyWithinTheDepthIsUnchanged()
    {
        var bar = Bar(0, 15);
        Assert.Same(bar, TeklaDrawingAssemblyOutlineApi.WithinDepth(bar, (-60, 20, 0)));
    }

    [Fact]
    public void MemberRunningThroughTheCutIsDrawnByTheCut()
    {
        var bar = Bar(-500, 500);
        Assert.Equal((-95d, 95d), Height(bar, (-10, 10, 0)));
        // 100 further along it has sunk by 10.
        Assert.Equal((-105d, 85d), Height(bar, (90, 110, 100)));
    }

    [Fact]
    public void MemberLyingAlongTheViewPlaneKeepsItsFullProjection()
    {
        // The plane y = 0 of the bar, seen from above: a cut 1000 long through a bar 190 deep.
        var bar = Bar(-500, 500);
        foreach (var vertex in bar.Solid.Vertices)
            vertex.Point = [vertex.Point[0], vertex.Point[2], vertex.Point[1]];
        Assert.Same(bar, TeklaDrawingAssemblyOutlineApi.WithinDepth(bar, (-10, 10, 0)));
    }

    [Fact]
    public void NotchFaceWithinTheDepthIsDrawnWithTheCutOfThePart()
    {
        // The near end face is a tenth as wide as the bar is where the view plane cuts it.
        var bar = Bar(0, 9000);
        foreach (var index in new[] { 0, 1, 2, 3 })
            bar.Solid.Vertices[index].Point[0] *= 0.1;
        var shown = TeklaDrawingAssemblyOutlineApi.WithinDepth(bar, (-60, 4600, 4500));
        Assert.Equal(2, shown.Solid.Faces.Count);
        var cut = shown.Solid.Faces[1].Loops[0].VertexIndexes
            .Select(index => shown.Solid.Vertices.Single(vertex => vertex.Index == index).Point).ToArray();
        Assert.All(cut, point => Assert.Equal(4500, point[2]));
        Assert.Equal(110, cut.Max(point => point[0]) - cut.Min(point => point[0]), 6);
    }

    [Fact]
    public void CutRepeatingTheEndFaceLeavesTheFaceAlone()
    {
        // The view plane lies 1 inside the bar: the cut is the end face sunk by 0.1.
        var bar = Bar(0, 9000);
        var shown = TeklaDrawingAssemblyOutlineApi.WithinDepth(bar, (-60, 2, 1));
        Assert.Equal(1, shown.Solid.Faces.Count);
        Assert.Equal((-95d, 95d), Height(bar, (-60, 2, 1)));
    }

    [Fact]
    public void CutReadFromTeklaIsUsedInsteadOfTheOneWorkedOut()
    {
        var bar = Bar(-500, 500);
        bar.Solid.ViewPlaneCut = [[[[-40, -30, 0], [40, -30, 0], [40, 30, 0], [-40, 30, 0]]]];
        Assert.Equal((-30d, 30d), Height(bar, (-10, 10, 0)));
        // Tekla found nothing in the plane: no cut, the full projection stays.
        bar.Solid.ViewPlaneCut = [];
        Assert.Same(bar, TeklaDrawingAssemblyOutlineApi.WithinDepth(bar, (-10, 10, 0)));
    }

    [Fact]
    public void MemberRunningThroughADepthWithNoKnownCutKeepsItsFullProjection()
    {
        // No face lies wholly within the depth, so there is nothing truer to draw than the projection.
        var bar = Bar(-500, 500);
        Assert.Same(bar, TeklaDrawingAssemblyOutlineApi.WithinDepth(bar, (-10, 10, null)));
    }

    [Fact]
    public void HollowMemberKeepsItsHoleInTheCut()
    {
        var tube = Bar(-500, 500);
        var index = tube.Solid.Vertices.Count;
        foreach (var depth in new[] { -500d, 500d })
            foreach (var (x, y) in new[] { (-50d, -50d), (50d, -50d), (50d, 50d), (-50d, 50d) })
                tube.Solid.Vertices.Add(new PartVertexGeometry { Index = index++, Point = [x, y - 0.1 * depth, depth] });
        tube.Solid.Faces[0].Loops.Add(new PartLoopGeometry { Index = 1, VertexIndexes = [8, 9, 10, 11] });
        tube.Solid.Faces[1].Loops.Add(new PartLoopGeometry { Index = 1, VertexIndexes = [12, 13, 14, 15] });
        foreach (var loop in new[] { new[] { 8, 9, 13, 12 }, new[] { 9, 10, 14, 13 }, new[] { 10, 11, 15, 14 }, new[] { 11, 8, 12, 15 } })
        {
            var face = new PartFaceGeometry { Index = tube.Solid.Faces.Count };
            face.Loops.Add(new PartLoopGeometry { Index = 0, VertexIndexes = loop.ToList() });
            tube.Solid.Faces.Add(face);
        }
        var cut = TeklaDrawingAssemblyOutlineApi.WithinDepth(tube, (-10, 10, 0));
        Assert.Equal(2, Assert.Single(cut.Solid.Faces).Loops.Count);
    }

    [Fact]
    public void WithoutAKnownDepthNothingChanges()
    {
        var bar = Bar(0, 9000);
        Assert.Same(bar, TeklaDrawingAssemblyOutlineApi.WithinDepth(bar, null));
    }

    private sealed class Solids(PartSolidGeometryInViewResult geometry) : IDrawingPartSolidGeometryApi
    {
        public PartSolidGeometryInViewResult GetPartSolidGeometryInView(int viewId, int modelId) => geometry;
    }
}
