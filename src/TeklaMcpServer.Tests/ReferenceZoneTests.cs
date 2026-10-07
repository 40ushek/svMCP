using Clipper2Lib;
using SolidContacts;
using System.Text.Json;
using TeklaMcpServer.Api.Drawing;
using TeklaMcpServer.Tools;
using Xunit;
using Kind = TeklaMcpServer.Api.Drawing.TimberPanelPartLocationRule.CandidateKind;

namespace TeklaMcpServer.Tests;

public sealed class ReferenceZoneTests
{
    private sealed class Roles(params PartRoleInView[] parts) : IDrawingPartRoleApi
    {
        public PartRoleReadResult GetRolesInView(int viewId) => new(parts, []);
    }

    private sealed class Outlines(Dictionary<int, PolyTreeD> trees, int? failedId = null) : IDrawingViewOutlineApi
    {
        public int Reads { get; private set; }
        public int[] Asked { get; private set; } = [];
        public ViewAssemblyOutlineResult GetAssemblyOutline(int viewId, OutlineOptions? options = null,
            IReadOnlyCollection<int>? modelIds = null)
        {
            Reads++;
            Asked = modelIds!.ToArray();
            var parts = trees.Where(pair => Asked.Contains(pair.Key) && pair.Key != failedId).ToDictionary();
            return new(viewId, ProjectedOutlineBuilder.BuildAssembly(parts.Values), parts,
                failedId.HasValue ? [new UnreadPart(failedId.Value, "synthetic solid failure")] : [],
                restricted: true, visibleCount: trees.Count, requestedIds: Asked);
        }
    }

    private static PartRoleInView Part(int id, string zone, bool included = true, string prefix = "P",
        string material = "TIMBER", bool zoneKnown = true) => new(id, "P" + id, prefix,
            new PartRoleResult(included ? PartRole.Included : PartRole.Excluded, "fixture", "fixture"),
            material: material, zone: zone, zoneKnown: zoneKnown);

    private static PolyTreeD Rectangle(double minX, double maxX, double minY, double maxY)
    {
        var clipper = new ClipperD();
        clipper.AddSubject(new PathsD { new() { new(minX, minY), new(maxX, minY), new(maxX, maxY), new(minX, maxY) } });
        var tree = new PolyTreeD();
        clipper.Execute(ClipType.Union, FillRule.NonZero, tree);
        return tree;
    }

    private static (StructuralOutline Outline, Outlines Reader) Capture(string[]? zones = null,
        bool unreadZone = false, bool failedReference = false, double referenceMin = 5,
        double referenceMax = 195, bool neighbours = false)
    {
        var parts = new List<PartRoleInView> { Part(1, "sheet"), Part(2, "sheet"),
            Part(10, "datum", false, zoneKnown: !unreadZone), Part(11, "datum", false, prefix: "INS") };
        var trees = new Dictionary<int, PolyTreeD> { [1] = Rectangle(0, 20, 0, 500),
            [2] = Rectangle(180, 200, 0, 500), [10] = Rectangle(referenceMin, referenceMax, 10, 490),
            [11] = Rectangle(-100, 500, -100, 600) };
        if (neighbours) { parts.Add(Part(12, "datum", false)); trees.Add(12, Rectangle(250, 400, 10, 490)); }
        var reader = new Outlines(trees, failedReference ? 10 : null);
        var insulation = PartExclusionRule.ByPrefix("INS");
        var outline = new TeklaDrawingStructuralOutlineApi(new Roles(parts.ToArray()), reader).Get(7,
            referenceZones: zones, exclusions: [insulation], referenceExclusions: [insulation]);
        return (outline, reader);
    }

    private static ViewDimensionContext Context(StructuralOutline outline) => new(7, 10, outline,
        [PartExclusionRule.ByPrefix("INS")], new { drawingGuid = "fixture" }, new { viewType = "FrontView" });

    private static JsonElement Chain(JsonElement answer, string side, string kind = "location") => answer
        .GetProperty("chainPreview").EnumerateArray().Single(row => row.GetProperty("side").GetString() == side)
        .GetProperty("chains").EnumerateArray().Single(chain => chain.GetProperty("kind").GetString() == kind);

