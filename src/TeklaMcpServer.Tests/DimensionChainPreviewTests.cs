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
    public void ShortHeaderKeepsOnlyWhatIsNotEmptyAndDiagnosticsReturnsTheRest()
    {
        var context = Context(rightHalfHeight: 68);
        var shortAnswer = context.Query("scale");
        Assert.False(shortAnswer.TryGetProperty("success", out _));
        Assert.False(shortAnswer.TryGetProperty("projectionVerification", out _));
        Assert.False(shortAnswer.TryGetProperty("issues", out _));
        Assert.False(shortAnswer.TryGetProperty("excludedModelIds", out _));
        Assert.Equal(10, shortAnswer.GetProperty("mainPart").GetInt32());
        Assert.True(shortAnswer.GetProperty("isComplete").GetBoolean());

        var full = context.Query("diagnostics");
        Assert.True(full.GetProperty("success").GetBoolean());
        Assert.True(full.TryGetProperty("projectionVerification", out _));
        Assert.True(full.TryGetProperty("issues", out _));
        Assert.True(full.TryGetProperty("mainPartUnresolvedModelIds", out _));
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
        var context = Context(rightHalfHeight: 68, unclassified: true);
        var unresolved = context.Query("diagnostics").GetProperty("mainPartUnresolvedModelIds");
        Assert.Equal(14, Assert.Single(unresolved.EnumerateArray()).GetInt32());
        var chain = Chain(context, "Bottom", "location");
        Assert.Equal(0, chain.GetProperty("pointIds").GetArrayLength());
        Assert.Contains("main part could not be resolved", chain.GetProperty("note").GetString());
    }

    [Fact]
    public void UnreadSecondaryPartStopsThePreviewEvenWhenTheMainPartIsKnown()
    {
        var context = Context(rightHalfHeight: 68, unread: true);
        var response = context.Query("chain", "Bottom");
        Assert.False(response.GetProperty("isComplete").GetBoolean());
        var chain = response.GetProperty("chainPreview").EnumerateArray().Single()
            .GetProperty("chains").EnumerateArray().Single(c => c.GetProperty("kind").GetString() == "location");
        Assert.Empty(chain.GetProperty("pointIds").EnumerateArray());
        Assert.Contains("incomplete", chain.GetProperty("note").GetString());
    }

    [Theory]
    [InlineData("SectionView")]
    [InlineData("EndView")]
    public void SectionAndEndViewsReturnProvisionalChains(string viewType)
    {
        var context = Context(rightHalfHeight: 68, viewType: viewType);
        var response = context.Query("chainDetails", "Bottom");
        Assert.Contains("provisional", response.GetProperty("sectionPreviewStatus").GetString());
        Assert.Equal(2, DetailedChain(context, "Bottom", "profile").GetProperty("pointIds").GetArrayLength());
    }

    [Fact]
    public void IProfileUsesActualFlangeAndWebEdges()
    {
        var context = ISectionContext();
        Assert.Equal(new[] { 70d, 20d, 70d }, Segments(DetailedChain(context, "Top", "profile")));
        Assert.Equal(new[] { 10d, 180d, 10d }, Segments(DetailedChain(context, "Left", "profile")));
        Assert.Empty(Unlocated(context, "X"));
        Assert.Empty(Unlocated(context, "Y"));
    }

    [Fact]
    public void RoundedIProfileKeepsTheStraightFlangeAndWebLevels()
    {
        var context = ISectionContext(roundedMain: true);
        Assert.Equal(new[] { 70d, 20d, 70d }, Segments(DetailedChain(context, "Top", "profile")));
        Assert.Equal(new[] { 10d, 180d, 10d }, Segments(DetailedChain(context, "Left", "profile")));
    }

    [Fact]
    public void EndPlateIsLocatedByItsTwoOuterFacesAndTheMainProfileEdges()
    {
        var context = ISectionContext(endPlate: true, viewType: "EndView");
        Assert.Equal(new[] { 50d, 160d, 50d }, Segments(DetailedChain(context, "Bottom", "location")));
        Assert.Equal(new[] { 70d, 200d, 70d }, Segments(DetailedChain(context, "Left", "location")));
        Assert.Empty(Unlocated(context, "X"));
        Assert.Empty(Unlocated(context, "Y"));
    }

    [Fact]
    public void SectionPreviewIdsResolveToTheReportedDimensionCoordinates()
    {
        var context = ISectionContext(endPlate: true, viewType: "EndView");
        var ids = Chain(context, "Bottom", "location").GetProperty("pointIds")
            .EnumerateArray().Select(p => p.GetString()!).ToArray();
        var coordinates = context.ResolvePointIds(ids, "horizontal-down");
        Assert.Equal(new[] { -130d, -80d, 80d, 130d },
            Enumerable.Range(0, ids.Length).Select(i => coordinates[i * 3]).ToArray());
    }

    [Fact]
    public void UnsupportedSectionProfileRefusesInsteadOfGuessing()
    {
        var context = ISectionContext(rakedMain: true);
        var chain = Chain(context, "Top", "profile");
        Assert.Empty(chain.GetProperty("pointIds").EnumerateArray());
        Assert.Contains("profile", chain.GetProperty("note").GetString());
    }

    [Fact]
    public void SectionPreviewRefusesAnInvalidScale()
    {
        var context = ISectionContext(scale: 0);
        var response = context.Query("chain", "Top");
        Assert.Equal("refused", response.GetProperty("sectionPreviewStatus").GetString());
        Assert.Contains("scale", Chain(context, "Top", "profile").GetProperty("note").GetString());
    }

    [Fact]
    public void NearbySectionPartsMergeAtHalfAPaperMillimeterAndAreReported()
    {
        var context = ISectionContext(angleShift: 0.14);
        var top = DetailedChain(context, "Top", "location");
        Assert.Equal(13, Assert.Single(top.GetProperty("mergedNearbyPartIds").EnumerateArray()).GetInt32());
        Assert.DoesNotContain(top.GetProperty("segments").EnumerateArray(),
            segment => Math.Abs(segment.GetDouble() - 0.14) < 0.001);
        Assert.Empty(Unlocated(context, "X"));
    }

    [Fact]
    public void SectionPartsBeyondHalfAPaperMillimeterStaySeparate()
    {
        var context = ISectionContext(angleShift: 6);
        var top = DetailedChain(context, "Top", "location");
        Assert.Empty(top.GetProperty("mergedNearbyPartIds").EnumerateArray());
        Assert.Contains(top.GetProperty("segments").EnumerateArray(),
            segment => Math.Abs(segment.GetDouble() - 6) < 0.001);
    }

    [Fact]
    public void PointDroppedForBeingTooCloseIsReported()
    {
        var chain = Chain(Context(rightHalfHeight: 68, closeRib: true), "Bottom", "location");
        Assert.NotEmpty(chain.GetProperty("droppedShortPointIds").EnumerateArray());
        Assert.NotEmpty(chain.GetProperty("skippedPartIds").EnumerateArray());
    }

    [Fact]
    public void CreatingFromIdsMovesTheWitnessPointToTheOutermostOnTheSameCoordinate()
    {
        var context = Context(rightHalfHeight: 68);
        var ids = context.Query("dimensionPoints", "Bottom").GetProperty("dimensionPoints").GetProperty("sides").EnumerateArray().Single()
            .GetProperty("points").EnumerateArray().Select(p => p.GetProperty("pointId").GetString()!).ToArray();
        var xy = context.ResolvePointIds(ids, "horizontal-down");
        var atX0 = Enumerable.Range(0, ids.Length).Where(i => Math.Abs(xy[i * 3]) < 1e-9).Select(i => xy[i * 3 + 1]).ToArray();
        Assert.NotEmpty(atX0);
        Assert.All(atX0, y => Assert.Equal(-170d, y));
    }

    [Fact]
    public void PartAboveTheMainAxisIsLocatedOnTheTopChainOnly()
    {
        var context = Context(rightHalfHeight: 68, upperPlate: true);
        Assert.Contains(500d, Cumulative(Chain(context, "Top", "location")));
        Assert.DoesNotContain(500d, Cumulative(Chain(context, "Bottom", "location")));
    }

    [Fact]
    public void PartCrossingTheMainAxisIsLocatedOnBothSides()
    {
        var context = Context(rightHalfHeight: 68, crossingPlate: true);
        Assert.Contains(500d, Cumulative(Chain(context, "Top", "location")));
        Assert.Contains(500d, Cumulative(Chain(context, "Bottom", "location")));
    }

    [Fact]
    public void PartIsNotLostWhenOnlyTheOtherSideOffersItsPoints()
    {
        // The tall end plate moves the assembly's middle up, so the plate above the main axis
        // is offered to the Bottom chain only; it must stay there instead of vanishing.
        var context = Context(rightHalfHeight: 68, upperPlate: true, tallEnd: true);
        var onTop = Cumulative(Chain(context, "Top", "location")).Contains(500d);
        var onBottom = Cumulative(Chain(context, "Bottom", "location")).Contains(500d);
        Assert.True(onTop || onBottom);
    }

    [Fact]
    public void PartOnTheAxisIsLocatedOnBothSides()
    {
        var context = Context(rightHalfHeight: 68, edgeOnPlate: true);
        Assert.Contains(500d, Cumulative(Chain(context, "Top", "location")));
        Assert.Contains(500d, Cumulative(Chain(context, "Bottom", "location")));
    }

    [Fact]
    public void CrossingPartIsStillLocatedWhenTheAssemblyMiddleIsPushedAway()
    {
        var context = Context(rightHalfHeight: 68, crossingPlate: true, tallEnd: true);
        var located = new[] { "Top", "Bottom" }.Any(side => Cumulative(Chain(context, side, "location")).Contains(500d));
        Assert.True(located);
    }

    [Fact]
    public void PartOfferedToTheOtherSideIsReportedNotSilentlyDropped()
    {
        // The end plate puts the assembly's middle between the upper plate's edges, so both lines
        // offer it; Bottom leaves it to Top and says so.
        var context = Context(rightHalfHeight: 68, upperPlate: true, midEnd: true);
        Assert.Contains(500d, Cumulative(Chain(context, "Top", "location")));
        var bottom = Chain(context, "Bottom", "location");
        Assert.Equal(16, Assert.Single(bottom.GetProperty("offeredOnOtherSidePartIds").EnumerateArray()).GetInt32());
        Assert.False(Chain(context, "Top", "location").TryGetProperty("offeredOnOtherSidePartIds", out _));
    }

    private static double[] Cumulative(JsonElement chain)
    {
        var total = -20d;
        return new[] { total }.Concat(Segments(chain).Select(x => total += x)).ToArray();
    }

    [Fact]
    public void RakedEndTakesTheCornerOnTheLineSideNotTheFarCorner()
    {
        var context = Context(rightHalfHeight: 68, rakedEnd: true);
        Assert.Equal(970d, Segments(Chain(context, "Top", "location")).Sum());
        Assert.Equal(1020d, Segments(Chain(context, "Bottom", "location")).Sum());
    }

    // The answer leaves an empty unlocated list out.
    private static IEnumerable<JsonElement> Unlocated(ViewDimensionContext context, string axis) =>
        context.Query("chainDetails").TryGetProperty($"unlocated{axis}ModelIds", out var list)
            ? list.EnumerateArray() : Enumerable.Empty<JsonElement>();

    private static JsonElement DetailedChain(ViewDimensionContext context, string side, string kind) =>
        context.Query("chainDetails", side).GetProperty("chainPreview").EnumerateArray().Single()
            .GetProperty("chains").EnumerateArray().Single(c => c.GetProperty("kind").GetString() == kind);

    [Fact]
    public void SectionAnswerIsOneChainPerAxisWithoutMirrorSides()
    {
        var context = ISectionContext(endPlate: true);
        var rows = context.Query("chain").GetProperty("chainPreview").EnumerateArray().ToArray();
        var sides = rows.Select(r => r.GetProperty("side").GetString()).ToArray();
        Assert.True(sides.Count(s => s is "Top" or "Bottom") <= 1, string.Join(",", sides));
        Assert.True(sides.Count(s => s is "Left" or "Right") <= 1, string.Join(",", sides));
        Assert.All(rows, r => Assert.Equal("chain", Assert.Single(r.GetProperty("chains").EnumerateArray()).GetProperty("kind").GetString()));
    }

    private static double[] Segments(JsonElement chain) =>
        chain.GetProperty("segments").EnumerateArray().Select(x => x.GetDouble()).ToArray();

    private static JsonElement Chain(ViewDimensionContext context, string side, string kind) =>
        context.Query("chain", side).GetProperty("chainPreview").EnumerateArray().Single()
            .GetProperty("chains").EnumerateArray().Single(c => c.GetProperty("kind").GetString() == kind);

    private static ViewDimensionContext ISectionContext(bool endPlate = false, bool rakedMain = false,
        string viewType = "SectionView", double? angleShift = null, bool roundedMain = false, double scale = 10)
    {
        var main = rakedMain
            ? Poly(10, (-80, -100), (80, -100), (70, 100), (-80, 100))
            : roundedMain
            ? Poly(10, (-80, -100), (80, -100), (80, -90), (20, -90),
                (10, -80), (10, 80), (20, 90), (80, 90), (80, 100), (-80, 100),
                (-80, 90), (-20, 90), (-10, 80), (-10, -80), (-20, -90), (-80, -90))
            : Poly(10, (-80, -100), (80, -100), (80, -90), (10, -90),
                (10, 90), (80, 90), (80, 100), (-80, 100), (-80, 90),
                (-10, 90), (-10, -90), (-80, -90));
        var parts = new Dictionary<int, PartSolidGeometryInViewResult> { [10] = main };
        var included = new List<PartRoleInView> {
            new(10, "P10", "P", new PartRoleResult(PartRole.Included, "included", "test"), true)
        };
        if (endPlate)
        {
            parts.Add(11, Solid(11, -130, 130, -170, 170));
            included.Add(new PartRoleInView(11, "P11", "P", new PartRoleResult(PartRole.Included, "included", "test"), false));
        }
        if (angleShift is { } shift)
        {
            parts.Add(12, Solid(12, 30, 35, 110, 120));
            parts.Add(13, Solid(13, 30 + shift, 35 + shift, 110, 120));
            included.Add(new PartRoleInView(12, "P12", "P", new PartRoleResult(PartRole.Included, "included", "test"), false));
            included.Add(new PartRoleInView(13, "P13", "P", new PartRoleResult(PartRole.Included, "included", "test"), false));
        }
        var outline = TeklaDrawingAssemblyOutlineApi.Build(7, parts.Keys, new Solids(parts));
        return new ViewDimensionContext(7, scale, new StructuralOutline(outline, included, [], [], [], [], parts.Keys.ToArray()),
            [], new { drawingGuid = "test" }, new { viewType });
    }

    private static ViewDimensionContext Context(double rightHalfHeight, string viewType = "BackView",
        bool secondMain = false, bool closeRib = false, bool unclassified = false, bool unread = false,
        bool rakedEnd = false, bool upperPlate = false, bool tallEnd = false, bool crossingPlate = false, bool edgeOnPlate = false, bool midEnd = false)
    {
        var parts = new Dictionary<int, PartSolidGeometryInViewResult> {
            [10] = Solid(10, 0, 1000, -76, 76),
            [11] = Solid(11, -20, 0, -170, 170),
            [12] = Solid(12, 200, 210, -66, 66),
            [13] = Solid(13, 1000, 1010, -rightHalfHeight, rightHalfHeight)
        };
        if (closeRib) parts[15] = Solid(15, 201.5, 206, -66, 66);
        if (upperPlate) parts[16] = Solid(16, 500, 510, 20, 120);
        if (crossingPlate) parts[16] = Solid(16, 500, 510, -20, 80);
        if (edgeOnPlate) parts[16] = Solid(16, 500, 510, -0.001, 0.001);
        if (midEnd) parts[11] = Solid(11, -20, 0, -76, 214);
        if (tallEnd) parts[11] = Solid(11, -20, 0, -170, 1000);
        if (rakedEnd)
        {
            parts.Remove(13);
            parts[10] = Poly(10, (0, -76), (1000, -76), (950, 76), (0, 76));
        }
        var ids = closeRib ? new[] { 10, 11, 12, 13, 15 } : new[] { 10, 11, 12, 13 };
        if (rakedEnd) ids = ids.Where(id => id != 13).ToArray();
        if (upperPlate || crossingPlate || edgeOnPlate) ids = ids.Append(16).ToArray();
        var outline = TeklaDrawingAssemblyOutlineApi.Build(7, ids, new Solids(parts));
        var role = new PartRoleResult(PartRole.Included, "included", "test");
        var structural = new StructuralOutline(outline,
            (new[] { new PartRoleInView(10, "P10", "P", role, true), new(11, "P11", "P", role, false),
             new(12, "P12", "P", role, secondMain), new(13, "P13", "P", role, false) })
                .Where(p => !rakedEnd || p.ModelId != 13).Concat(upperPlate || crossingPlate || edgeOnPlate ? [new PartRoleInView(16, "P16", "P", role, false)] : []).Concat(closeRib ? [new PartRoleInView(15, "P15", "P", role, false)] : []).ToArray(),
            [], unclassified ? [new PartRoleInView(14, "P14", "P", role, false)] : [],
            unread ? [new UnreadPart(14, "property read failed")] : [], [], ids);
        return new ViewDimensionContext(7, 10, structural, [], new { drawingGuid = "test" }, new { viewType });
    }

    private static PartSolidGeometryInViewResult Solid(int id, double x0, double x1, double y0, double y1) =>
        Poly(id, (x0, y0), (x1, y0), (x1, y1), (x0, y1));

    private static PartSolidGeometryInViewResult Poly(int id, params (double, double)[] points)
    {
        var geometry = new PartSolidGeometryInViewResult { Success = true, ModelId = id, ViewId = 7 };
        for (var i = 0; i < points.Length; i++)
            geometry.Solid.Vertices.Add(new PartVertexGeometry { Index = i, Point = [points[i].Item1, points[i].Item2, 0] });
        var face = new PartFaceGeometry { Index = 0, Normal = [0, 0, 1] };
        face.Loops.Add(new PartLoopGeometry { Index = 0, VertexIndexes = Enumerable.Range(0, points.Length).ToList() });
        geometry.Solid.Faces.Add(face);
        return geometry;
    }

    private sealed class Solids(Dictionary<int, PartSolidGeometryInViewResult> parts) : IDrawingPartSolidGeometryApi
    {
        public PartSolidGeometryInViewResult GetPartSolidGeometryInView(int viewId, int modelId) => parts[modelId];
    }
}
