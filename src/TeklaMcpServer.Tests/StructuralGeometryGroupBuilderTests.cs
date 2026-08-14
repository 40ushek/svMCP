using System.Collections.Generic;
using System.Linq;
using Clipper2Lib;
using SolidContacts;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class StructuralGeometryGroupBuilderTests
{
    [Fact]
    public void DisconnectedAssemblyComponentsKeepTheirCombinedExtent()
    {
        var structural = Structural(Square(7, 10, 0, 0, 10, 10), Square(7, 20, 100, 20, 110, 30));

        var group = StructuralGeometryGroupBuilder.Build(structural);
        CalcDimensionChains.Apply(group);

        Assert.Equal(2, group.BoundaryShapes.Count);
        Assert.Equal(0d, group.Extent!.MinX, precision: 6);
        Assert.Equal(110d, group.Extent.MaxX, precision: 6);
        Assert.Equal(0d, group.Extent.MinY, precision: 6);
        Assert.Equal(30d, group.Extent.MaxY, precision: 6);
        var extentCoordinates = group.DimensionChains![DimensionChainSide.Top].Positions
            .Where(position => position.Supports.Any(support => support.Kind == DimensionChainPositionSupportKind.GroupExtent))
            .Select(position => position.Coordinate).ToArray();
        Assert.Equal(0d, extentCoordinates[0], precision: 6);
        Assert.Equal(110d, extentCoordinates[1], precision: 6);
    }

    [Fact]
    public void NestedOutlineRingsAlternateOuterHoleAndIslandByDepth()
    {
        var part = Polygon(
            7,
            10,
            [(0d, 0d), (100d, 0d), (100d, 100d), (0d, 100d)],
            [(20d, 20d), (80d, 20d), (80d, 80d), (20d, 80d)],
            [(40d, 40d), (60d, 40d), (60d, 60d), (40d, 60d)]);

        var group = StructuralGeometryGroupBuilder.Build(Structural(part));

        Assert.Equal([false, true, false], group.Shapes
            .Where(shape => shape.Id.StartsWith("defining-part:10:ring:"))
            .Select(shape => shape.IsHole));
        Assert.All(group.Shapes.Where(shape => shape.Id.StartsWith("defining-part:10:ring:")),
            shape => Assert.Equal(10, shape.ModelId));
        Assert.All(group.BoundaryShapes, shape => Assert.Null(shape.ModelId));
    }

    [Fact]
    public void MalformedRingMakesTheSnapshotIncompleteInsteadOfSilentlyEmpty()
    {
        var nodes = new[]
        {
            new OutlineTreeNodeResult { Polygon = [null!, [1d]] }
        };
        var issues = new List<GeometryGroupSourceIssue>();

        var shapes = StructuralGeometryGroupBuilder.BuildRings("structural-boundary", nodes, issues);

        Assert.Equal(PlanarShapeKind.Empty, Assert.Single(shapes).Shape.Kind);
        var issue = Assert.Single(issues);
        Assert.Equal("structural-boundary:ring:0", issue.Id);
        Assert.Contains("without an X/Y coordinate", issue.Reason);
        Assert.Contains("no usable planar point", issue.Reason);
    }

    [Fact]
    public void StructuralGapsTravelAsNeutralGroupIssues()
    {
        var unread = new PartSolidGeometryInViewResult
        {
            Success = false,
            ViewId = 7,
            ModelId = 20,
            Error = "solid unavailable"
        };
        var structural = Structural(Square(7, 10, 0, 0, 10, 10), unread);

        var group = StructuralGeometryGroupBuilder.Build(structural);

        Assert.False(group.Completeness.IsComplete);
        var issue = Assert.Single(group.Completeness.Issues);
        Assert.Equal("outline:20", issue.Id);
        Assert.Equal("solid unavailable", issue.Reason);
    }

    private static StructuralOutline Structural(params PartSolidGeometryInViewResult[] parts)
    {
        var outline = TeklaDrawingAssemblyOutlineApi.Build(
            7,
            parts.Select(part => part.ModelId),
            new StubSolidGeometryApi(parts));
        var defining = parts.Select(part => new PartRoleInView(
            part.ModelId,
            "P" + part.ModelId,
            "T",
            new PartRoleResult(PartRole.Defining, "prefix-T", "test"))).ToList();
        return new StructuralOutline(outline, defining, [], []);
    }

    private static PartSolidGeometryInViewResult Square(
        int viewId, int modelId, double minX, double minY, double maxX, double maxY) =>
        Polygon(viewId, modelId, [(minX, minY), (maxX, minY), (maxX, maxY), (minX, maxY)]);

    private static PartSolidGeometryInViewResult Polygon(
        int viewId,
        int modelId,
        params (double X, double Y)[][] loops)
    {
        var geometry = new PartSolidGeometryInViewResult
        {
            Success = true,
            ViewId = viewId,
            ModelId = modelId
        };
        var face = new PartFaceGeometry { Index = 0, Normal = [0, 0, 1] };
        var index = 0;
        for (var loopIndex = 0; loopIndex < loops.Length; loopIndex++)
        {
            var indexes = new List<int>();
            foreach (var point in loops[loopIndex])
            {
                geometry.Solid.Vertices.Add(new PartVertexGeometry { Index = index, Point = [point.X, point.Y, 0] });
                indexes.Add(index++);
            }
            face.Loops.Add(new PartLoopGeometry { Index = loopIndex, VertexIndexes = indexes });
        }
        geometry.Solid.Faces.Add(face);
        return geometry;
    }

    private sealed class StubSolidGeometryApi : IDrawingPartSolidGeometryApi
    {
        private readonly Dictionary<int, PartSolidGeometryInViewResult> _parts;

        public StubSolidGeometryApi(IEnumerable<PartSolidGeometryInViewResult> parts) =>
            _parts = parts.ToDictionary(part => part.ModelId);

        public PartSolidGeometryInViewResult GetPartSolidGeometryInView(int viewId, int modelId) => _parts[modelId];
    }
}
