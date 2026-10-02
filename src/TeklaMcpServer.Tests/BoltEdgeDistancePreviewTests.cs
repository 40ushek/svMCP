using System.Text.Json;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class BoltEdgeDistancePreviewTests
{
    [Theory]
    [InlineData(0, -1, 20)]
    [InlineData(0, 1, 80)]
    [InlineData(1, -1, 30)]
    [InlineData(1, 1, 70)]
    public void MeasuresFromCenterAlongAxisToActualPartContour(int axis, int sign, double expected)
    {
        var result = Edge([Rectangle()], [20, 30, 17], axis, sign);
        Assert.Equal("Candidate", result.GetProperty("state").GetString());
        Assert.Equal(expected, result.GetProperty("distance").GetDouble());
        Assert.Equal(17, result.GetProperty("edgePoint")[2].GetDouble());
        Assert.Equal(10, result.GetProperty("partId").GetInt32());
        Assert.False(result.GetProperty("writeReady").GetBoolean());
        var points = result.GetProperty("points");
        Assert.True(points[0][axis].GetDouble() < points[1][axis].GetDouble());
    }

    [Fact]
    public void ConcaveNotchUsesLocalEdgeRatherThanPartExtent()
    {
        var contour = Polygon([0, 0], [100, 0], [100, 40], [40, 40], [40, 100], [0, 100]);
        var result = Edge([contour], [20, 60, 0], 0, 1);
        Assert.Equal(20, result.GetProperty("distance").GetDouble());
        Assert.Equal(40, result.GetProperty("edgePoint")[0].GetDouble());
    }

    [Fact]
    public void ChamferUsesRayIntersectionInsteadOfBoundingBox()
    {
        var contour = Polygon([0, 0], [100, 0], [100, 60], [60, 100], [0, 100]);
        var result = Edge([contour], [20, 80, 0], 0, 1);
        Assert.Equal(60, result.GetProperty("distance").GetDouble());
        Assert.Equal(80, result.GetProperty("edgePoint")[0].GetDouble());
    }

    [Fact]
    public void BoltHoleCircumferenceIsNotThePartEdge()
    {
        var contour = Rectangle();
        var hole = Polygon([15, 25], [25, 25], [25, 35], [15, 35]);
        hole.IsHole = true;
        contour.Children.Add(hole);
        var result = Edge([contour], [20, 30, 0], 0, -1);
        Assert.Equal(20, result.GetProperty("distance").GetDouble());
    }

    [Theory]
    [InlineData("missing", "no captured")]
    [InlineData("outside", "outside")]
    [InlineData("ambiguous", "ambiguous")]
    [InlineData("vertex", "vertex")]
    [InlineData("restriction", "restriction")]
    public void UnresolvedGeometryReturnsReasonWithoutInventingAnEdge(string mode, string reason)
    {
        OutlineTreeNodeResult[]? contours = mode == "missing" ? null : mode == "ambiguous"
            ? [Rectangle(), Rectangle()] : [Rectangle()];
        var point = mode == "outside" ? new double[] { 150, 30, 0 }
            : mode == "vertex" ? [20, 0, 0] : [20, 30, 0];
        var result = Edge(contours, point, 0, 1, mode != "restriction");
        Assert.Equal("Blocked", result.GetProperty("state").GetString());
        Assert.Contains(reason, result.GetProperty("reason").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("distance").ValueKind);
        Assert.Empty(result.GetProperty("points").EnumerateArray());
    }

    [Fact]
    public void ContextUsesExtremesForEachRowAndColumnIncludingSingleCenterRows()
    {
        var group = new BoltGroupGeometry {
            ModelId = 42, Shape = "BoltArray", PartToBeBoltedId = 10,
            FirstPosition = [10, 20, 0], SecondPosition = [70, 20, 0],
            Positions = [new() { Index = 5, Point = [10, 20, 0] }, new() { Index = 9, Point = [70, 20, 0] }]
        };
        var snapshot = Capture(group);
        var context = ViewDimensionContextTests.Context(bolts: snapshot);
        var answer = context.Query("boltChains").GetProperty("boltChainPreview").GetProperty("groups")[0];
        var edges = answer.GetProperty("edgeChains");
        Assert.Equal(6, edges.GetArrayLength());
        Assert.Equal(new[] { 10d, 190d }, edges.EnumerateArray().Where(edge => edge.GetProperty("axis").GetString() == "X")
            .Select(edge => edge.GetProperty("distance").GetDouble()));
        Assert.Equal(new[] { 5 }, edges[0].GetProperty("sourceIndices").EnumerateArray().Select(v => v.GetInt32()));
        Assert.Equal(new[] { 9 }, edges[1].GetProperty("sourceIndices").EnumerateArray().Select(v => v.GetInt32()));
        Assert.All(edges.EnumerateArray(), edge => Assert.Equal(10, edge.GetProperty("partId").GetInt32()));
    }

    [Fact]
    public void SharedGroupProducesSeparatePartSpecificProposals()
    {
        var group = new BoltGroupGeometry {
            ModelId = 42, Shape = "BoltArray", PartToBeBoltedId = 10, PartToBoltToId = 20,
            FirstPosition = [20, 30, 0], SecondPosition = [60, 30, 0],
            Positions = [new() { Index = 1, Point = [20, 30, 0] }]
        };
        var contours = new Dictionary<int, IReadOnlyList<OutlineTreeNodeResult>> {
            [10] = [Rectangle()], [20] = [Polygon([0, 0], [200, 0], [200, 200], [0, 200])]
        };
        var result = JsonSerializer.SerializeToElement(BoltDimensionChainPreview.Build(Capture(group).Answer,
            [10, 20], [DimensionChainSide.Top], contours));
        var edges = result.GetProperty("groups")[0].GetProperty("edgeChains");
        Assert.Equal(4, edges.GetArrayLength());
        Assert.Equal(80, edges[1].GetProperty("distance").GetDouble());
        Assert.Equal(180, edges[3].GetProperty("distance").GetDouble());
        Assert.Equal(20, edges[3].GetProperty("partId").GetInt32());
    }

    [Fact]
    public void ContextFreezesContoursInsteadOfKeepingMutableSourceNodes()
    {
        var geometry = new PartSolidGeometryInViewResult { Success = true, ViewId = 7, ModelId = 10 };
        var corners = new[] { new double[] { 0, 0, 0 }, [100, 0, 0], [100, 100, 0], [0, 100, 0] };
        geometry.Solid.Vertices = corners.Select((p, index) => new PartVertexGeometry { Index = index, Point = p }).ToList();
        geometry.Solid.Faces = [new() { Index = 0, Normal = [0, 0, 1],
            Loops = [new() { Index = 0, VertexIndexes = [0, 1, 2, 3] }] }];
        var outline = TeklaDrawingAssemblyOutlineApi.Build(7, [10], new SolidReader(geometry));
        var structural = new StructuralOutline(outline,
            [new(10, "P10", "P", new PartRoleResult(PartRole.Included, "included", "test"), true)], [], []);
        var group = new BoltGroupGeometry { ModelId = 42, Shape = "BoltArray", PartToBeBoltedId = 10,
            FirstPosition = [20, 30, 0], SecondPosition = [60, 30, 0],
            Positions = [new() { Index = 1, Point = [20, 30, 0] }] };
        var context = new ViewDimensionContext(7, 10, structural, [], new { }, new { }, Capture(group));
        var first = context.Query("boltChains");
        outline.PartNodes[10][0].Polygon.Clear();
        Assert.Equal(first.GetRawText(), context.Query("boltChains").GetRawText());
        Assert.Equal(20, first.GetProperty("boltChainPreview").GetProperty("groups")[0]
            .GetProperty("edgeChains")[0].GetProperty("distance").GetDouble());
    }

    private sealed class SolidReader(PartSolidGeometryInViewResult geometry) : IDrawingPartSolidGeometryApi
    {
        public PartSolidGeometryInViewResult GetPartSolidGeometryInView(int viewId, int modelId) => geometry;
    }

    private static ViewBoltGeometrySnapshot Capture(BoltGroupGeometry group) =>
        ViewBoltGeometrySnapshot.Capture(7, [10], id => new() {
            Success = true, ViewId = 7, PartId = id, BoltGroups = [group]
        }, new DepthBox(-1000, -1000, -1000, 1000, 1000, 1000), point => point, true);

    private static JsonElement Edge(OutlineTreeNodeResult[]? contours, double[] center, int axis, int sign, bool inside = true) =>
        JsonSerializer.SerializeToElement(BoltEdgeDistancePreview.Build(42, 10, "row", [new() { Index = 5, Point = center }],
            axis, sign, contours, inside));

    private static OutlineTreeNodeResult Rectangle() => Polygon([0, 0], [100, 0], [100, 100], [0, 100]);
    private static OutlineTreeNodeResult Polygon(params double[][] points) => new() { Polygon = points.ToList() };
}