    [Fact]
    public void ActualReferencePolygonAddsOffsetsAndUsesOneCombinedRead()
    {
        var (outline, reader) = Capture(["datum"]);
        Assert.Equal(1, reader.Reads);
        Assert.Equal(new[] { 1, 2, 10 }, reader.Asked);
        Assert.Equal(new[] { 1, 2 }, outline.Outline.PartOutlines.Keys.OrderBy(id => id));
        Assert.Equal(new[] { 10 }, outline.ReferenceZone!.Outline!.PartOutlines.Keys);
        var context = Context(outline);
        var answer = context.Query("chainDetails", ruleSet: "panel");
        var chain = Chain(answer, "Bottom");
        Assert.Equal(new[] { 5d, 15, 160, 15, 5 }, chain.GetProperty("segments").EnumerateArray().Select(value => value.GetDouble()));
        Assert.Equal(2, chain.GetProperty("points").EnumerateArray().Count(point =>
            point.GetProperty("candidateKinds").EnumerateArray().Any(kind => kind.GetString() == "ReferenceZone")));
        Assert.Empty(chain.GetProperty("missingSupportModelIds").EnumerateArray());
        var ids = chain.GetProperty("pointIds").EnumerateArray().Select(id => id.GetString()!).ToArray();
        Assert.Equal(new[] { 0d, 5, 20, 180, 195, 200 }, context.ResolvePointIds(ids, "horizontal-down")
            .Where((_, index) => index % 3 == 0).Select(value => Math.Round(value, 6)));
        Assert.Equal(200, Chain(answer, "Bottom", "overall").GetProperty("segments")[0].GetDouble());
        Assert.Equal(500, Chain(answer, "Right", "overall").GetProperty("segments")[0].GetDouble());
        Assert.Equal("complete", answer.GetProperty("chainDiagnostics").GetProperty("referenceZone").GetProperty("status").GetString());
        double[]? writtenPoints = null;
        var provider = new ViewDimensionContextProvider(() => "drawing", (_, _) => context, () => { },
            write: (request, _) => { writtenPoints = request.Points; return new() { Created = true }; },
            referenceRead: (_, _, _, _) => context);
        Assert.Same(context, provider.Get(7, referenceZones: ["datum"]));
        Assert.True(provider.Create(new CreateDimensionRequest {
            ViewId = 7, ContextId = context.ContextId, PointIds = ids,
            Direction = "horizontal-down", Distance = 12, RuleSet = "panel"
        }).Created);
        Assert.Equal(new[] { 0d, 5, 20, 180, 195, 200 }, writtenPoints!
            .Where((_, index) => index % 3 == 0).Select(value => Math.Round(value, 6)));
        Assert.Equal(1, reader.Reads);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void UnreadZoneOrReferenceSolidSuppressesEntireReferenceMix(bool unreadZone, bool failedReference)
    {
        var (outline, reader) = Capture(["datum"], unreadZone, failedReference);
        var answer = Context(outline).Query("chainDetails", ruleSet: "panel");
        Assert.Equal(new[] { 20d, 160, 20 }, Chain(answer, "Bottom").GetProperty("segments").EnumerateArray().Select(value => value.GetDouble()));
        Assert.Equal("suppressed", answer.GetProperty("chainDiagnostics").GetProperty("referenceZone").GetProperty("status").GetString());
        Assert.DoesNotContain(11, reader.Asked);
        Assert.True(outline.IsComplete);
    }

    [Fact]
    public void NeighbouringReferenceComponentDoesNotChangePanelBoundsOrChains()
    {
        var normal = Context(Capture(["datum"]).Outline).Query("chainDetails", ruleSet: "panel");
        var neighbours = Context(Capture(["datum"], neighbours: true).Outline).Query("chainDetails", ruleSet: "panel");
        Assert.Equal(Chain(normal, "Bottom").GetProperty("segments").GetRawText(), Chain(neighbours, "Bottom").GetProperty("segments").GetRawText());
        Assert.Equal(200, Chain(neighbours, "Bottom", "overall").GetProperty("segments")[0].GetDouble());
    }

    [Fact]
    public void CoincidentKindsPreservePanelOwnershipWithoutCountingReferenceParents()
    {
        var outline = Capture(["datum"], referenceMin: 20, referenceMax: 180).Outline;
        var context = Context(outline);
        var answer = context.Query("chainDetails", ruleSet: "panel");
        var shared = Chain(answer, "Bottom").GetProperty("points").EnumerateArray().Where(point =>
            point.GetProperty("candidateKinds").EnumerateArray().Any(kind => kind.GetString() == "ReferenceZone")).ToArray();
        Assert.NotEmpty(shared);
        Assert.All(shared, point => Assert.Contains(point.GetProperty("candidateKinds").EnumerateArray(), kind => kind.GetString() == "Member"));
        var group = StructuralGeometryGroupBuilder.Build(outline);
        CalcDimensionChains.Apply(group);
        var point = DimensionPointCatalog.Build(group.DimensionChains!).AllPoints.First(p => p.Parents.Any(parent => parent.ModelId == 1));
        Assert.Empty(TimberPanelPartLocationRule.AccountedPartIds(point, [Kind.ReferenceZone], [1, 2]));
        Assert.Contains(1, TimberPanelPartLocationRule.AccountedPartIds(point, [Kind.ReferenceZone, Kind.Member], [1, 2]));
        Assert.Empty(TimberPanelPartLocationRule.AccountedPartIds(point, [Kind.Member], [10]));
        var coincidentTree = outline.Outline.PartOutlines[1];
        var referenceOutline = new StructuralOutline(new ViewAssemblyOutlineResult(7, coincidentTree,
            new Dictionary<int, PolyTreeD> { [10] = coincidentTree }, []), [Part(10, "datum")], [], []);
        var referenceGroup = StructuralGeometryGroupBuilder.Build(referenceOutline);
        CalcDimensionChains.Apply(referenceGroup);
        var combined = DimensionPointCatalog.Build(group.DimensionChains!).WithReference(
            DimensionPointCatalog.Build(referenceGroup.DimensionChains!), group.Extent!,
            ReferenceZoneOutline.VerticesForPanel(referenceGroup, group.Extent!));
        var combinedPoint = combined.AllPoints.First(p => p.Parents.Any(parent => parent.ModelId == 1)
            && p.Parents.Any(parent => parent.ModelId == 10));
        Assert.Equal(new[] { 1 }, TimberPanelPartLocationRule.AccountedPartIds(combinedPoint,
            [Kind.ReferenceZone, Kind.Member], [1, 2]));
    }

    [Fact]
    public void DroppingAShortReferenceOffsetDoesNotReportFrameAsDroppedPanelMember()
    {
        var outline = Capture(["datum"], referenceMin: 5, referenceMax: 199).Outline;
        var answer = Context(outline).Query("chainDetails", ruleSet: "panel");
        Assert.DoesNotContain(Chain(answer, "Bottom").GetProperty("droppedShortPartIds").EnumerateArray(), id => id.GetInt32() == 10);
        Assert.DoesNotContain(Chain(answer, "Bottom").GetProperty("segments").EnumerateArray(), segment => segment.GetDouble() == 1);
    }

    [Fact]
    public void SheetWithoutVerticalMembersStillGetsVerticalReferenceOffsets()
    {
        var reader = new Outlines(new Dictionary<int, PolyTreeD> {
            [1] = Rectangle(0, 1000, 0, 500), [10] = Rectangle(5, 995, 10, 490) });
        var outline = new TeklaDrawingStructuralOutlineApi(new Roles(Part(1, "sheet"), Part(10, "datum", false)), reader)
            .Get(7, referenceZones: ["datum"]);
        var answer = Context(outline).Query("chainDetails", ruleSet: "panel");
        Assert.Equal(new[] { 10d, 480, 10 }, Chain(answer, "Left").GetProperty("segments").EnumerateArray().Select(value => value.GetDouble()));
        Assert.Equal(500, Chain(answer, "Right", "overall").GetProperty("segments")[0].GetDouble());
    }

    [Fact]
    public void ReferenceSelectionPreservesExistingPanelSelectionAndReadsOverlappingIdsOnce()
    {
        var parts = new[] { Part(1, "sheet"), Part(10, "datum") };
        var trees = new Dictionary<int, PolyTreeD> {
            [1] = Rectangle(0, 1000, 0, 500), [10] = Rectangle(5, 995, 10, 490) };
        var baseline = new TeklaDrawingStructuralOutlineApi(new Roles(parts), new Outlines(trees)).Get(7);
        var reader = new Outlines(trees);
        var selected = new TeklaDrawingStructuralOutlineApi(new Roles(parts), reader).Get(7, referenceZones: ["datum"]);
        Assert.Equal(baseline.Included.Select(part => part.ModelId), selected.Included.Select(part => part.ModelId));
        Assert.Equal(new[] { 1, 10 }, selected.Outline.PartOutlines.Keys.OrderBy(id => id));
        Assert.Equal(new[] { 10 }, selected.ReferenceZone!.ModelIds);
        Assert.Equal(new[] { 1, 10 }, reader.Asked);
        Assert.Equal(1, reader.Reads);
        var answer = Context(selected).Query("chainDetails", ruleSet: "panel");
        Assert.Contains(Chain(answer, "Left").GetProperty("points").EnumerateArray(), point =>
            point.GetProperty("candidateKinds").EnumerateArray().Any(kind => kind.GetString() == "ReferenceZone"));
        Assert.Equal(500, Chain(answer, "Right", "overall").GetProperty("segments")[0].GetDouble());
    }

    [Fact]
    public void ExcludingTheFrameFromTheMeasuredLayerDoesNotBlockItAsReference()
    {
        // Reproduces a live case (PR2613, EW/2, view 1213, 2026-10-07): measuring the cladding
        // (prefix S) against the frame (prefix T, zone 0) needs excludePrefixes=T so the frame is
        // not counted as the cladding panel's own member. That exclusion must not also remove the
        // frame from being eligible as the reference - the frame is still part of the same panel,
        // only not part of the layer being measured.
        var parts = new[] { Part(1, "3", prefix: "S"), Part(10, "0", included: false, prefix: "T"),
            Part(12, "0", included: false, prefix: "T") };
        var trees = new Dictionary<int, PolyTreeD> {
            [1] = Rectangle(0, 1200, 0, 3000), [10] = Rectangle(-50, 1250, -50, 3050),
            [12] = Rectangle(1400, 2500, -100, 3200) };
        var outline = new TeklaDrawingStructuralOutlineApi(new Roles(parts), new Outlines(trees)).Get(7,
            referenceZones: ["0"], exclusions: [PartExclusionRule.ByPrefix("T")]);
        Assert.Equal(new[] { 1 }, outline.Included.Select(part => part.ModelId));
        Assert.Null(outline.ReferenceZone!.Error);
        Assert.Equal(new[] { 10, 12 }, outline.ReferenceZone!.ModelIds);
        var context = Context(outline);
        var answer = context.Query("chainDetails", ruleSet: "panel");
        var bottom = Chain(answer, "Bottom");
        Assert.Equal(new[] { 50d, 1200, 50 }, bottom.GetProperty("segments").EnumerateArray().Select(value => value.GetDouble()));
        Assert.Equal(new[] { 50d, 3000, 50 }, Chain(answer, "Left").GetProperty("segments").EnumerateArray().Select(value => value.GetDouble()));
        Assert.Equal(1300, Chain(answer, "Bottom", "overall").GetProperty("segments")[0].GetDouble());
        Assert.Equal(3100, Chain(answer, "Right", "overall").GetProperty("segments")[0].GetDouble());
        Assert.Empty(bottom.GetProperty("missingSupportModelIds").EnumerateArray());
        Assert.DoesNotContain(bottom.GetProperty("droppedShortPartIds").EnumerateArray(), id => id.GetInt32() == 10);
        var ids = bottom.GetProperty("pointIds").EnumerateArray().Select(id => id.GetString()!).ToArray();
        Assert.Equal(new[] { -50d, 0, 1200, 1250 }, context.ResolvePointIds(ids, "horizontal-down")
            .Where((_, index) => index % 3 == 0).Select(value => Math.Round(value, 6)));
    }

    private static (ViewDimensionContext Context, Outlines Reader) OverhangingContext()
    {
        var reader = new Outlines(new Dictionary<int, PolyTreeD> {
            [1] = Rectangle(0, 1200, 0, 3000), [10] = Rectangle(-50, 1250, -50, 3050),
            [12] = Rectangle(1400, 2500, -100, 3200) });
        var outline = new TeklaDrawingStructuralOutlineApi(new Roles(Part(1, "3"),
            Part(10, "0", false), Part(12, "0", false)), reader).Get(7, referenceZones: ["0"]);
        return (Context(outline), reader);
    }

    [Theory]
    [InlineData("horizontal-down", -50, -50, 1250, -50, -60)]
    [InlineData("horizontal", -50, 3050, 1250, 3050, 3060)]
    [InlineData("vertical-left", -50, -50, -50, 3050, -60)]
    [InlineData("vertical", 1250, -50, 1250, 3050, 1260)]
    public void PanelPlacementUsesScopedUnionOnEverySideWhileSteelKeepsItsOwnBounds(
        string direction, double x1, double y1, double x2, double y2, double target)
    {
        var (context, reader) = OverhangingContext();
        double[] points = [x1, y1, 0, x2, y2, 0];
        Assert.Throws<ArgumentException>(() => context.Calculate(direction, points, 1));
        var placement = context.Query("placement", points: points, direction: direction, paperGapMm: 1, ruleSet: "panel")
            .GetProperty("placement");
        Assert.Equal(10, Math.Round(placement.GetProperty("Distance").GetDouble(), 6));
        Assert.Equal(target, Math.Round(placement.GetProperty("TargetLineCoordinate").GetDouble(), 6));
        Assert.Throws<ArgumentException>(() => context.Query("placement", points: points, direction: direction, paperGapMm: 1, ruleSet: "steel"));
        Assert.Equal(1, reader.Reads);
    }

    [Fact]
    public void AutomaticPanelWritesUseUnionForPreviewAndExplicitIdsWithoutAnotherSolidRead()
    {
        var (context, reader) = OverhangingContext();
        var dimensions = new GetDimensionsResult();
        var writes = new List<(double[] Points, double Distance)>();
        var provider = new ViewDimensionContextProvider(() => "drawing", (_, _) => context, () => { },
            write: (request, distance) => {
                writes.Add((request.Points, distance));
                var id = 100 + writes.Count;
                var points = Enumerable.Range(0, request.Points.Length / 3).Select(i => new DrawingPointInfo {
                    X = request.Points[i * 3], Y = request.Points[i * 3 + 1], Order = i }).ToList();
                dimensions.Groups.Add(new DimensionGroupInfo { DimensionType = "Horizontal", TopDirection = -1,
                    Items = [new DimensionItemInfo { Id = id, Distance = distance, PointList = points,
                        TeklaDimensionType = "Relative",
                        ReferenceLine = new DrawingLineInfo { StartY = points.Min(point => point.Y) - distance },
                        LengthList = points.Zip(points.Skip(1), (a, b) => Math.Abs(b.X - a.X)).ToList() }] });
                return new CreateDimensionResult { Created = true, DimensionId = id };
            }, referenceRead: (_, _, _, _) => context, readDimensions: _ => dimensions, validateAttributes: _ => { });
        provider.Get(7, referenceZones: ["0"]);
        var result = provider.CreateBatch(new CreateDimensionsBatchRequest { ViewId = 7, ContextId = context.ContextId,
            RuleSet = "panel", Chains = [new BatchDimensionChain { Preview = "Bottom-location", PaperGapMm = 1 }] });
        Assert.Equal("created", result.Chains.Single().Status);
        Assert.Equal(10, writes.Single().Distance);
        var answer = context.Query("chainDetails", ruleSet: "panel");
        var ids = Chain(answer, "Bottom", "overall").GetProperty("pointIds").EnumerateArray().Select(id => id.GetString()!).ToArray();
        var created = provider.Create(new CreateDimensionRequest { ViewId = 7, ContextId = context.ContextId,
            PointIds = ids, Direction = "horizontal-down", PaperGapMm = 2, RuleSet = "panel" });
        Assert.True(created.Created);
        Assert.Equal(20, Math.Round(Convert.ToDouble(created.DistanceUsed), 6));
        Assert.Equal(new[] { -50d, 1250 }, writes.Last().Points.Where((_, index) => index % 3 == 0).Select(value => Math.Round(value, 6)));
        // An explicit steel batch on the same captured context keeps both the original
        // supports and placement bounds, even though this context also has reference geometry.
        var ownIds = Chain(answer, "Bottom").GetProperty("points").EnumerateArray()
            .Where(point => point.GetProperty("candidateKinds").EnumerateArray().Any(kind => kind.GetString() != "ReferenceZone"))
            .Select(point => point.GetProperty("pointId").GetString()!).ToArray();
        var steel = provider.CreateBatch(new CreateDimensionsBatchRequest { ViewId = 7, ContextId = context.ContextId,
            RuleSet = "steel", Chains = [new BatchDimensionChain { Key = "own", PointIds = ownIds,
                Direction = "horizontal-down", PaperGapMm = 1 }] });
        Assert.Equal("created", steel.Chains.Single().Status);
        Assert.Equal(10, writes.Last().Distance);
        Assert.Equal(new[] { 0d, 1200 }, writes.Last().Points.Where((_, index) => index % 3 == 0));
        Assert.Equal(1, reader.Reads);
    }

    [Fact]
    public void ExplicitRuleSetPlacesPanelOwnedOverallEndpointsAgainstTheReferenceBounds()
    {
        var reader = new Outlines(new Dictionary<int, PolyTreeD> {
            [1] = Rectangle(0, 1200, 0, 3000), [10] = Rectangle(50, 1150, -50, 3050) });
        var outline = new TeklaDrawingStructuralOutlineApi(new Roles(Part(1, "3"), Part(10, "0", false)), reader)
            .Get(7, referenceZones: ["0"]);
        var context = Context(outline);
        var answer = context.Query("chainDetails", ruleSet: "panel");
        var ids = Chain(answer, "Bottom", "overall").GetProperty("pointIds").EnumerateArray().Select(id => id.GetString()!).ToArray();
        Assert.All(ids, id => Assert.StartsWith("p", id));
        var writes = 0;
        var provider = new ViewDimensionContextProvider(() => "drawing", (_, _) => context, () => { },
            write: (_, _) => { writes++; return new() { Created = true }; }, referenceRead: (_, _, _, _) => context);
        provider.Get(7, referenceZones: ["0"]);
        Assert.Throws<ArgumentException>(() => provider.Create(new CreateDimensionRequest {
            ViewId = 7, ContextId = context.ContextId, PointIds = ids, Direction = "horizontal-down", PaperGapMm = 1 }));
        Assert.Equal(0, writes);
        var panel = provider.Create(new CreateDimensionRequest { ViewId = 7, ContextId = context.ContextId,
            PointIds = ids, Direction = "horizontal-down", PaperGapMm = 1, RuleSet = "panel" });
        Assert.Equal(60, Math.Round(Convert.ToDouble(panel.DistanceUsed), 6));
        Assert.Equal(-60, Math.Round(panel.Placement!.TargetLineCoordinate, 6));
        var steel = provider.Create(new CreateDimensionRequest { ViewId = 7, ContextId = context.ContextId,
            PointIds = ids, Direction = "horizontal-down", PaperGapMm = 1, RuleSet = "steel" });
        Assert.Equal(10, Math.Round(Convert.ToDouble(steel.DistanceUsed), 6));
        Assert.Equal(-10, steel.Placement!.TargetLineCoordinate);
        Assert.Equal(1, reader.Reads);
    }

    [Fact]
    public void AsymmetricReferenceUsesGlobalOverallBoundsAndResolvesOppositeSideVertices()
    {
        var clipper = new ClipperD();
        clipper.AddSubject(new PathsD { new() { new(-50, -50), new(1200, -50), new(1300, 3050), new(-50, 3050) } });
        var frame = new PolyTreeD();
        clipper.Execute(ClipType.Union, FillRule.NonZero, frame);
        var reader = new Outlines(new Dictionary<int, PolyTreeD> { [1] = Rectangle(0, 1200, 0, 3000), [10] = frame });
        var outline = new TeklaDrawingStructuralOutlineApi(new Roles(Part(1, "3"), Part(10, "0", false)), reader)
            .Get(7, referenceZones: ["0"]);
        var context = Context(outline);
        var answer = context.Query("chainDetails", ruleSet: "panel");
        var overall = Chain(answer, "Bottom", "overall");
        Assert.Equal(1350, overall.GetProperty("segments")[0].GetDouble());
        Assert.Equal(3100, Chain(answer, "Right", "overall").GetProperty("segments")[0].GetDouble());
        var ids = overall.GetProperty("pointIds").EnumerateArray().Select(id => id.GetString()!).ToArray();
        var resolved = context.ResolvePointIds(ids, "horizontal-down");
        Assert.Equal(new[] { -50d, 1300 }, resolved.Where((_, index) => index % 3 == 0).Select(value => Math.Round(value, 6)));
        Assert.Equal(3050, Math.Round(resolved[4], 6));
        double[]? written = null;
        var provider = new ViewDimensionContextProvider(() => "drawing", (_, _) => context, () => { },
            write: (request, _) => { written = request.Points; return new() { Created = true }; },
            referenceRead: (_, _, _, _) => context);
        provider.Get(7, referenceZones: ["0"]);
        Assert.True(provider.Create(new CreateDimensionRequest { ViewId = 7, ContextId = context.ContextId,
            PointIds = ids, Direction = "horizontal-down", PaperGapMm = 1, RuleSet = "panel" }).Created);
        Assert.Equal(resolved, written);
        Assert.Equal(1, reader.Reads);
    }

    [Fact]
    public void SingleChainTransportCarriesExplicitPlacementScopeAndAcceptsLegacyArguments()
    {
        string[] args = ["create_dimension", "7", "", "horizontal-down", "", "standard", "1", "", "",
            "ctx_fixture", "[\"p0001\",\"p0002\"]", "1", " PANEL "];
        var parsed = DrawingCommandParsers.ParseCreateDimensionRequest(args);
        Assert.True(parsed.IsValid);
        Assert.Equal("panel", parsed.Request.RuleSet);
        Assert.True(DrawingCommandParsers.ParseCreateDimensionRequest(args.Take(12).ToArray()).IsValid);
        args[12] = "unknown";
        Assert.False(DrawingCommandParsers.ParseCreateDimensionRequest(args).IsValid);
    }

    [Fact]
    public void ReferenceExclusionsDropJunkFromTheReferenceLayerIndependentlyOfThePanelExclusions()
    {
        // The same frame (zone 0) can itself carry junk (insulation stapled inside it) that must
        // still drop out of the reference polygon - but through its own filter, not the panel's.
        var parts = new[] { Part(1, "3", prefix: "S"), Part(10, "0", included: false, prefix: "T"),
            Part(11, "0", included: false, prefix: "INS") };
        var trees = new Dictionary<int, PolyTreeD> {
            [1] = Rectangle(0, 1200, 0, 3000), [10] = Rectangle(-50, 1250, -50, 3050),
            [11] = Rectangle(-50, 1250, -50, 3050) };
        var withoutReferenceExclusions = new TeklaDrawingStructuralOutlineApi(new Roles(parts), new Outlines(trees))
            .Get(7, referenceZones: ["0"], exclusions: [PartExclusionRule.ByPrefix("T")]);
        Assert.Equal(new[] { 10, 11 }, withoutReferenceExclusions.ReferenceZone!.ModelIds.OrderBy(id => id));
        var withReferenceExclusions = new TeklaDrawingStructuralOutlineApi(new Roles(parts), new Outlines(trees))
            .Get(7, referenceZones: ["0"], exclusions: [PartExclusionRule.ByPrefix("T")],
                referenceExclusions: [PartExclusionRule.ByPrefix("INS")]);
        Assert.Equal(new[] { 10 }, withReferenceExclusions.ReferenceZone!.ModelIds);
    }

    [Fact]
    public void SelectionUsesBothCallerExclusionsAndDoesNotAssumeNumericZones()
    {
        var selected = ReferenceZoneOutline.Select(new[] { Part(1, "datum"), Part(2, "datum", prefix: "INS"),
            Part(3, "datum", material: "SEALANT-XYZ"), Part(4, "other"), Part(5, "unread", prefix: "INS", zoneKnown: false) },
            ["datum"], [PartExclusionRule.ByPrefix("ins"), PartExclusionRule.ByMaterial("sealant")]);
        Assert.Null(selected.Error);
        Assert.Equal(new[] { 1 }, selected.ModelIds);
        var unknown = ReferenceZoneOutline.Select(new[] { Part(1, "datum"), Part(6, "", zoneKnown: false) }, ["datum"], []);
        Assert.NotNull(unknown.Error);
        Assert.Empty(unknown.ModelIds);
        Assert.Throws<ArgumentException>(() => new TimberPanelPartLocationSettings(3, [""]));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnreadPropertyRequiredOnlyByReferenceFilterSuppressesReferenceMix(bool materialFilter)
    {
        var frame = new PartRoleInView(10, "T10", materialFilter ? "T" : null,
            new PartRoleResult(PartRole.Excluded, "panel-prefix", "fixture"),
            material: null, zone: "0", partPrefixKnown: materialFilter, materialKnown: false);
        var reader = new Outlines(new Dictionary<int, PolyTreeD> {
            [1] = Rectangle(0, 1200, 0, 3000), [10] = Rectangle(-50, 1250, -50, 3050) });
        var referenceRule = materialFilter ? PartExclusionRule.ByMaterial("INS") : PartExclusionRule.ByPrefix("INS");
        var outline = new TeklaDrawingStructuralOutlineApi(new Roles(Part(1, "3"), frame), reader).Get(7,
            referenceZones: ["0"], referenceExclusions: [referenceRule]);
        Assert.True(outline.IsComplete);
        Assert.NotNull(outline.ReferenceZone!.Error);
        Assert.Empty(outline.ReferenceZone.ModelIds);
        Assert.Equal(new[] { 1 }, reader.Asked);
        var answer = Context(outline).Query("chainDetails", ruleSet: "panel");
        Assert.Equal("suppressed", answer.GetProperty("chainDiagnostics").GetProperty("referenceZone").GetProperty("status").GetString());
    }

    [Fact]
    public void ReferenceFilterUsesItsOwnReadRequirementsAndAcceptsKnownEmptyValues()
    {
        var part = new PartRoleInView(10, "T10", null, PartRoleResult.Unclassified,
            material: "", zone: "0", partPrefixKnown: false, materialKnown: true);
        var selected = ReferenceZoneOutline.Select([part], ["0"], [PartExclusionRule.ByMaterial("INS")]);
        Assert.Null(selected.Error);
        Assert.Equal(new[] { 10 }, selected.ModelIds);
        var excluded = ReferenceZoneOutline.Select([part], ["0"], [PartExclusionRule.ByPrefix("INS")]);
        Assert.NotNull(excluded.Error);
        var knownEmpty = new PartRoleInView(10, "T10", "", PartRoleResult.Unclassified, zone: "0", partPrefixKnown: true);
        Assert.Null(ReferenceZoneOutline.Select([knownEmpty], ["0"], [PartExclusionRule.ByPrefix("INS")]).Error);
    }

    [Fact]
    public void ReferenceScopeHasCanonicalCacheKeyAndTransportPreservesOpaqueZoneStrings()
    {
        var reads = 0;
        var provider = new ViewDimensionContextProvider(() => "drawing", (_, _) => throw new Exception("unused"), () => { },
            referenceRead: (id, rules, zones, referenceExclusions) => { reads++; return Context(Capture(zones.ToArray()).Outline); });
        var first = provider.Get(7, referenceZones: ["datum", "aux", "datum"]);
        Assert.Same(first, provider.Get(7, referenceZones: [" aux ", "datum"]));
        Assert.NotSame(first, provider.Get(7, referenceZones: ["datum"]));
        Assert.Equal(2, reads);
        var args = ModelTools.BuildViewDimensionContextArgs(7, "chainDetails", "all", "INS", "", false,
            "", "horizontal", null, "panel", "", "", ["datum", "-2"]);
        Assert.Equal(new[] { "-2", "datum" }, DrawingCommandParsers.ParseViewDimensionReferenceZones(args));
        Assert.Empty(DrawingCommandParsers.ParseViewDimensionReferenceZones(args.Take(13).ToArray()));
        Assert.Throws<ArgumentException>(() => ModelTools.BuildViewDimensionContextArgs(7, "chain", "all", "", "", false,
            "", "horizontal", null, "steel", "", "", ["datum"]));
    }
}
