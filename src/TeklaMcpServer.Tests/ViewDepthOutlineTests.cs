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

    private static (double MinY, double MaxY) Height(PartSolidGeometryInViewResult geometry, (double, double)? depth)
    {
        var outline = TeklaDrawingAssemblyOutlineApi.Build(7, [10], new Solids(geometry), depthRange: depth);
        var ys = outline.PartNodes[10].SelectMany(node => node.Polygon).Select(point => point[1]).ToArray();
        return (System.Math.Round(ys.Min(), 6), System.Math.Round(ys.Max(), 6));
    }

    [Fact]
    public void PartReachingBeyondTheDepthIsDrawnByItsFacesWithinTheDepth()
    {
        var bar = Bar(0, 9000);
        // Unlimited, the far end smears the outline 900 down.
        Assert.Equal((-995d, 95d), Height(bar, null));
        // Within the view depth only the near end face remains: the true section.
        Assert.Equal((-95d, 95d), Height(bar, (-60, 1)));
        // The stored solid is untouched: contacts and other readers still get all of it.
        Assert.Equal(6, bar.Solid.Faces.Count);
    }

    [Fact]
    public void PartWhollyWithinTheDepthIsUnchanged()
    {
        var bar = Bar(0, 15);
        Assert.Same(bar, TeklaDrawingAssemblyOutlineApi.WithinDepth(bar, (-60, 20)));
    }

    [Fact]
    public void MemberRunningThroughTheCutKeepsItsFullProjection()
    {
        // No face lies wholly within the depth, so there is nothing truer to draw than the projection.
        var bar = Bar(-500, 500);
        Assert.Same(bar, TeklaDrawingAssemblyOutlineApi.WithinDepth(bar, (-10, 10)));
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
