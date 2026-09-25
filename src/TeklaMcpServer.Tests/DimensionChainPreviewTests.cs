using System.Text.Json;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

/// <summary>A HEA-like beam (10) with a left end plate (11), a rib (12) and a right end plate (13 or 14).</summary>
public sealed class DimensionChainPreviewTests
{
    [Fact]
    public void BottomLocationChainRunsAlongTheMainPartAndClosesOnTheOuterFaces()
    {
        var chain = Chain(Context(rightHalfHeight: 68), "Bottom", "location");
        Assert.Equal(new[] { 20d, 200d, 800d, 10d }, Segments(chain));
        Assert.Equal(5, chain.GetProperty("pointIds").GetArrayLength());
    }

    [Fact]
    public void OverallChainHasTwoPointsAtTheExtremes()
    {
        var chain = Chain(Context(rightHalfHeight: 68), "Bottom", "overall");
        Assert.Equal(new[] { 1030d }, Segments(chain));
    }

    [Fact]
    public void LeftChainLocatesTheEndPlateAgainstTheMainPartEdges()
    {
        var chain = Chain(Context(rightHalfHeight: 68), "Left", "location");
        Assert.Equal(new[] { 94d, 152d, 94d }, Segments(chain));
    }

    [Fact]
    public void RightChainUsesTheInsetEndPlate()
    {
        var chain = Chain(Context(rightHalfHeight: 68), "Right", "location");
        Assert.Equal(new[] { 8d, 136d, 8d }, Segments(chain));
    }

    [Fact]
    public void FlushEndPlateGivesNoChainAndSaysWhy()
    {
        var chain = Chain(Context(rightHalfHeight: 76), "Right", "location");
        Assert.Equal(0, chain.GetProperty("pointIds").GetArrayLength());
        Assert.Contains("flush", chain.GetProperty("note").GetString());
    }

    [Fact]
    public void DefaultAnswerIsShortAndDetailsAreOptIn()
    {
        var context = Context(rightHalfHeight: 68);
        var shortChain = Chain(context, "Bottom", "location");
        Assert.False(shortChain.TryGetProperty("points", out _));
        Assert.False(shortChain.TryGetProperty("side", out _));
        Assert.False(shortChain.TryGetProperty("note", out _));
        Assert.False(shortChain.TryGetProperty("skippedPartIds", out _));
        Assert.DoesNotContain("999", shortChain.GetRawText());
        Assert.Equal("[20,200,800,10]", shortChain.GetProperty("segments").GetRawText());
        var detailed = context.Query("chainDetails", "Bottom").GetProperty("chainPreview").EnumerateArray().Single()
            .GetProperty("chains").EnumerateArray().Single(c => c.GetProperty("kind").GetString() == "location");
        Assert.True(detailed.TryGetProperty("points", out var points));
        Assert.Equal(5, points.GetArrayLength());
    }

    [Fact]
    public void PreviewIsOptInAndNeverPartOfAll()
    {
        var context = Context(rightHalfHeight: 68);
        Assert.False(context.Query("all").TryGetProperty("chainPreview", out _));
        Assert.True(context.Query("chain", "Bottom").TryGetProperty("chainPreview", out _));
    }

    [Fact]
    public void SeveralMainPartsStopThePreview()
    {
        var chain = Chain(Context(rightHalfHeight: 68, secondMain: true), "Bottom", "location");
        Assert.Equal(0, chain.GetProperty("pointIds").GetArrayLength());
        Assert.Contains("more than one main part", chain.GetProperty("note").GetString());
    }

    [Fact]
    public void UnclassifiedPartStopsThePreview()
    {
        var chain = Chain(Context(rightHalfHeight: 68, unclassified: true), "Bottom", "location");
        Assert.Equal(0, chain.GetProperty("pointIds").GetArrayLength());
        Assert.Contains("main part could not be resolved", chain.GetProperty("note").GetString());
    }

