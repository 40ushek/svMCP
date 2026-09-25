using System.Text.Json;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class ViewDimensionContextTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QuestionsAndMultipleCreatesShareOneReadInEitherOrder(bool createFirst)
    {
        var reads = 0;
        var writes = 0;
        var provider = new ViewDimensionContextProvider(() => "drawing-a",
            (id, filters) => { reads++; return Context(id, filters); }, () => { },
            (_, distance) => { writes++; Assert.Equal(420, distance); return new() { Created = true }; });
        if (!createFirst) provider.Get(7).ChainPositions();
        for (var i = 0; i < 3; i++) Assert.True(provider.Create(Request()).Created);
        provider.Get(7).Query("all");
        provider.Get(7).ChainPositions(true);
        Assert.Equal(1, reads);
        Assert.Equal(3, writes);
    }

    [Fact]
    public void ExplicitDistanceDoesNotBuildUnusedGeometry()
    {
        var provider = new ViewDimensionContextProvider(() => null,
            (_, _) => throw new Exception("unexpected read"), () => { }, (_, d) => new() { Created = d == 12 });
        var request = Request(); request.Distance = 12;
        Assert.True(provider.Create(request).Created);
    }

    [Fact]
    public void FiltersHaveCanonicalKeysAndNeverUseTheLastQueriedScope()
    {
        var reads = 0;
        var provider = new ViewDimensionContextProvider(() => "a",
            (id, filters) => { reads++; return Context(id, filters); }, () => { });
        var first = provider.Get(7, "r,M,r");
        Assert.Same(first, provider.Get(7, " m, R "));
        Assert.NotSame(first, provider.Get(7));
        Assert.NotSame(first, provider.Get(7, "", "r,m"));
        Assert.Same(first, provider.Get(7, "r,m"));
        Assert.Equal(3, reads);
    }

    [Fact]
    public void CreateUsesItsOwnFilters()
    {
        var scopes = new List<string>();
        var provider = new ViewDimensionContextProvider(() => "a", (id, f) => {
            scopes.Add(string.Join(",", f.Select(x => x.Id))); return Context(id, f);
        }, () => { }, (_, _) => new() { Created = true });
        provider.Get(7, "R");
        provider.Create(Request());
        var filtered = Request(); filtered.ExcludePrefixes = "r";
        provider.Create(filtered);
        Assert.Equal(new[] { "exclude-prefix:R", "" }, scopes);
    }

    [Fact]
    public void RefreshAndScopeSwitchesInvalidateLowerCachesBeforeReading()
    {
        string? drawing = "a";
        var calls = new List<string>();
        var provider = new ViewDimensionContextProvider(() => drawing,
            (id, f) => { calls.Add("read"); return Context(id, f); }, () => calls.Add("invalidate"));
        var first = provider.Get(7);
        Assert.Same(first, provider.Get(7));
        calls.Clear();
        Assert.NotSame(first, provider.Get(7, refresh: true));
        Assert.Equal(new[] { "invalidate", "read" }, calls);
        calls.Clear(); provider.Get(8);
        Assert.Equal(new[] { "invalidate", "read" }, calls);
        calls.Clear(); drawing = "b"; provider.Get(8);
        Assert.Equal("invalidate", calls[0]); Assert.Equal("read", calls.Last());
        drawing = null; provider.ObserveActiveDrawing(); drawing = "b";
        var returned = provider.Get(7);
        Assert.NotSame(first, returned);
    }

    [Fact]
    public void IncompleteReadKeepsDiagnosticsAndCannotReachWriter()
    {
        var provider = new ViewDimensionContextProvider(() => "a", (id, f) => Context(id, f, incomplete: true),
            () => { }, (_, _) => throw new Exception("writer must not run"));
        var json = provider.Get(7).Query();
        Assert.False(json.GetProperty("isComplete").GetBoolean());
        Assert.NotEmpty(json.GetProperty("issues").EnumerateArray());
        Assert.Equal(99, json.GetProperty("outsideDepthModelIds")[0].GetInt32());
        Assert.Throws<InvalidOperationException>(() => provider.Create(Request()));
    }

    [Fact]
    public void ExplicitDistanceCannotWritePointIdsFromIncompleteContext()
    {
        var provider = new ViewDimensionContextProvider(() => "a", (id, f) => Context(id, f, incomplete: true),
            () => { }, (_, _) => throw new Exception("writer must not run"));
        var context = provider.Get(7);
        var ids = context.Query("dimensionPoints", "Top").GetProperty("dimensionPoints")
            .GetProperty("sides")[0].GetProperty("points").EnumerateArray()
            .Select(point => point.GetProperty("pointId").GetString()!).Take(2).ToArray();

        var error = Assert.Throws<InvalidOperationException>(() => provider.Create(new CreateDimensionRequest {
            ViewId = 7, ContextId = context.ContextId, PointIds = ids, Direction = "horizontal", Distance = 10
        }));
        Assert.Contains("incomplete view context", error.Message);
    }

    [Fact]
    public void CompactQuestionsRetainEveryOwnersEvidenceAndLeaveFullSourceUnchanged()
    {
        var context = Context();
        var before = context.ChainPositions(true).GetRawText();
        var full = context.ChainPositions(true);
        var compact = context.Query("points", "all");
        foreach (var side in full.GetProperty("sides").EnumerateArray())
        {
            var name = side.GetProperty("side").GetString();
            var shortSide = compact.GetProperty("sides").EnumerateArray().Single(s => s.GetProperty("side").GetString() == name);
            var expected = side.GetProperty("positions").EnumerateArray().SelectMany(p => p.GetProperty("supports").EnumerateArray())
                .Select(s => Evidence(s.GetProperty("point")[0].GetDouble(), s.GetProperty("point")[1].GetDouble(), s)).ToHashSet();
            var actual = shortSide.GetProperty("points").EnumerateArray().SelectMany(p => p.GetProperty("supports").EnumerateArray()
                .Select(s => Evidence(p.GetProperty("x").GetDouble(), p.GetProperty("y").GetDouble(), s))).ToHashSet();
            Assert.True(expected.SetEquals(actual));
            var pts = shortSide.GetProperty("points").EnumerateArray().ToArray();
            Assert.Equal(pts.Length, pts.Select(p => (p.GetProperty("x").GetDouble(), p.GetProperty("y").GetDouble())).Distinct().Count());
        }
        context.Query("all", "Left");
        context.Calculate("horizontal", Request().Points);
        Assert.Equal(before, context.ChainPositions(true).GetRawText());
    }

    [Fact]
    public void MainPartIdentityDistinguishesKnownNoAndUnresolved()
    {
        var json = Context().ChainPositions();
        Assert.Equal(10, json.GetProperty("mainPartModelIds")[0].GetInt32());
        Assert.Equal(20, json.GetProperty("mainPartUnresolvedModelIds")[0].GetInt32());
    }

    [Fact]
    public void PlacementQuestionMatchesCreationCalculation()
    {
        var context = Context();
        var query = context.Query("placement,scale", points: Request().Points);
        Assert.Equal(context.Calculate("horizontal", Request().Points).Distance,
            query.GetProperty("placement").GetProperty("Distance").GetDouble());
        Assert.Throws<ArgumentException>(() => context.Query("placement"));
        Assert.Throws<ArgumentException>(() => context.Query("unknown"));
        Assert.Throws<ArgumentException>(() => context.Query(sides: "-1"));
    }

    [Fact]
    public void ChainRuleSetRejectsUnknownValuesAndDefaultsToSteel()
    {
        var context = Context();
        Assert.Throws<ArgumentException>(() => context.Query("chain", ruleSet: "brick"));
        var defaultAnswer = context.Query("chain", "Bottom");
        var explicitSteel = context.Query("chain", "Bottom", ruleSet: "steel");
        Assert.Equal(defaultAnswer.GetProperty("chainPreview").GetRawText(),
            explicitSteel.GetProperty("chainPreview").GetRawText());
    }

    [Fact]
    public void PanelRuleSetRoutesChainQuestionToTimberPreview()
    {
        var result = Context().Query("chainDetails", "Bottom", ruleSet: "panel");
        var chain = result.GetProperty("chainPreview")[0].GetProperty("chains")[0];
        Assert.Equal("not-requested", chain.GetProperty("contactStatus").GetString());
    }

    [Fact]
    public void ParserRetainsCreationFilterArguments()
    {
        var parse = DrawingCommandParsers.ParseCreateDimensionRequest(
            ["create_dimension", "7", "[0,0,0,100,0,0]", "horizontal", "", "standard", "8", "R,M", "wool"]);
        Assert.True(parse.IsValid);
        Assert.Equal("R,M", parse.Request.ExcludePrefixes);
        Assert.Equal("wool", parse.Request.ExcludeMaterials);
    }

    [Fact]
    public void DimensionPointQueryReturnsContextScopedIdsWithoutCoordinatesAndSharesCornerIds()
    {
        var json = Context().Query("dimensionPoints");
        var contextId = json.GetProperty("contextId").GetString();
        Assert.StartsWith("ctx_", contextId);

        var sides = json.GetProperty("dimensionPoints").GetProperty("sides").EnumerateArray().ToArray();
        Assert.Equal(4, sides.Length);
        var allIds = new List<string>();
        foreach (var side in sides)
        {
            var points = side.GetProperty("points").EnumerateArray().ToArray();
            Assert.True(points.Length >= 2);
            foreach (var point in points)
            {
                Assert.True(point.TryGetProperty("pointId", out _));
                Assert.False(point.TryGetProperty("x", out _));
                Assert.False(point.TryGetProperty("y", out _));
                Assert.True(point.TryGetProperty("kinds", out _));
                Assert.True(point.TryGetProperty("parents", out _));
                allIds.Add(point.GetProperty("pointId").GetString()!);
            }
        }

        Assert.True(allIds.Count > allIds.Distinct(StringComparer.Ordinal).Count());
        Assert.False(Context().Query("all").TryGetProperty("dimensionPoints", out _));
    }

    [Fact]
    public void CreateResolvesIdsFromTheirCachedContextAndPreservesRequestedOrder()
    {
        var reads = 0;
        double[]? writtenPoints = null;
        var provider = new ViewDimensionContextProvider(() => "drawing-a",
            (id, filters) => { reads++; return Context(id, filters); }, () => { },
            (request, distance) => {
                writtenPoints = request.Points;
                Assert.Equal(12, distance);
                return new() { Created = true };
            });
        var context = provider.Get(7);
        var side = context.Query("dimensionPoints", "Top").GetProperty("dimensionPoints")
            .GetProperty("sides")[0].GetProperty("points");
        var allIdPoints = side.EnumerateArray().ToArray();
        var allCoordinates = context.Query("points", "Top").GetProperty("sides")[0].GetProperty("points")
            .EnumerateArray().ToArray();
        // Two points on different coordinates along the line: on one coordinate the id form moves
        // the point to the outermost one, so only the order and the along-coordinate are compared.
        var second = Array.FindIndex(allCoordinates, p => p.GetProperty("x").GetDouble() != allCoordinates[0].GetProperty("x").GetDouble());
        var idPoints = new[] { allIdPoints[0], allIdPoints[second] };
        var coordinates = new[] { allCoordinates[0], allCoordinates[second] };
        var ids = idPoints.Reverse().Select(p => p.GetProperty("pointId").GetString()!).ToArray();

        Assert.True(provider.Create(new CreateDimensionRequest {
            ViewId = 7, ContextId = context.ContextId, PointIds = ids, Direction = "horizontal", Distance = 12
        }).Created);

        Assert.Equal(1, reads);
        var expectedX = coordinates.Reverse().Select(p => p.GetProperty("x").GetDouble());
        Assert.Equal(expectedX, new[] { writtenPoints![0], writtenPoints[3] });
    }

    [Fact]
    public void CreateRejectsExpiredContextUnknownIdsWrongSideAndCoordinateMix()
    {
        var provider = new ViewDimensionContextProvider(() => "drawing-a",
            (id, filters) => Context(id, filters), () => { }, (_, _) => new() { Created = true });
        var context = provider.Get(7);
        var ids = context.Query("dimensionPoints", "Top").GetProperty("dimensionPoints")
            .GetProperty("sides")[0].GetProperty("points").EnumerateArray()
            .Select(p => p.GetProperty("pointId").GetString()!).Take(2).ToArray();

        Assert.Throws<InvalidOperationException>(() => provider.Create(new CreateDimensionRequest {
            ViewId = 7, ContextId = "ctx_expired", PointIds = ids, Direction = "horizontal", Distance = 10
        }));
        Assert.Throws<ArgumentException>(() => provider.Create(new CreateDimensionRequest {
            ViewId = 7, ContextId = context.ContextId, PointIds = ids, Points = [1, 2, 0], Direction = "horizontal", Distance = 10
        }));
        Assert.Throws<ArgumentException>(() => provider.Create(new CreateDimensionRequest {
            ViewId = 7, ContextId = context.ContextId, PointIds = [ids[0], "unknown"], Direction = "horizontal", Distance = 10
        }));
        Assert.Throws<ArgumentException>(() => provider.Create(new CreateDimensionRequest {
            ViewId = 7, ContextId = context.ContextId, PointIds = [ids[0], ids[0]], Direction = "horizontal", Distance = 10
        }));

        var refreshContext = provider.Get(7, refresh: true);
        Assert.NotEqual(context.ContextId, refreshContext.ContextId);
        Assert.Throws<InvalidOperationException>(() => provider.Create(new CreateDimensionRequest {
            ViewId = 7, ContextId = context.ContextId, PointIds = ids, Direction = "horizontal", Distance = 10
        }));
    }

    [Fact]
    public void DifferentFilterSnapshotsKeepIndependentContextIdsUntilRefresh()
    {
        var written = new List<double[]>();
        var provider = new ViewDimensionContextProvider(() => "drawing-a",
            (id, filters) => Context(id, filters), () => { },
            (request, _) => { written.Add(request.Points); return new() { Created = true }; });
        var unfiltered = provider.Get(7);
        var filtered = provider.Get(7, "R");
        Assert.NotEqual(unfiltered.ContextId, filtered.ContextId);
        var ids = unfiltered.Query("dimensionPoints", "Top").GetProperty("dimensionPoints")
            .GetProperty("sides")[0].GetProperty("points").EnumerateArray()
            .Select(p => p.GetProperty("pointId").GetString()!).Take(2).ToArray();

        Assert.True(provider.Create(new CreateDimensionRequest {
            ViewId = 7, ContextId = unfiltered.ContextId, PointIds = ids, Direction = "horizontal", Distance = 10
        }).Created);
        Assert.Single(written);
    }

    [Fact]
    public void CreateDimensionParserAcceptsOnlyOneOfCoordinatesOrContextPointIds()
    {
        var byId = DrawingCommandParsers.ParseCreateDimensionRequest(
            ["create_dimension", "7", "", "horizontal", "10", "standard", "", "", "", "ctx_abc", "[\"p0001\",\"p0002\"]"]);
        Assert.True(byId.IsValid, byId.Error);
        Assert.Equal(new[] { "p0001", "p0002" }, byId.Request.PointIds);

        var withFilters = DrawingCommandParsers.ParseCreateDimensionRequest(
            ["create_dimension", "7", "", "horizontal", "10", "standard", "", "R", "", "ctx_abc", "[\"p0001\",\"p0002\"]"]);
        Assert.False(withFilters.IsValid);

        var both = DrawingCommandParsers.ParseCreateDimensionRequest(
            ["create_dimension", "7", "[0,0,0,1,0,0]", "horizontal", "10", "standard", "", "", "", "ctx_abc", "[\"p0001\",\"p0002\"]"]);
        Assert.False(both.IsValid);
    }

    [Fact]
    public void SolidGeometryIsStoredAsAnIsolatedSnapshotAndMissingPartsReturnNull()
    {
        var context = Context();

        var returned = context.GetPartSolidGeometry(10);
        Assert.NotNull(returned);
        Assert.Equal(4, returned.Solid.Vertices.Count);
        Assert.Single(returned.Solid.Faces);
        returned.Solid.Vertices[0].Point[0] = 999;
        returned.Solid.Faces[0].Loops[0].VertexIndexes.Clear();

        var stored = context.GetPartSolidGeometry(10);
        Assert.Equal(0, stored!.Solid.Vertices[0].Point[0]);
        Assert.Equal(new[] { 0, 1, 2, 3 }, stored.Solid.Faces[0].Loops[0].VertexIndexes);
        Assert.Null(context.GetPartSolidGeometry(999));
        Assert.NotNull(context.GetPartSolidGeometrySnapshot(10));
    }

    [Fact]
    public void ContactsAreOptInLazyAndCachedAcrossDimensionFilterContexts()
    {
        var solidReads = new List<(int ViewId, int ModelId)>();
        var provider = new ViewDimensionContextProvider(() => "drawing-a",
            (id, filters) => Context(id, filters), () => { },
            readContactSolid: (viewId, modelId) => {
                solidReads.Add((viewId, modelId));
                return new PartSolidGeometryInViewResult {
                    ViewId = viewId, ModelId = modelId, Success = false, Error = "test unread"
                };
            });

        provider.Get(7).Query("all");
        Assert.Empty(solidReads);

        var contacts = provider.Get(7, "R").Query("contacts").GetProperty("contacts");
        Assert.Equal("all-depth-visible", contacts.GetProperty("scope").GetString());
        Assert.False(contacts.GetProperty("exclusionsApplied").GetBoolean());
        Assert.Single(solidReads);
        Assert.Equal((7, 20), solidReads[0]); // model 10 was reused from the captured outline

        provider.Get(7).Query("contacts");
        Assert.Single(solidReads); // both the extra solid read and contact result are cached
    }

    [Fact]
    public void DisplayRoundingDoesNotSerializeBinaryFloatingPointTail()
    {
        var json = ViewDimensionContext.Freeze(new { point = 4704.0010000000002 }, display: true);
        Assert.Equal("4704.001", json.GetProperty("point").GetRawText());
    }

    private static string Evidence(double x, double y, JsonElement s) =>
        JsonSerializer.Serialize(new { x, y,
            model = s.TryGetProperty("modelId", out var id) && id.ValueKind != JsonValueKind.Null ? id.GetInt32() : (int?)null,
            kind = s.GetProperty("kind").GetString(), hole = s.TryGetProperty("isHole", out var h) && h.GetBoolean(),
            extent = s.TryGetProperty("partExtentAlongChain", out var e) && e.ValueKind != JsonValueKind.Null ? e.GetDouble() : (double?)null });

    private static CreateDimensionRequest Request() => new() { ViewId = 7, Points = [0, 0, 0, 260, 0, 0], Direction = "horizontal" };

    internal static ViewDimensionContext Context(int id = 7, IReadOnlyList<PartExclusionRule>? exclusions = null, bool incomplete = false)
    {
        var geometry = new PartSolidGeometryInViewResult { Success = true, ModelId = 10, ViewId = id };
        var points = new[] { (0d, 0d), (260d, 0d), (260d, 340d), (0d, 340d) };
        for (var i = 0; i < points.Length; i++)
            geometry.Solid.Vertices.Add(new PartVertexGeometry { Index = i, Point = [points[i].Item1, points[i].Item2, 0] });
        var face = new PartFaceGeometry { Index = 0, Normal = [0, 0, 1] };
        face.Loops.Add(new PartLoopGeometry { Index = 0, VertexIndexes = [0, 1, 2, 3] });
        geometry.Solid.Faces.Add(face);
        var outline = TeklaDrawingAssemblyOutlineApi.Build(id, [10], new Solid(geometry));
        var role = new PartRoleResult(PartRole.Included, "included", "test");
        var structural = new StructuralOutline(outline,
            [new(10, "P10", "P", role, true)], [new(20, "R20", "R", new PartRoleResult(PartRole.Excluded, "excluded", "test"), false, false)], [],
            incomplete ? [new UnreadPart(30, "unread")] : [], [99], [10, 20]);
        return new ViewDimensionContext(id, 10, structural, exclusions ?? [], new { drawingGuid = "test" }, new { viewType = "FrontView" });
    }

    private sealed class Solid(PartSolidGeometryInViewResult geometry) : IDrawingPartSolidGeometryApi
    {
        public PartSolidGeometryInViewResult GetPartSolidGeometryInView(int viewId, int modelId) => geometry;
    }
}
