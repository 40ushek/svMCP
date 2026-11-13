using System.Text.Json;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class BoltChainRuleTests
{
    [Fact]
    public void AllCreatesThePartChainAndOffersNoSeparateSteps()
    {
        var results = Decide(new BoltChainSettings(), Part(), Steps());
        var part = Assert.Single(results);
        Assert.Equal("bolt-part-10-groups-42-Bottom", part.Key);
        Assert.True(part.Create);
    }

    [Fact]
    public void InternalCreatesTheStepsAndOffersNoPartChain()
    {
        var results = Decide(new BoltChainSettings(mainPartInternal: BoltInternalDimensions.Internal), Part(), Steps());
        var steps = Assert.Single(results);
        Assert.Equal("bolt-42-X-0", steps.Key);
        Assert.True(steps.Create);
    }

    [Fact]
    public void NoneOffersNothingForThatPartRoleOnly()
    {
        var settings = new BoltChainSettings(mainPartInternal: BoltInternalDimensions.None);
        Assert.Empty(Decide(settings, Part(), Steps()));
        var secondary = Assert.Single(Decide(settings, Part(main: false), Steps(main: false)));
        Assert.True(secondary.Create);
    }

    [Fact]
    public void NecessaryIsListedButNotCreated()
    {
        var settings = new BoltChainSettings(secondaryPartInternal: BoltInternalDimensions.Necessary);
        var result = Assert.Single(Decide(settings, Part(main: false), Steps(main: false)));
        Assert.False(result.Create);
        Assert.Contains("Necessary", result.Reason);
    }

    [Theory]
    [InlineData("Plan", "Side")]
    [InlineData("Side", "Plan")]
    public void AClassSwitchedOffIsListedWithItsReason(string projections, string viewClass)
    {
        var result = Assert.Single(Decide(new BoltChainSettings(dimensionedProjections:
                (BoltDimensionedProjections)Enum.Parse(typeof(BoltDimensionedProjections), projections)),
            Part(viewClass: (BoltGroupViewClass)Enum.Parse(typeof(BoltGroupViewClass), viewClass))));
        Assert.False(result.Create);
        Assert.Contains("switched off", result.Reason);
    }

    [Fact]
    public void AnUnconfirmedEdgeRefusesThePartChainAndPlacesTheStepsOfItsGroup()
    {
        var results = Decide(new BoltChainSettings(), Part(edges: false), Steps(edges: false));
        Assert.Equal(2, results.Length);
        Assert.False(results[0].Create);
        Assert.Contains("not confirmed", results[0].Reason);
        Assert.True(results[1].Create);
        Assert.Equal("bolt-42-X-0", results[1].Key);
    }

    [Fact]
    public void TheStepsFallbackCannotBypassTheirOwnRefusals()
    {
        var collapsed = Steps(edges: false, points: 1);
        var blocked = Steps(edges: false, refusal: "restriction evidence does not admit every source", key: "bolt-42-X-1");
        var results = Decide(new BoltChainSettings(), Part(edges: false), collapsed, blocked);
        Assert.All(results, result => Assert.False(result.Create));
        Assert.Contains(results, result => result.Key == "bolt-42-X-0" && result.Reason == "fewer than two distinct points");
    }

    [Fact]
    public void OnASectionTheStepsDoNotDependOnTheEdgeSearch()
    {
        var results = Decide(new BoltChainSettings(), Part(edges: false, refusal: "axis ray aligns with a contour vertex"), Steps(edges: false));
        Assert.Equal(2, results.Length);
        Assert.Equal("axis ray aligns with a contour vertex", results[0].Reason);
        Assert.True(results[1].Create);
        Assert.Equal("bolt-42-X-0", results[1].Key);
    }

    [Fact]
    public void AGroupOnSeveralSecondaryPartsGetsAnEdgeChainOnEachAndItsStepsOnce()
    {
        var group = Group(42, 20);
        group.PartToBeBoltedId = 10;
        group.PartToBoltToId = 20;
        group.OtherPartIds = [30];
        var contours = new Dictionary<int, IReadOnlyList<OutlineTreeNodeResult>> {
            [10] = Contours()[10], [20] = Contours()[10], [30] = Contours()[10]
        };
        var candidates = BoltChainCandidates.Build(Snapshot(group), [10, 20, 30], [10], contours, _ => true, new BoltChainSettings());
        var parts = candidates.Where(c => c.Kind == BoltChainCandidate.PartKind && c.Axis == 0).Select(c => c.PartId).ToArray();
        Assert.Equal(new int?[] { 20, 30 }, parts);
        Assert.Single(candidates, c => c.Kind == BoltChainCandidate.InternalKind && c.Axis == 0);
        Assert.Equal(candidates.Count, candidates.Select(c => c.Key).Distinct().Count());
    }

    [Fact]
    public void OverlappingChainsOfTwoGroupsStandOnSeparateRows()
    {
        var context = DimensionChainPreviewTests.Context(76, bolts: SnapshotOf(Group(42, 20, -40), Group(43, 300, -40)));
        var bolt = context.Query("chain").GetProperty("chainPreview").EnumerateArray()
            .Single(row => row.GetProperty("side").GetString() == "Bolt").GetProperty("chains").EnumerateArray()
            .Where(chain => chain.GetProperty("direction").GetString() == "0,-1,0" && !chain.GetProperty("incomplete").GetBoolean())
            .ToDictionary(chain => chain.GetProperty("key").GetString()!, chain => chain.GetProperty("distance").GetDouble());
        Assert.Equal(116, bolt["bolt-part-10-groups-42-Bottom"], 6);
        // The second line is at least one gap beyond the first (and off the Bottom side rows, see below).
        Assert.True(bolt["bolt-part-10-groups-43-Bottom"] - bolt["bolt-part-10-groups-42-Bottom"] >= 80 - 1e-9);
    }

    [Theory]
    [InlineData("local")]
    [InlineData("side")]
    public void BoltLinesStayOffLocalLinesAndSideRows(string mode)
    {
        // local: two bolts on the plate 500..510 that has its own Local chain beside it;
        // side: two bolts near the beam end, whose edge chain stands next to the Right rows.
        var group = mode == "local"
            ? new BoltGroupGeometry { ModelId = 42, Shape = "BoltArray", FirstPosition = [505, 40, 0], SecondPosition = [505, 100, 0],
                Axis = [0, 0, 1], PartToBeBoltedId = 16,
                Positions = [new() { Index = 0, Point = [505, 40, 0] }, new() { Index = 1, Point = [505, 100, 0] }] }
            : Group(42, 900, -40);
        var snapshot = ViewBoltGeometrySnapshot.Capture(7, [16, 10], id => new() {
            Success = true, ViewId = 7, PartId = id, BoltGroups = id == group.PartToBeBoltedId ? [group] : []
        }, new DepthBox(-2000, -2000, -2000, 2000, 2000, 2000), point => point, true);
        var context = DimensionChainPreviewTests.Context(76, upperPlate: true, bolts: snapshot);
        var rows = context.Query("chain").GetProperty("chainPreview").EnumerateArray().ToArray();
        var gap = 8 * 10;
        var taken = new List<(double Nx, double Ny, double Line, double From, double To)>();
        foreach (var chain in rows.Where(row => row.GetProperty("side").GetString() is "Local" or "Inclined")
                     .SelectMany(row => row.GetProperty("chains").EnumerateArray()))
            taken.Add(Line(chain));
        var extent = (MinX: -20d, MaxX: 1010d, MinY: -170d, MaxY: 170d);
        foreach (var row in rows.Where(row => row.GetProperty("side").GetString() is "Top" or "Bottom" or "Left" or "Right"))
        {
            var count = row.GetProperty("chains").EnumerateArray().Count(chain => !chain.GetProperty("incomplete").GetBoolean());
            for (var r = 1; r <= count; r++)
                taken.Add(row.GetProperty("side").GetString() switch {
                    "Top" => (0, 1, extent.MaxY + r * gap, double.MinValue, double.MaxValue),
                    "Bottom" => (0, -1, -extent.MinY + r * gap, double.MinValue, double.MaxValue),
                    "Left" => (-1, 0, -extent.MinX + r * gap, double.MinValue, double.MaxValue),
                    _ => (1, 0, extent.MaxX + r * gap, double.MinValue, double.MaxValue)
                });
        }
        var bolts = rows.Single(row => row.GetProperty("side").GetString() == "Bolt").GetProperty("chains").EnumerateArray()
            .Where(chain => !chain.GetProperty("incomplete").GetBoolean()).Select(Line).ToArray();
        Assert.NotEmpty(bolts);
        foreach (var bolt in bolts)
            Assert.DoesNotContain(taken, other => other.Nx == bolt.Nx && other.Ny == bolt.Ny
                && Math.Abs(other.Line - bolt.Line) < gap && other.From <= bolt.To && bolt.From <= other.To);
    }

    private static (double Nx, double Ny, double Line, double From, double To) Line(JsonElement chain)
    {
        var n = chain.GetProperty("direction").GetString()!.Split(',').Select(value =>
            double.Parse(value, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        var points = chain.GetProperty("points").EnumerateArray().Select(p => (X: p[0].GetDouble(), Y: p[1].GetDouble())).ToArray();
        var along = points.Select(p => p.X * -n[1] + p.Y * n[0]).ToArray();
        return (n[0], n[1], points[0].X * n[0] + points[0].Y * n[1] + chain.GetProperty("distance").GetDouble(), along.Min(), along.Max());
    }

    [Fact]
    public void AnIncompleteReadOfNoGroupAtAllStillShowsTheBoltRow()
    {
        var snapshot = ViewBoltGeometrySnapshot.Capture(7, [10], id =>
            new() { ViewId = 7, PartId = id, Error = "read failed" },
            new DepthBox(-1000, -1000, -1000, 1000, 1000, 1000), point => point, true);
        var row = DimensionChainPreviewTests.Context(76, bolts: snapshot).Query("chain").GetProperty("chainPreview")
            .EnumerateArray().Single(item => item.GetProperty("side").GetString() == "Bolt");
        Assert.Contains("unread parts 10", row.GetProperty("readIncomplete").GetString());
        Assert.Empty(row.GetProperty("chains").EnumerateArray());
    }

    [Fact]
    public void AnIncompleteBoltReadCreatesNoBoltChainAndSaysWhy()
    {
        var snapshot = ViewBoltGeometrySnapshot.Capture(7, [10, 11], id => id == 11
            ? new() { ViewId = 7, PartId = 11, Error = "read failed" }
            : new() { Success = true, ViewId = 7, PartId = id, BoltGroups = [Group(42, 20, -40)] },
            new DepthBox(-1000, -1000, -1000, 1000, 1000, 1000), point => point, true);
        var context = DimensionChainPreviewTests.Context(76, bolts: snapshot);
        var row = context.Query("chain").GetProperty("chainPreview").EnumerateArray()
            .Single(item => item.GetProperty("side").GetString() == "Bolt");
        Assert.Contains("unread parts 11", row.GetProperty("readIncomplete").GetString());
        Assert.All(row.GetProperty("chains").EnumerateArray(), chain => {
            Assert.True(chain.GetProperty("incomplete").GetBoolean());
            Assert.Contains("read incompletely", chain.GetProperty("incompleteReason").GetString());
        });
        var writes = new List<CreateDimensionRequest>();
        Assert.Throws<ArgumentException>(() => Provider(context, writes).CreateBatch(new CreateDimensionsBatchRequest {
            ViewId = 7, ContextId = context.ContextId, RuleSet = "steel",
            Chains = [new() { Preview = "bolt-part-10-groups-42-Bottom" }]
        }));
        Assert.Empty(writes);
    }

    [Fact]
    public void ARefusedGroupIsAlwaysListed()
    {
        var group = new BoltChainCandidate("bolt-42", BoltChainCandidate.GroupKind, 42, 10, true, BoltGroupViewClass.Skewed,
            0, [], [], "bolt group is skewed to the view; no chain is built for it yet", true, new DimensionDirection(0, -1), 0, new object());
        var result = Assert.Single(Decide(new BoltChainSettings(), group));
        Assert.False(result.Create);
        Assert.Contains("skewed", result.Reason);
    }

    [Fact]
    public void BuiltChainsFromTwoGroupsOnOnePartStaySeparateAndKeepTheirKeys()
    {
        var snapshot = Snapshot(Group(21, 20), Group(22, 120));
        var forward = Keys(BoltChainCandidates.Build(snapshot, [10], [10], Contours(), _ => true, new BoltChainSettings()));
        var reversed = Keys(BoltChainCandidates.Build(Snapshot(Group(22, 120), Group(21, 20)), [10], [10], Contours(), _ => true, new BoltChainSettings()));
        Assert.Equal(forward, reversed);
        Assert.Contains("bolt-part-10-groups-21-Bottom", forward);
        Assert.Contains("bolt-part-10-groups-22-Bottom", forward);
        var candidates = BoltChainCandidates.Build(snapshot, [10], [10], Contours(), _ => true, new BoltChainSettings());
        var first = candidates.Single(c => c.Key == "bolt-part-10-groups-21-Bottom");
        // Edge - two bolts of group 21 - edge: no bolt of group 22 and no span between the groups.
        Assert.Equal(new[] { 20d, 60d, 180d }, first.Segments);
        Assert.Equal(10, first.BaseOffset, 6);
    }

    [Fact]
    public void ASideGroupIsMeasuredOnlyAcrossItsBoltAxis()
    {
        var group = Group(42, 20);
        group.Axis = [0, 1, 0];
        group.SecondPosition = [20, 10, 40];
        group.Positions = [new() { Index = 0, Point = [20, 10, 0] }, new() { Index = 1, Point = [20, 10, 40] },
            new() { Index = 2, Point = [80, 10, 0] }, new() { Index = 3, Point = [80, 10, 40] }];
        var candidates = BoltChainCandidates.Build(Snapshot(group), [10], [10], Contours(), _ => false, new BoltChainSettings());
        Assert.All(candidates, candidate => Assert.Equal(0, candidate.Axis));
        var steps = candidates.Single(c => c.Kind == BoltChainCandidate.InternalKind);
        Assert.Equal(new[] { 60d }, steps.Segments);
        Assert.All(candidates, candidate => Assert.Equal(BoltGroupViewClass.Side, candidate.ViewClass));
    }

    [Fact]
    public void ContextOffersABoltRowAndTheBatchWritesItsChain()
    {
        var context = DimensionChainPreviewTests.Context(76, bolts: SnapshotOf(Group(42, 20, -40)));
        var rows = context.Query("chain").GetProperty("chainPreview").EnumerateArray().ToArray();
        var bolt = rows.Single(row => row.GetProperty("side").GetString() == "Bolt").GetProperty("chains").EnumerateArray().ToArray();
        var part = bolt.Single(chain => chain.GetProperty("key").GetString() == "bolt-part-10-groups-42-Bottom");
        Assert.False(part.GetProperty("incomplete").GetBoolean());
        Assert.Equal("bolt", part.GetProperty("kind").GetString());
        Assert.Equal("Plan", part.GetProperty("viewClass").GetString());
        // The beam runs 0..1000 at y -76..76; the bolts at y -40 stand nearer its bottom edge.
        Assert.Equal(new[] { 20d, 60d, 920d }, part.GetProperty("segments").EnumerateArray().Select(v => v.GetDouble()));
        Assert.Equal("0,-1,0", part.GetProperty("direction").GetString());
        Assert.Equal(8 * 10 + 36, part.GetProperty("distance").GetDouble(), 6);
        Assert.DoesNotContain(DimensionChainRepeats.FromPreview(context.Query("chain")), chain => chain.Side == "Bolt");

        var writes = new List<CreateDimensionRequest>();
        var provider = Provider(context, writes);
        provider.CreateBatch(new CreateDimensionsBatchRequest {
            ViewId = 7, ContextId = context.ContextId, RuleSet = "steel",
            Chains = [new() { Preview = "bolt-part-10-groups-42-Bottom" }]
        });
        var write = Assert.Single(writes);
        Assert.Equal(12, write.Points.Length);
        Assert.Equal(116, write.Distance!.Value, 6);
    }

    [Fact]
    public void ARefusedBoltKeyRejectsTheWholeBatchBeforeAnyWrite()
    {
        var skewed = Group(43, 200, -40);
        skewed.Axis = [0, Math.Sin(Math.PI / 6), Math.Cos(Math.PI / 6)];
        var context = DimensionChainPreviewTests.Context(76, bolts: SnapshotOf(Group(42, 20, -40), skewed));
        var refused = context.Query("chain").GetProperty("chainPreview").EnumerateArray()
            .Single(row => row.GetProperty("side").GetString() == "Bolt").GetProperty("chains").EnumerateArray()
            .Single(chain => chain.GetProperty("key").GetString() == "bolt-43");
        Assert.True(refused.GetProperty("incomplete").GetBoolean());
        Assert.Contains("skewed", refused.GetProperty("incompleteReason").GetString());

        var writes = new List<CreateDimensionRequest>();
        var provider = Provider(context, writes);
        var error = Assert.Throws<ArgumentException>(() => provider.CreateBatch(new CreateDimensionsBatchRequest {
            ViewId = 7, ContextId = context.ContextId, RuleSet = "steel",
            Chains = [new() { Preview = "bolt-part-10-groups-42-Bottom" }, new() { Preview = "bolt-43" }]
        }));
        Assert.Contains("not to be created", error.Message);
        Assert.Empty(writes);
    }

    private static ViewDimensionContextProvider Provider(ViewDimensionContext context, List<CreateDimensionRequest> writes)
    {
        var provider = new ViewDimensionContextProvider(() => "drawing-a", (_, _) => context, () => { },
            (request, _) => { writes.Add(request); return new() { Created = true, DimensionId = 101 }; },
            readDimensions: _ => new GetDimensionsResult(), validateAttributes: _ => { });
        provider.Get(7);
        return provider;
    }

    private static (string Key, bool Create, string? Reason)[] Decide(BoltChainSettings settings, params BoltChainCandidate[] candidates) =>
        new DimensionRuleSet(new BoltChainRule(settings))
            .Calculate(new DimensionRuleContext([], null, new BoltChainInput(candidates))).Results
            .Select(result => ((string)result.Evidence["key"], (bool)result.Evidence["create"], result.Note)).ToArray();

    private static BoltChainCandidate Part(bool main = true, BoltGroupViewClass viewClass = BoltGroupViewClass.Plan,
        bool edges = true, string? refusal = null) =>
        new("bolt-part-10-groups-42-Bottom", BoltChainCandidate.PartKind, 42, 10, main, viewClass, 0,
            Points(4), [10, 60, 10], refusal, edges, new DimensionDirection(0, -1), 20, new object());

    private static BoltChainCandidate Steps(bool main = true, bool edges = true, int points = 2, string? refusal = null,
        string key = "bolt-42-X-0") =>
        new(key, BoltChainCandidate.InternalKind, 42, 10, main, BoltGroupViewClass.Plan, 0,
            Points(points), [60], refusal, edges, new DimensionDirection(0, -1), 20, new object());

    private static DimensionRulePoint[] Points(int count) => Enumerable.Range(0, count)
        .Select(i => new DimensionRulePoint("p" + i, i * 10, 0, [])).ToArray();

    private static BoltGroupGeometry Group(int id, double x, double y = 10) => new() {
        ModelId = id, Shape = "BoltArray", FirstPosition = [x, y, 0], SecondPosition = [x + 60, y, 0],
        Axis = [0, 0, 1], PartToBeBoltedId = 10,
        Positions = [new() { Index = 0, Point = [x, y, 0] }, new() { Index = 1, Point = [x + 60, y, 0] }]
    };

    private static ViewBoltGeometrySnapshot SnapshotOf(params BoltGroupGeometry[] groups) =>
        ViewBoltGeometrySnapshot.Capture(7, [10], id => new() {
            Success = true, ViewId = 7, PartId = id, BoltGroups = groups.ToList()
        }, new DepthBox(-1000, -1000, -1000, 1000, 1000, 1000), point => point, true);

    private static JsonElement Snapshot(params BoltGroupGeometry[] groups) => SnapshotOf(groups).Answer;

    private static string[] Keys(IEnumerable<BoltChainCandidate> candidates) =>
        candidates.Select(candidate => candidate.Key).OrderBy(key => key, StringComparer.Ordinal).ToArray();

    private static Dictionary<int, IReadOnlyList<OutlineTreeNodeResult>> Contours() => new() {
        [10] = [new OutlineTreeNodeResult { Polygon = [[0, 0], [260, 0], [260, 340], [0, 340]] }]
    };
}