    [Theory]
    [InlineData("SectionView")]
    [InlineData("EndView")]
    public void SectionAndEndViewsAreRefused(string viewType)
    {
        var chain = Chain(Context(rightHalfHeight: 68, viewType: viewType), "Bottom", "location");
        Assert.Equal(0, chain.GetProperty("pointIds").GetArrayLength());
        Assert.Contains(viewType, chain.GetProperty("note").GetString());
    }

    [Fact]
    public void PointDroppedForBeingTooCloseIsReported()
    {
        var chain = Chain(Context(rightHalfHeight: 68, closeRib: true), "Bottom", "location");
        Assert.NotEmpty(chain.GetProperty("droppedShortPointIds").EnumerateArray());
        Assert.NotEmpty(chain.GetProperty("skippedPartIds").EnumerateArray());
    }

    private static double[] Segments(JsonElement chain) =>
        chain.GetProperty("segments").EnumerateArray().Select(x => x.GetDouble()).ToArray();

    private static JsonElement Chain(ViewDimensionContext context, string side, string kind) =>
        context.Query("chain", side).GetProperty("chainPreview").EnumerateArray().Single()
            .GetProperty("chains").EnumerateArray().Single(c => c.GetProperty("kind").GetString() == kind);

    private static ViewDimensionContext Context(double rightHalfHeight, string viewType = "BackView",
        bool secondMain = false, bool closeRib = false, bool unclassified = false)
    {
        var parts = new Dictionary<int, PartSolidGeometryInViewResult> {
            [10] = Solid(10, 0, 1000, -76, 76),
            [11] = Solid(11, -20, 0, -170, 170),
            [12] = Solid(12, 200, 210, -66, 66),
            [13] = Solid(13, 1000, 1010, -rightHalfHeight, rightHalfHeight)
        };
        if (closeRib) parts[15] = Solid(15, 201.5, 206, -66, 66);
        var ids = closeRib ? new[] { 10, 11, 12, 13, 15 } : new[] { 10, 11, 12, 13 };
        var outline = TeklaDrawingAssemblyOutlineApi.Build(7, ids, new Solids(parts));
        var role = new PartRoleResult(PartRole.Included, "included", "test");
        var structural = new StructuralOutline(outline,
            (new[] { new PartRoleInView(10, "P10", "P", role, true), new(11, "P11", "P", role, false),
             new(12, "P12", "P", role, secondMain), new(13, "P13", "P", role, false) })
                .Concat(closeRib ? [new PartRoleInView(15, "P15", "P", role, false)] : []).ToArray(),
            [], unclassified ? [new PartRoleInView(14, "P14", "P", role, false)] : [], [], [], ids);
        return new ViewDimensionContext(7, 10, structural, [], new { drawingGuid = "test" }, new { viewType });
    }

    private static PartSolidGeometryInViewResult Solid(int id, double x0, double x1, double y0, double y1)
    {
        var geometry = new PartSolidGeometryInViewResult { Success = true, ModelId = id, ViewId = 7 };
        var points = new[] { (x0, y0), (x1, y0), (x1, y1), (x0, y1) };
        for (var i = 0; i < points.Length; i++)
            geometry.Solid.Vertices.Add(new PartVertexGeometry { Index = i, Point = [points[i].Item1, points[i].Item2, 0] });
        var face = new PartFaceGeometry { Index = 0, Normal = [0, 0, 1] };
        face.Loops.Add(new PartLoopGeometry { Index = 0, VertexIndexes = [0, 1, 2, 3] });
        geometry.Solid.Faces.Add(face);
        return geometry;
    }

    private sealed class Solids(Dictionary<int, PartSolidGeometryInViewResult> parts) : IDrawingPartSolidGeometryApi
    {
        public PartSolidGeometryInViewResult GetPartSolidGeometryInView(int viewId, int modelId) => parts[modelId];
    }
}
