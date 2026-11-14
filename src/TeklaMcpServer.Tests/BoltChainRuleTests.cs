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
    public void AnUnconfirmedEdgeRefusesThePartChainAndPlacesNoStepsUnderAll()
    {
        var result = Assert.Single(Decide(new BoltChainSettings(), Part(edges: false), Steps(edges: false)));
        Assert.False(result.Create);
        Assert.Contains("not confirmed", result.Reason);
    }

    [Fact]
    public void ARefusedEdgeChainPlacesNoStepsUnderAll()
    {
        var result = Assert.Single(Decide(new BoltChainSettings(),
            Part(refusal: "axis ray aligns with a contour vertex"), Steps()));
        Assert.Equal("axis ray aligns with a contour vertex", result.Reason);
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
        var candidates = BoltChainCandidates.Build(Snapshot(group), [10, 20, 30], [10], contours, (_, _, _) => true, new BoltChainSettings());
        var parts = candidates.Where(c => c.Kind == BoltChainCandidate.PartKind && c.Axis == 0).Select(c => c.PartId).ToArray();
        Assert.Equal(new int?[] { 20, 30 }, parts);
        Assert.Single(candidates, c => c.Kind == BoltChainCandidate.InternalKind && c.Axis == 0);
        Assert.Equal(candidates.Count, candidates.Select(c => c.Key).Distinct().Count());
    }

    [Fact]
    public void OverlappingChainsOfTwoGroupsStandOnSeparateRows()
    {
        var context = DimensionChainPreviewTests.Context(76, bolts: SnapshotOf(Group(42, 20, -40), Sized(Group(43, 300, -40), 20)));
        var bolt = context.Query("chain").GetProperty("chainPreview").EnumerateArray()
            .Single(row => row.GetProperty("side").GetString() == "Bolt").GetProperty("chains").EnumerateArray()
            .Where(chain => chain.GetProperty("direction").GetString() == "0,-1,0" && !chain.GetProperty("incomplete").GetBoolean())
            .ToDictionary(chain => chain.GetProperty("key").GetString()!, chain => chain.GetProperty("distance").GetDouble());
        Assert.Equal(80, bolt["bolt-part-10-groups-42-Bottom"], 6);
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
    public void OnASectionAnEdgeTheSectionChainUsesIsConfirmed()
    {
        // Bolts on the end plate -130..130; the section chain locates the plate by both edges.
        var group = new BoltGroupGeometry { ModelId = 42, Shape = "BoltArray", FirstPosition = [-100, 150, 0],
            SecondPosition = [100, 150, 0], Axis = [0, 0, 1], PartToBeBoltedId = 11,
            Positions = [new() { Index = 0, Point = [-100, 150, 0] }, new() { Index = 1, Point = [100, 150, 0] }] };
        var snapshot = ViewBoltGeometrySnapshot.Capture(7, [11], id => new() {
            Success = true, ViewId = 7, PartId = id, BoltGroups = [group]
        }, new DepthBox(-2000, -2000, -2000, 2000, 2000, 2000), point => point, true);
        var answer = DimensionChainPreviewTests.ISectionContext(endPlate: true, bolts: snapshot).Query("chain");
        var bolt = answer.GetProperty("chainPreview").EnumerateArray()
            .Single(row => row.GetProperty("side").GetString() == "Bolt").GetProperty("chains").EnumerateArray().ToArray();
        var edge = bolt.Single(chain => chain.GetProperty("key").GetString() == "bolt-part-11-groups-42-Top");
        Assert.False(edge.GetProperty("incomplete").GetBoolean(), answer.GetRawText());
        Assert.Equal(new[] { 30d, 200d, 30d }, edge.GetProperty("segments").EnumerateArray().Select(v => v.GetDouble()));
        Assert.DoesNotContain(bolt, chain => chain.GetProperty("key").GetString() == "bolt-42-X-0");
    }

    [Fact]
    public void OnASectionAnEdgeNoPartChainUsesGivesNoBoltDimension()
    {
        var group = Group(42, 20, -40);
        var candidates = BoltChainCandidates.Build(Snapshot(group), [10], [10], Contours(),
            (partId, axis, points) => false, new BoltChainSettings());
        Assert.All(candidates.Where(c => c.Kind != BoltChainCandidate.GroupKind), c => Assert.False(c.EdgeConfirmed));
        var results = new DimensionRuleSet(new BoltChainRule(new BoltChainSettings()))
            .Calculate(new DimensionRuleContext([], null, new BoltChainInput(candidates))).Results;
        Assert.DoesNotContain(results, r => (bool)r.Evidence["create"]);
        Assert.Contains(results, r => ((string)r.Evidence["key"]).StartsWith("bolt-part-10-groups-42-"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SectionEdgeDecisionDoesNotDependOnAnswerDetailOrQueryOrder(bool detailedFirst)
    {
        // The compact section omits the web thickness points; the underlying profile still checks them.
        var group = Group(42, -2, 0);
        group.SecondPosition = [2, 0, 0];
        group.Positions[1].Point = [2, 0, 0];
        var context = DimensionChainPreviewTests.ISectionContext(bolts: SnapshotOf(group));
        var questions = detailedFirst ? new[] { "chainDetails", "chain" } : new[] { "chain", "chainDetails" };
        foreach (var question in questions)
        {
            var chain = BoltChain(context.Query(question), "bolt-part-10-groups-42-Top");
            Assert.False(chain.GetProperty("incomplete").GetBoolean());
            Assert.Equal(new[] { 8d, 4d, 8d }, chain.GetProperty("segments").EnumerateArray().Select(value => value.GetDouble()));
        }
    }

    [Fact]
    public void SectionEdgesOfAnotherPartCannotConfirmAnInsetPlate()
    {
        // The plate and the main profile both span -80..80, but the plate is located by the
        // profile and contributes no X-edge supports of its own to the section's chains.
        var group = Group(42, -40, 0);
        group.PartToBeBoltedId = 15;
        group.SecondPosition = [40, 0, 0];
        group.Positions[1].Point = [40, 0, 0];
        var context = DimensionChainPreviewTests.ISectionContext(insetPlateDepth: -100, bolts: SnapshotOf(group));
        foreach (var question in new[] { "chain", "chainDetails" })
        {
            var chain = BoltChain(context.Query(question), "bolt-part-15-groups-42-Top");
            Assert.True(chain.GetProperty("incomplete").GetBoolean());
            Assert.Equal("no part edge is confirmed on this view", chain.GetProperty("incompleteReason").GetString());
        }
    }

    [Fact]
    public void ASlantedEdgeAtTheBoltGivesWayToTheCornerOfTheFacingEdge()
    {
        // A fin plate: top edge -20..130 at y 296, right edge square at x 130, slanted edge down to
        // the tip at (-130, 130). Two bolts one above the other at x 90.
        var group = new BoltGroupGeometry { ModelId = 42, Shape = "BoltArray", FirstPosition = [90, 171, 0],
            SecondPosition = [90, 261, 0], Axis = [0, 0, 1], PartToBeBoltedId = 11,
            Positions = [new() { Index = 0, Point = [90, 171, 0] }, new() { Index = 1, Point = [90, 261, 0] }] };
        var contours = new Dictionary<int, IReadOnlyList<OutlineTreeNodeResult>> {
            [11] = [new OutlineTreeNodeResult { Polygon = [[-130, 130], [130, 130], [130, 296], [-20, 296]] }]
        };
        var snapshot = ViewBoltGeometrySnapshot.Capture(7, [11], id => new() {
            Success = true, ViewId = 7, PartId = id, BoltGroups = [group]
        }, new DepthBox(-1000, -1000, -1000, 1000, 1000, 1000), point => point, true).Answer;
        var candidates = BoltChainCandidates.Build(snapshot, [11], [10], contours, (_, _, _) => true, new BoltChainSettings());
        var top = candidates.Single(c => c.Key == "bolt-part-11-groups-42-Top");
        Assert.Null(top.BuiltRefusal);
        Assert.Equal(new[] { 110d, 40d }, top.Segments);
        Assert.Equal(new[] { -20d, 296d }, new[] { top.Points[0].X, top.Points[0].Y });

        // An end the view does not confirm is dropped: bolts - edge.
        var oneEnd = BoltChainCandidates.Build(snapshot, [11], [10], contours,
            (_, _, end) => end[0] > 0, new BoltChainSettings()).Single(c => c.Key == "bolt-part-11-groups-42-Top");
        Assert.Equal(new[] { 40d }, oneEnd.Segments);
    }

    [Theory]
    [InlineData("outside", "outside")]
    [InlineData("split", "different outer contours")]
    [InlineData("rows", "outside")]
    public void BoltsMustStandInsideOneContourWhateverEndsAreFound(string mode, string reason)
    {
        // outside: a part 0..260 and a bolt at x -20; split: two contours 0..100 and 200..300, one bolt
        // on each; rows: a part 0..100 x 0..100 with bolts at x 20, 80 and y 10, 20, 110 - the row at
        // y 110 lies outside, and merging the projection must not hide it on either axis.
        var (xs, ys) = mode switch {
            "outside" => (new[] { -20d, 80 }, new[] { 50d }),
            "split" => (new[] { 20d, 280 }, new[] { 50d }),
            _ => (new[] { 20d, 80 }, new[] { 10d, 20, 110 })
        };
        var positions = ys.SelectMany(y => xs.Select(x => new[] { x, y, 0d }))
            .Select((point, index) => new BoltPointGeometry { Index = index, Point = point }).ToList();
        var group = new BoltGroupGeometry { ModelId = 42, Shape = "BoltArray", FirstPosition = positions[0].Point.ToArray(),
            SecondPosition = positions[1].Point.ToArray(), Axis = [0, 0, 1], PartToBeBoltedId = 10, Positions = positions };
        IReadOnlyList<OutlineTreeNodeResult> outline = mode switch {
            "outside" => [new OutlineTreeNodeResult { Polygon = [[0, 0], [260, 0], [260, 100], [0, 100]] }],
            "split" => [new OutlineTreeNodeResult { Polygon = [[0, 0], [100, 0], [100, 100], [0, 100]] },
                new OutlineTreeNodeResult { Polygon = [[200, 0], [300, 0], [300, 100], [200, 100]] }],
            _ => [new OutlineTreeNodeResult { Polygon = [[0, 0], [100, 0], [100, 100], [0, 100]] }]
        };
        var candidates = BoltChainCandidates.Build(Snapshot(group), [10], [10],
            new Dictionary<int, IReadOnlyList<OutlineTreeNodeResult>> { [10] = outline }, (_, _, _) => true, new BoltChainSettings());
        var chains = candidates.Where(c => c.Kind == BoltChainCandidate.PartKind).ToArray();
        Assert.Equal(2, chains.Length);
        Assert.All(chains, chain => {
            Assert.Contains(reason, chain.BuiltRefusal);
            Assert.Empty(chain.Points);
        });
    }

    [Fact]
    public void TheEdgeCornerComesFromTheContourTheBoltsStandIn()
    {
        // Bolts in the contour 0..100; a neighbour contour -10..-0.05 runs from y -200 to 200, within
        // the tolerance of the edge x 0. The corner must be (0, 100) of the bolts' own contour.
        var group = new BoltGroupGeometry { ModelId = 42, Shape = "BoltArray", FirstPosition = [20, 50, 0],
            SecondPosition = [80, 50, 0], Axis = [0, 0, 1], PartToBeBoltedId = 10,
            Positions = [new() { Index = 0, Point = [20, 50, 0] }, new() { Index = 1, Point = [80, 50, 0] }] };
        IReadOnlyList<OutlineTreeNodeResult> outline = [
            new OutlineTreeNodeResult { Polygon = [[0, 0], [100, 0], [100, 100], [0, 100]] },
            new OutlineTreeNodeResult { Polygon = [[-10, -200], [-0.05, -200], [-0.05, 200], [-10, 200]] }];
        var candidates = BoltChainCandidates.Build(Snapshot(group), [10], [10],
            new Dictionary<int, IReadOnlyList<OutlineTreeNodeResult>> { [10] = outline }, (_, _, _) => true,
            new BoltChainSettings(), new DimensionCoordinateSettings(0.1));
        var top = candidates.Single(c => c.Kind == BoltChainCandidate.PartKind && c.Axis == 0);
        Assert.Null(top.BuiltRefusal);
        Assert.Equal(new[] { 0d, 100d }, new[] { top.Points[0].X, top.Points[0].Y });
        Assert.Equal(new[] { 100d, 100d }, new[] { top.Points[top.Points.Count - 1].X, top.Points[top.Points.Count - 1].Y });
    }

    [Fact]
    public void AVertexAtTheBoltsHeightDoesNotLendTheFacingCorner()
    {
        // A plate 100 x 100 with both top corners cut from (0, 80) and (100, 80); bolts at the height
        // where the cuts start. The edge search ends ambiguous, not on a slanted edge: the ends of the
        // top edge (x 20 and 80) must not be taken, they would give 10 - 40 - 10.
        var group = Group(42, 30, 80);
        group.SecondPosition = [70, 80, 0];
        group.Positions = [new() { Index = 0, Point = [30, 80, 0] }, new() { Index = 1, Point = [70, 80, 0] }];
        IReadOnlyList<OutlineTreeNodeResult> outline = [new OutlineTreeNodeResult {
            Polygon = [[0, 0], [100, 0], [100, 80], [80, 100], [20, 100], [0, 80]] }];
        var top = BoltChainCandidates.Build(Snapshot(group), [10], [10],
            new Dictionary<int, IReadOnlyList<OutlineTreeNodeResult>> { [10] = outline }, (_, _, _) => true, new BoltChainSettings())
            .Single(c => c.Key == "bolt-part-10-groups-42-Top");
        Assert.Empty(top.Points);
        Assert.Contains("no part edge is confirmed", top.BuiltRefusal);
        Assert.Contains("aligns with a contour vertex", top.BuiltRefusal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.000001)]
    [InlineData(-0.000001)]
    [InlineData(100)]
    [InlineData(100.000001)]
    public void ASideGroupOnThePartFaceIsMeasuredAlongThatEdgeWhicheverSideRoundingFalls(double y)
    {
        // Seen from the side the centers lie in the face of the part, drawn as its edge y 0 or y 100.
        var group = Group(42, 20, y);
        group.Axis = [0, 1, 0];
        IReadOnlyList<OutlineTreeNodeResult> outline = [new OutlineTreeNodeResult { Polygon = [[0, 0], [100, 0], [100, 100], [0, 100]] }];
        var chain = BoltChainCandidates.Build(Snapshot(group), [10], [10],
            new Dictionary<int, IReadOnlyList<OutlineTreeNodeResult>> { [10] = outline }, (_, _, _) => true, new BoltChainSettings())
            .Single(c => c.Kind == BoltChainCandidate.PartKind);
        Assert.Null(chain.BuiltRefusal);
        Assert.Equal(new[] { 20d, 60d, 20d }, chain.Segments);
        Assert.Equal(new[] { 0d, 20, 80, 100 }, chain.Points.Select(point => point.X));
    }

    [Fact]
    public void ASideGroupWithCentersOnBothSidesOfTheEdgeLineIsMeasuredAlongIt()
    {
        // Tolerance 0.1; centers 0.09 below and 0.09 above the edge y 0: each lies on the edge,
        // though they stand 0.18 apart from one another.
        var group = Group(42, 20, -0.09);
        group.Axis = [0, 1, 0];
        group.Positions = [new() { Index = 0, Point = [20, -0.09, 0] }, new() { Index = 1, Point = [80, -0.09, 0] },
            new() { Index = 2, Point = [20, 0.09, 40] }, new() { Index = 3, Point = [80, 0.09, 40] }];
        IReadOnlyList<OutlineTreeNodeResult> outline = [new OutlineTreeNodeResult { Polygon = [[0, 0], [100, 0], [100, 100], [0, 100]] }];
        var chain = BoltChainCandidates.Build(Snapshot(group), [10], [10],
            new Dictionary<int, IReadOnlyList<OutlineTreeNodeResult>> { [10] = outline }, (_, _, _) => true,
            new BoltChainSettings(), new DimensionCoordinateSettings(0.1))
            .Single(c => c.Kind == BoltChainCandidate.PartKind);
        Assert.Null(chain.BuiltRefusal);
        Assert.Equal(new[] { 20d, 60d, 20d }, chain.Segments);
        Assert.Equal(new[] { 0d, 0d }, new[] { chain.Points[0].X, chain.Points[0].Y });
        Assert.Equal(new[] { 100d, 0d }, new[] { chain.Points[3].X, chain.Points[3].Y });
    }

    [Fact]
    public void ASideGroupOnANearlyLevelEdgeIsJudgedByItsDistanceToThatEdge()
    {
        // Tolerance 0.1; the edge runs (0, 0)..(100, 0.09). Both centers lie within 0.1 of the
        // segment, though 0.125 from a level line through the middle of it.
        var group = Group(42, 10, -0.08);
        group.Axis = [0, 1, 0];
        group.SecondPosition = [20, -0.08, 0];
        group.Positions = [new() { Index = 0, Point = [10, -0.08, 0] }, new() { Index = 1, Point = [20, -0.08, 0] }];
        IReadOnlyList<OutlineTreeNodeResult> outline = [new OutlineTreeNodeResult { Polygon = [[0, 0], [100, 0.09], [100, 100], [0, 100]] }];
        var chain = BoltChainCandidates.Build(Snapshot(group), [10], [10],
            new Dictionary<int, IReadOnlyList<OutlineTreeNodeResult>> { [10] = outline }, (_, _, _) => true,
            new BoltChainSettings(), new DimensionCoordinateSettings(0.1))
            .Single(c => c.Kind == BoltChainCandidate.PartKind);
        Assert.Null(chain.BuiltRefusal);
        Assert.Equal(new[] { 10d, 10d, 80d }, chain.Segments);
        Assert.Equal(new[] { 0d, 0d }, new[] { chain.Points[0].X, chain.Points[0].Y });
        Assert.Equal(new[] { 100d, 0.09 }, new[] { chain.Points[3].X, chain.Points[3].Y });
    }

    [Fact]
    public void APlanGroupOnThePartEdgeIsRefused()
    {
        IReadOnlyList<OutlineTreeNodeResult> outline = [new OutlineTreeNodeResult { Polygon = [[0, 0], [100, 0], [100, 100], [0, 100]] }];
        var chains = BoltChainCandidates.Build(Snapshot(Group(42, 20, 0.000001)), [10], [10],
            new Dictionary<int, IReadOnlyList<OutlineTreeNodeResult>> { [10] = outline }, (_, _, _) => true, new BoltChainSettings())
            .Where(c => c.Kind == BoltChainCandidate.PartKind).ToArray();
        Assert.Equal(2, chains.Length);
        Assert.All(chains, chain => {
            Assert.Contains("lies on the projected part edge", chain.BuiltRefusal);
            Assert.Empty(chain.Points);
        });
    }

    [Fact]
    public void TwoGroupsInTheSameHolesOfOnePartAreDimensionedOnce()
    {
        // Bolts and a hole group in the same two holes of part 10: one chain per side, not two.
        var bolts = Group(21, 20, 50);
        var holes = Group(22, 20, 50);
        holes.PartToBoltToId = 10;
        var candidates = BoltChainCandidates.Build(Snapshot(bolts, holes), [10], [10],
            new Dictionary<int, IReadOnlyList<OutlineTreeNodeResult>> { [10] = [new OutlineTreeNodeResult {
                Polygon = [[0, 0], [100, 0], [100, 100], [0, 100]] }] }, (_, _, _) => true, new BoltChainSettings());
        Assert.DoesNotContain(candidates, c => c.GroupId == 22 && c.Kind != BoltChainCandidate.GroupKind);
        Assert.Contains("as bolt group 21", candidates.Single(c => c.GroupId == 22).BuiltRefusal);
        Assert.Contains(candidates, c => c.GroupId == 21 && c.Kind == BoltChainCandidate.PartKind && c.BuiltRefusal == null);
    }

    private static JsonElement BoltChain(JsonElement answer, string key) => answer.GetProperty("chainPreview").EnumerateArray()
        .Single(row => row.GetProperty("side").GetString() == "Bolt").GetProperty("chains").EnumerateArray()
        .Single(chain => chain.GetProperty("key").GetString() == key);

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
        // Different bolts: the groups are not merged.
        var snapshot = Snapshot(Group(21, 20), Sized(Group(22, 120), 20));
        var forward = Keys(BoltChainCandidates.Build(snapshot, [10], [10], Contours(), (_, _, _) => true, new BoltChainSettings()));
        var reversed = Keys(BoltChainCandidates.Build(Snapshot(Sized(Group(22, 120), 20), Group(21, 20)), [10], [10], Contours(), (_, _, _) => true, new BoltChainSettings()));
        Assert.Equal(forward, reversed);
        Assert.Contains("bolt-part-10-groups-21-Bottom", forward);
        Assert.Contains("bolt-part-10-groups-22-Bottom", forward);
        var candidates = BoltChainCandidates.Build(snapshot, [10], [10], Contours(), (_, _, _) => true, new BoltChainSettings());
        var first = candidates.Single(c => c.Key == "bolt-part-10-groups-21-Bottom");
        // Edge - two bolts of group 21 - edge: no bolt of group 22 and no span between the groups.
        Assert.Equal(new[] { 20d, 60d, 180d }, first.Segments);
        // The first point is the edge corner nearest the line, so the line stands one gap from it.
        Assert.Equal(0, first.BaseOffset, 6);
        Assert.Equal(new[] { 0d, 0d }, new[] { first.Points[0].X, first.Points[0].Y });
    }

    [Fact]
    public void ASideGroupIsMeasuredOnlyAcrossItsBoltAxis()
    {
        var group = Group(42, 20);
        group.Axis = [0, 1, 0];
        group.SecondPosition = [20, 10, 40];
        group.Positions = [new() { Index = 0, Point = [20, 10, 0] }, new() { Index = 1, Point = [20, 10, 40] },
            new() { Index = 2, Point = [80, 10, 0] }, new() { Index = 3, Point = [80, 10, 40] }];
        var candidates = BoltChainCandidates.Build(Snapshot(group), [10], [10], Contours(), (_, _, _) => false, new BoltChainSettings());
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
        // The edge points stand at the corners of the beam bottom edge (y -76): the line one gap below.
        Assert.Equal(8 * 10, part.GetProperty("distance").GetDouble(), 6);
        Assert.Equal(-76, part.GetProperty("points")[0][1].GetDouble(), 6);
        Assert.DoesNotContain(DimensionChainRepeats.FromPreview(context.Query("chain")), chain => chain.Side == "Bolt");

        var writes = new List<CreateDimensionRequest>();
        var provider = Provider(context, writes);
        provider.CreateBatch(new CreateDimensionsBatchRequest {
            ViewId = 7, ContextId = context.ContextId, RuleSet = "steel",
            Chains = [new() { Preview = "bolt-part-10-groups-42-Bottom" }]
        });
        var write = Assert.Single(writes);
        Assert.Equal(12, write.Points.Length);
        Assert.Equal(80, write.Distance!.Value, 6);
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

    [Fact]
    public void ARepeatWithPointIdsRejectsTheWholeBatchBeforeAnyWrite()
    {
        // Top-overall is a repeat of Bottom-overall here; its row is already given to the bolt line.
        var group = new BoltGroupGeometry { ModelId = 42, Shape = "BoltArray", FirstPosition = [-15, 100, 0],
            SecondPosition = [-5, 100, 0], Axis = [0, 0, 1], PartToBeBoltedId = 11,
            Positions = [new() { Index = 0, Point = [-15, 100, 0] }, new() { Index = 1, Point = [-5, 100, 0] }] };
        var context = DimensionChainPreviewTests.Context(76, bolts: SnapshotOf(group));
        context.Query("chain,repeats", readSheet: () => []);
        var writes = new List<CreateDimensionRequest>();
        var provider = Provider(context, writes);
        var error = Assert.Throws<ArgumentException>(() => provider.CreateBatch(new CreateDimensionsBatchRequest {
            ViewId = 7, ContextId = context.ContextId, RuleSet = "steel",
            Chains = [new() { Preview = "bolt-part-11-groups-42-Top" }, new() { Preview = "Top-overall" }]
        }));
        Assert.Contains("not to be created", error.Message);
        Assert.Contains("Bottom-overall", error.Message);
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

    [Fact]
    public void GroupsWithTheSameHoleOnOnePartShareOneChain()
    {
        var snapshot = Snapshot(Group(21, 20), Group(22, 120));
        var candidates = BoltChainCandidates.Build(snapshot, [10], [10], Contours(), (_, _, _) => true, new BoltChainSettings());
        var chain = candidates.Single(c => c.Key == "bolt-part-10-groups-21-22-Bottom");
        Assert.Null(chain.BuiltRefusal);
        Assert.Equal(new[] { 20d, 60d, 40d, 60d, 80d }, chain.Segments);
        Assert.DoesNotContain(candidates, c => c.Kind == BoltChainCandidate.PartKind && c.GroupId == 22);
        Assert.Equal(new[] { 21, 22 }, JsonSerializer.SerializeToElement(chain.Holes).EnumerateArray()
            .Select(hole => hole.GetProperty("boltGroupId").GetInt32()));
    }

    [Theory]
    [InlineData("Separate", 2)]
    [InlineData("SameHole", 2)]
    [InlineData("SameBoltSize", 1)]
    public void TheMergeSettingDecidesWhichGroupsShareAChain(string merge, int bottomChains)
    {
        // Same bolt size, different holes (tolerance 2 and 3).
        var wide = Group(22, 120);
        wide.HoleTolerance = 3;
        var tight = Group(21, 20);
        tight.HoleTolerance = 2;
        var candidates = BoltChainCandidates.Build(Snapshot(tight, wide), [10], [10], Contours(), (_, _, _) => true,
            new BoltChainSettings(groupMerge: Enum.Parse<BoltGroupMerge>(merge)));
        Assert.Equal(bottomChains, candidates.Count(c => c.Kind == BoltChainCandidate.PartKind && c.Key.EndsWith("-Bottom")));
    }

    [Theory]
    [InlineData(20)]
    [InlineData(120)]
    public void AGroupOutsideTheViewDepthNeitherSuppressesNorJoinsAnAdmittedOne(double admittedX)
    {
        // Group 21 stands beyond the depth box; group 22 is admitted, in the same holes or elsewhere.
        var outside = Group(21, 20);
        outside.Positions.ForEach(position => position.Point[2] = 5000);
        var candidates = BoltChainCandidates.Build(Snapshot(outside, Group(22, admittedX)), [10], [10], Contours(),
            (_, _, _) => true, new BoltChainSettings());
        var admitted = candidates.Single(c => c.Key == "bolt-part-10-groups-22-Bottom");
        Assert.Null(admitted.BuiltRefusal);
        Assert.DoesNotContain(candidates, c => c.Key.Contains("groups-21-22"));
    }

    [Fact]
    public void SideGroupsInTheSameXyButMeasuredAlongDifferentAxesAreNotTwins()
    {
        var alongX = Group(21, 20, 50);
        alongX.Axis = [1, 0, 0];
        var alongY = Group(22, 20, 50);
        alongY.Axis = [0, 1, 0];
        var candidates = BoltChainCandidates.Build(Snapshot(alongX, alongY), [10], [10], Contours(),
            (_, _, _) => true, new BoltChainSettings());
        Assert.DoesNotContain(candidates, c => c.BuiltRefusal?.Contains("same projected bolt positions") == true);
        Assert.Contains(candidates, c => c.GroupId == 21 && c.Kind == BoltChainCandidate.PartKind && c.Axis == 1);
        Assert.Contains(candidates, c => c.GroupId == 22 && c.Kind == BoltChainCandidate.PartKind && c.Axis == 0);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public void ASlotCountsOnlyInThePartsWhoseHoleFlagIsOn(bool slotInPart10, int bottomChains)
    {
        // Group 22 carries a slot of 30; it acts on part 10 only when its hole flag is on there.
        var slotted = Group(22, 120);
        slotted.SlottedHoleY = 30;
        slotted.PartHoles = [new() { PartId = 10, Special = slotInPart10, HoleType = slotInPart10 ? "HOLE_TYPE_SLOTTED" : "Plain",
            SlottedHoleY = slotInPart10 ? 30 : 0 }];
        var plain = Group(21, 20);
        plain.PartHoles = [new() { PartId = 10, Special = false, HoleType = "Plain" }];
        var candidates = BoltChainCandidates.Build(Snapshot(plain, slotted), [10], [10], Contours(), (_, _, _) => true,
            new BoltChainSettings());
        Assert.Equal(bottomChains, candidates.Count(c => c.Kind == BoltChainCandidate.PartKind && c.Key.EndsWith("-Bottom")));
    }

    [Fact]
    public void GroupsMergeAcrossOnlyWithTheSameSpacing()
    {
        // Three pairs across the part at x 40, 120 and 200: rows at y 120/220, 120/220 and 125/215.
        // Along X they stand one beside the other: one chain. Across, the third spacing differs.
        BoltGroupGeometry Pair(int id, double x, double low, double high) => new() {
            ModelId = id, Shape = "BoltArray", FirstPosition = [x, low, 0], SecondPosition = [x, high, 0],
            Axis = [0, 0, 1], PartToBeBoltedId = 10,
            Positions = [new() { Index = 0, Point = [x, low, 0] }, new() { Index = 1, Point = [x, high, 0] }] };
        var candidates = BoltChainCandidates.Build(Snapshot(Pair(21, 40, 120, 220), Pair(22, 120, 120, 220), Pair(23, 200, 125, 215)),
            [10], [10], Contours(), (_, _, _) => true, new BoltChainSettings());
        var keys = candidates.Where(c => c.Kind == BoltChainCandidate.PartKind).Select(c => c.Key).ToArray();
        Assert.Contains(keys, key => key.StartsWith("bolt-part-10-groups-21-22-23-") && (key.EndsWith("-Bottom") || key.EndsWith("-Top")));
        Assert.Contains(keys, key => key.StartsWith("bolt-part-10-groups-21-22-") && !key.Contains("-23-") && (key.EndsWith("-Left") || key.EndsWith("-Right")));
        Assert.Contains(keys, key => key.StartsWith("bolt-part-10-groups-23-") && (key.EndsWith("-Left") || key.EndsWith("-Right")));
    }

    [Fact]
    public void ARepeatIsMarkedIncompleteAndKeepsNoRowForTheBoltLines()
    {
        // Bolts near the top of the tall end plate 11 (x -20..0, y -170..170): their line meets the
        // Top rows, Top-location and Top-overall. Top-overall repeats Bottom-overall on this view.
        var group = new BoltGroupGeometry { ModelId = 42, Shape = "BoltArray", FirstPosition = [-15, 100, 0],
            SecondPosition = [-5, 100, 0], Axis = [0, 0, 1], PartToBeBoltedId = 11,
            Positions = [new() { Index = 0, Point = [-15, 100, 0] }, new() { Index = 1, Point = [-5, 100, 0] }] };
        var context = DimensionChainPreviewTests.Context(76, bolts: SnapshotOf(group));
        double TopBolt(JsonElement answer) => answer.GetProperty("chainPreview").EnumerateArray()
            .Single(row => row.GetProperty("side").GetString() == "Bolt").GetProperty("chains").EnumerateArray()
            .Single(chain => chain.GetProperty("key").GetString() == "bolt-part-11-groups-42-Top").GetProperty("distance").GetDouble();
        var plain = TopBolt(context.Query("chain"));
        var answer = context.Query("chain,repeats", readSheet: () => []);
        var repeat = answer.GetProperty("chainPreview").EnumerateArray()
            .Single(row => row.GetProperty("side").GetString() == "Top").GetProperty("chains").EnumerateArray()
            .Single(chain => chain.GetProperty("key").GetString() == "Top-overall");
        Assert.True(repeat.GetProperty("incomplete").GetBoolean());
        Assert.Equal("sameView", repeat.GetProperty("repeat").GetString());
        Assert.Contains("Bottom-overall", repeat.GetProperty("incompleteReason").GetString());
        Assert.Contains(answer.GetProperty("repeats").EnumerateArray(), row => row.GetProperty("key").GetString() == "Top-overall");
        // Both Top chains repeat the Bottom ones here: no Top row is kept, the bolt line stands one gap
        // out instead of beyond two empty rows, and the batch reads the same answer.
        Assert.All(answer.GetProperty("chainPreview").EnumerateArray().Single(row => row.GetProperty("side").GetString() == "Top")
            .GetProperty("chains").EnumerateArray(), chain => Assert.True(chain.GetProperty("incomplete").GetBoolean()));
        Assert.Equal(240, plain, 6);
        Assert.Equal(80, TopBolt(answer), 6);
        Assert.Equal(TopBolt(answer), TopBolt(context.Query("chain", storedRepeats: true)), 6);
        // A plain chain read clears the repeats again.
        Assert.Equal(plain, TopBolt(context.Query("chain")), 6);
        Assert.Equal(plain, TopBolt(context.Query("chain", storedRepeats: true)), 6);
    }

    [Theory]
    [InlineData(-14, 14)]
    [InlineData(-200, 0)]
    // The plate's near edge exactly one gap past the beam end, where the line would stand: in the way.
    [InlineData(-94, 94)]
    public void APartPastTheBeamEndPushesTheLineOfTheBeamBoltsOut(double plateX, double baseOffset)
    {
        // Bolts at x 300 across a beam 0..1000 (y -80..80); an end plate 14 wide (y -85..85) stands
        // either at the beam end or 186 beyond it. The vertical chain stands on the Left, beside x 0.
        var group = new BoltGroupGeometry { ModelId = 42, Shape = "BoltArray", FirstPosition = [300, -45, 0],
            SecondPosition = [300, 45, 0], Axis = [0, 0, 1], PartToBeBoltedId = 10,
            Positions = [new() { Index = 0, Point = [300, -45, 0] }, new() { Index = 1, Point = [300, 45, 0] }] };
        var contours = new Dictionary<int, IReadOnlyList<OutlineTreeNodeResult>> {
            [10] = [new OutlineTreeNodeResult { Polygon = [[0, -80], [1000, -80], [1000, 80], [0, 80]] }],
            [11] = [new OutlineTreeNodeResult { Polygon = [[plateX, -85], [plateX + 14, -85], [plateX + 14, 85], [plateX, 85]] }] };
        var left = BoltChainCandidates.Build(Snapshot(group), [10], [10], contours, (_, _, _) => true,
            new BoltChainSettings(), lineGap: 80).Single(c => c.Key == "bolt-part-10-groups-42-Left");
        Assert.Null(left.BuiltRefusal);
        Assert.Equal(baseOffset, left.BaseOffset, 6);
    }

    [Fact]
    public void ARowTurnedInTheViewPlaneIsDimensionedAlongTheViewAxes()
    {
        // M.156 front view: two bolts in plan along a slanted plate edge, (20, 30) and (50, 60) in
        // the plate 0..100 x 0..100. No skewed group: edge - bolt - bolt - edge on both axes.
        var group = new BoltGroupGeometry { ModelId = 42, Shape = "BoltArray", FirstPosition = [20, 30, 0],
            SecondPosition = [50, 60, 0], Axis = [0, 0, 1], PartToBeBoltedId = 10,
            Positions = [new() { Index = 0, Point = [20, 30, 0] }, new() { Index = 1, Point = [50, 60, 0] }] };
        var candidates = BoltChainCandidates.Build(Snapshot(group), [10], [10],
            new Dictionary<int, IReadOnlyList<OutlineTreeNodeResult>> { [10] = [new OutlineTreeNodeResult {
                Polygon = [[0, 0], [100, 0], [100, 100], [0, 100]] }] }, (_, _, _) => true, new BoltChainSettings());
        Assert.DoesNotContain(candidates, c => c.Kind == BoltChainCandidate.GroupKind);
        var along = candidates.Single(c => c.Kind == BoltChainCandidate.PartKind && c.Axis == 0);
        var across = candidates.Single(c => c.Kind == BoltChainCandidate.PartKind && c.Axis == 1);
        Assert.Null(along.BuiltRefusal);
        Assert.Null(across.BuiltRefusal);
        Assert.Equal(new[] { 20d, 30, 50 }, along.Segments);
        Assert.Equal(new[] { 30d, 30, 40 }, across.Segments);
    }

    [Fact]
    public void ABeamUnderAFinPlateDoesNotPushThePlateBoltLineAcrossTheView()
    {
        // M.156 top view: a fin plate 800..950 standing on a beam 0..1000 (y -80..80), up to y 200;
        // bolts in the plate at x 830. The vertical chain stands beside the plate, not past the
        // beam's far end: the beam lies under the chain, it is not beside it.
        var group = new BoltGroupGeometry { ModelId = 42, Shape = "BoltArray", FirstPosition = [830, 100, 0],
            SecondPosition = [830, 150, 0], Axis = [0, 0, 1], PartToBeBoltedId = 11,
            Positions = [new() { Index = 0, Point = [830, 100, 0] }, new() { Index = 1, Point = [830, 150, 0] }] };
        var contours = new Dictionary<int, IReadOnlyList<OutlineTreeNodeResult>> {
            [10] = [new OutlineTreeNodeResult { Polygon = [[0, -80], [1000, -80], [1000, 80], [0, 80]] }],
            [11] = [new OutlineTreeNodeResult { Polygon = [[800, 0], [950, 0], [950, 200], [800, 200]] }] };
        var chain = BoltChainCandidates.Build(Snapshot(group), [10, 11], [10], contours, (_, _, _) => true,
            new BoltChainSettings(), lineGap: 80).Single(c => c.Key == "bolt-part-11-groups-42-Left");
        Assert.Null(chain.BuiltRefusal);
        Assert.Equal(0, chain.BaseOffset, 6);
    }

    [Fact]
    public void ASecondPartInTheWayIsClearedTooAfterTheFirst()
    {
        // Gap 10. The bolts' part spans y 0..10; a part in the way at y 15..30 moves the bound to 30;
        // the next one at y 25..100 starts before that bound but outside the part's own bound 10:
        // it is in the way as well, not under the chain. The line clears both.
        var group = new BoltGroupGeometry { ModelId = 42, Shape = "BoltArray", FirstPosition = [20, 5, 0],
            SecondPosition = [80, 5, 0], Axis = [0, 0, 1], PartToBeBoltedId = 10,
            Positions = [new() { Index = 0, Point = [20, 5, 0] }, new() { Index = 1, Point = [80, 5, 0] }] };
        var contours = new Dictionary<int, IReadOnlyList<OutlineTreeNodeResult>> {
            [10] = [new OutlineTreeNodeResult { Polygon = [[0, 0], [100, 0], [100, 10], [0, 10]] }],
            [11] = [new OutlineTreeNodeResult { Polygon = [[0, 15], [100, 15], [100, 30], [0, 30]] }],
            [12] = [new OutlineTreeNodeResult { Polygon = [[0, 25], [100, 25], [100, 100], [0, 100]] }] };
        var top = BoltChainCandidates.Build(Snapshot(group), [10], [10], contours, (_, _, _) => true,
            new BoltChainSettings(), lineGap: 10).Single(c => c.Key == "bolt-part-10-groups-42-Top");
        // The first point is the corner of the part's top edge (y 10); the bound ends at 100.
        Assert.Equal(90, top.BaseOffset, 6);
    }

    [Fact]
    public void BoltsWithoutAnEdgeOfTheirPlateAreCarriedByTheLocationChain()
    {
        // M.156 front view: a fin plate under the beam, pointed at the bottom (tip at x 550), its
        // edges slanted at the bolts' height; no edge of the plate ends the bolt chain. The bolts
        // go onto the Bottom location chain instead, as the detailer draws them.
        var group = new BoltGroupGeometry { ModelId = 42, Shape = "BoltArray", FirstPosition = [530, -180, 0],
            SecondPosition = [570, -180, 0], Axis = [0, 0, 1], PartToBeBoltedId = 30,
            Positions = [new() { Index = 0, Point = [530, -180, 0] }, new() { Index = 1, Point = [570, -180, 0] }] };
        var context = DimensionChainPreviewTests.Context(76, bolts: SnapshotOf(group),
            extraPolygons: [(30, [(500, -76), (600, -76), (600, -150), (550, -250), (500, -150)])]);
        var rows = context.Query("chain").GetProperty("chainPreview").EnumerateArray().ToArray();
        var location = rows.Single(row => row.GetProperty("side").GetString() == "Bottom").GetProperty("chains").EnumerateArray()
            .Single(chain => chain.GetProperty("key").GetString() == "Bottom-location");
        Assert.True(location.GetProperty("incomplete").GetBoolean());
        var bolt = rows.Single(row => row.GetProperty("side").GetString() == "Bolt").GetProperty("chains").EnumerateArray().ToArray();
        var carried = bolt.Single(chain => chain.GetProperty("key").GetString() == "Bottom-location-bolts");
        Assert.False(carried.GetProperty("incomplete").GetBoolean());
        var xs = carried.GetProperty("points").EnumerateArray().Select(point => point[0].GetDouble()).ToArray();
        Assert.Contains(530d, xs);
        Assert.Contains(570d, xs);
        Assert.Contains("carried by Bottom-location-bolts",
            bolt.Single(chain => chain.GetProperty("key").GetString() == "bolt-part-30-groups-42-Bottom").GetProperty("incompleteReason").GetString());
        // The batch writes it like any chain with its own points.
        var writes = new List<CreateDimensionRequest>();
        Provider(context, writes).CreateBatch(new CreateDimensionsBatchRequest {
            ViewId = 7, ContextId = context.ContextId, RuleSet = "steel", Chains = [new() { Preview = "Bottom-location-bolts" }] });
        Assert.Equal(xs.Length * 3, Assert.Single(writes).Points.Length);
    }

    [Fact]
    public void AnIncompleteBoltReadCarriesNoBoltsIntoTheLocationChain()
    {
        // As above, but the bolts of part 11 could not be read: no bolt dimension at all, so the
        // location chain keeps its place and nothing carried can be written.
        var group = new BoltGroupGeometry { ModelId = 42, Shape = "BoltArray", FirstPosition = [530, -180, 0],
            SecondPosition = [570, -180, 0], Axis = [0, 0, 1], PartToBeBoltedId = 30,
            Positions = [new() { Index = 0, Point = [530, -180, 0] }, new() { Index = 1, Point = [570, -180, 0] }] };
        var bolts = ViewBoltGeometrySnapshot.Capture(7, [10, 11], id => id == 11
                ? throw new InvalidOperationException("bolt read failed")
                : new() { Success = true, ViewId = 7, PartId = id, BoltGroups = [group] },
            new DepthBox(-1000, -1000, -1000, 1000, 1000, 1000), point => point, true);
        var context = DimensionChainPreviewTests.Context(76, bolts: bolts,
            extraPolygons: [(30, [(500, -76), (600, -76), (600, -150), (550, -250), (500, -150)])]);
        var rows = context.Query("chain").GetProperty("chainPreview").EnumerateArray().ToArray();
        var boltRow = rows.Single(row => row.GetProperty("side").GetString() == "Bolt");
        Assert.NotEqual(JsonValueKind.Null, boltRow.GetProperty("readIncomplete").ValueKind);
        Assert.DoesNotContain(boltRow.GetProperty("chains").EnumerateArray(), chain => chain.GetProperty("key").GetString() == "Bottom-location-bolts");
        Assert.False(rows.Single(row => row.GetProperty("side").GetString() == "Bottom").GetProperty("chains").EnumerateArray()
            .Single(chain => chain.GetProperty("key").GetString() == "Bottom-location").GetProperty("incomplete").GetBoolean());
        var writes = new List<CreateDimensionRequest>();
        Assert.Throws<ArgumentException>(() => Provider(context, writes).CreateBatch(new CreateDimensionsBatchRequest {
            ViewId = 7, ContextId = context.ContextId, RuleSet = "steel", Chains = [new() { Preview = "Bottom-location-bolts" }] }));
        Assert.Empty(writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheOverallNeverLandsOnTheLineOfTheCarriedBoltChainWhateverTheBatchOrder(bool overallFirst)
    {
        var group = new BoltGroupGeometry { ModelId = 42, Shape = "BoltArray", FirstPosition = [530, -180, 0],
            SecondPosition = [570, -180, 0], Axis = [0, 0, 1], PartToBeBoltedId = 30,
            Positions = [new() { Index = 0, Point = [530, -180, 0] }, new() { Index = 1, Point = [570, -180, 0] }] };
        var context = DimensionChainPreviewTests.Context(76, bolts: SnapshotOf(group),
            extraPolygons: [(30, [(500, -76), (600, -76), (600, -150), (550, -250), (500, -150)])]);
        context.Query("chain");
        var lines = new Dictionary<int, double>();
        var provider = new ViewDimensionContextProvider(() => "drawing-a", (_, _) => context, () => { },
            (request, distance) => {
                // The line of a Bottom chain: its first point along X, the distance downward.
                var first = Enumerable.Range(0, request.Points.Length / 3).OrderBy(i => request.Points[i * 3]).First();
                lines[request.Points.Length] = request.Points[first * 3 + 1] - distance;
                return new() { Created = true, DimensionId = lines.Count };
            },
            readDimensions: _ => new GetDimensionsResult(), validateAttributes: _ => { });
        provider.Get(7);
        BatchDimensionChain Overall() => new() { Preview = "Bottom-overall" };
        BatchDimensionChain Carried() => new() { Preview = "Bottom-location-bolts" };
        provider.CreateBatch(new CreateDimensionsBatchRequest { ViewId = 7, ContextId = context.ContextId, RuleSet = "steel",
            Chains = overallFirst ? [Overall(), Carried()] : [Carried(), Overall()] });
        // Two points for the overall, more for the carried chain; their lines one gap (80) apart at least.
        var overall = lines[6];
        var carried = lines.Single(line => line.Key > 6).Value;
        Assert.True(Math.Abs(overall - carried) >= 80 - 1e-6, $"overall {overall}, carried {carried}");
        Assert.True(overall < carried);
    }

    [Fact]
    public void ACarriedChainMirroringOneOnTheFrontViewIsARepeat()
    {
        // The context is a back view. The front view already holds the same carried chain, so its
        // mirror here is a repeat: not placed, and the batch refuses it.
        var group = new BoltGroupGeometry { ModelId = 42, Shape = "BoltArray", FirstPosition = [530, -180, 0],
            SecondPosition = [570, -180, 0], Axis = [0, 0, 1], PartToBeBoltedId = 30,
            Positions = [new() { Index = 0, Point = [530, -180, 0] }, new() { Index = 1, Point = [570, -180, 0] }] };
        var context = DimensionChainPreviewTests.Context(76, bolts: SnapshotOf(group),
            extraPolygons: [(30, [(500, -76), (600, -76), (600, -150), (550, -250), (500, -150)])]);
        JsonElement Carried(JsonElement answer) => answer.GetProperty("chainPreview").EnumerateArray()
            .Single(row => row.GetProperty("side").GetString() == "Bolt").GetProperty("chains").EnumerateArray()
            .Single(chain => chain.GetProperty("key").GetString() == "Bottom-location-bolts");
        var segments = Carried(context.Query("chain")).GetProperty("segments").EnumerateArray().Select(value => value.GetDouble()).ToArray();
        var answer = context.Query("chain,repeats",
            readSheet: () => [new DimensionChainRepeats.Placed(900, 99, "FrontView", segments.Reverse().ToArray(), true)]);
        var carried = Carried(answer);
        Assert.True(carried.GetProperty("incomplete").GetBoolean());
        Assert.Equal("mirror", carried.GetProperty("repeat").GetString());
        Assert.Contains(answer.GetProperty("repeats").EnumerateArray(), row => row.GetProperty("key").GetString() == "Bottom-location-bolts");
        var writes = new List<CreateDimensionRequest>();
        Assert.Throws<ArgumentException>(() => Provider(context, writes).CreateBatch(new CreateDimensionsBatchRequest {
            ViewId = 7, ContextId = context.ContextId, RuleSet = "steel", Chains = [new() { Preview = "Bottom-location-bolts" }] }));
        Assert.Empty(writes);
    }

    // A fin plate as long as the beam under it, pointed at the bottom: the Bottom location chain is
    // the two ends, 1000, so no overall is offered beside it; then the plate's edgeless bolts go
    // into that chain. The overall 1000 comes back on its own line, once.
    private static ViewDimensionContext FullLengthFinPlate()
    {
        var group = new BoltGroupGeometry { ModelId = 42, Shape = "BoltArray", FirstPosition = [450, -180, 0],
            SecondPosition = [550, -180, 0], Axis = [0, 0, 1], PartToBeBoltedId = 30,
            Positions = [new() { Index = 0, Point = [450, -180, 0] }, new() { Index = 1, Point = [550, -180, 0] }] };
        var bolts = ViewBoltGeometrySnapshot.Capture(7, [10, 30],
            id => new() { Success = true, ViewId = 7, PartId = id, BoltGroups = id == 30 ? [group] : [] },
            new DepthBox(-1000, -1000, -1000, 1000, 1000, 1000), point => point, true);
        return DimensionChainPreviewTests.Bare("FrontView", bolts, DimensionChainPreviewTests.Solid(10, 0, 1000, -76, 76),
            DimensionChainPreviewTests.Poly(30, (0, -76), (1000, -76), (1000, -150), (500, -250), (0, -150)));
    }

    private static JsonElement[] SideChains(JsonElement answer) => answer.GetProperty("chainPreview").EnumerateArray()
        .Where(row => row.GetProperty("side").GetString() is "Top" or "Bottom" or "Left" or "Right")
        .SelectMany(row => row.GetProperty("chains").EnumerateArray()).ToArray();

    [Fact]
    public void AnOverallGivenByALocationChainComesBackWhenBoltsAreCarriedIntoIt()
    {
        var context = FullLengthFinPlate();
        var answer = context.Query("chain,repeats", readSheet: () => []);
        // The 1000 alone is placed exactly once: the restored Bottom overall.
        var whole = SideChains(answer).Where(chain => !chain.GetProperty("incomplete").GetBoolean()
            && chain.GetProperty("segments").EnumerateArray().Select(value => value.GetDouble()).SequenceEqual([1000d])).ToArray();
        Assert.Equal("Bottom-overall", Assert.Single(whole).GetProperty("key").GetString());
        // The bolt steps stay in the carried chain.
        var carried = answer.GetProperty("chainPreview").EnumerateArray().Single(row => row.GetProperty("side").GetString() == "Bolt")
            .GetProperty("chains").EnumerateArray().Single(chain => chain.GetProperty("key").GetString() == "Bottom-location-bolts");
        Assert.Equal(new[] { 450d, 100, 450 }, carried.GetProperty("segments").EnumerateArray().Select(value => value.GetDouble()));
        // The batch takes both and gives them different lines.
        var lines = new List<double>();
        var provider = new ViewDimensionContextProvider(() => "drawing-a", (_, _) => context, () => { },
            (request, distance) => {
                var first = Enumerable.Range(0, request.Points.Length / 3).OrderBy(i => request.Points[i * 3]).First();
                lines.Add(request.Points[first * 3 + 1] - distance);
                return new() { Created = true, DimensionId = lines.Count };
            },
            readDimensions: _ => new GetDimensionsResult(), validateAttributes: _ => { });
        provider.Get(7);
        provider.CreateBatch(new CreateDimensionsBatchRequest { ViewId = 7, ContextId = context.ContextId, RuleSet = "steel",
            Chains = [new() { Preview = "Bottom-overall" }, new() { Preview = "Bottom-location-bolts" }] });
        Assert.Equal(2, lines.Count);
        Assert.True(Math.Abs(lines[0] - lines[1]) >= 80 - 1e-6, $"lines {lines[0]} and {lines[1]}");
    }

    [Fact]
    public void ARestoredOverallStaysARepeatOfTheSameOverallOnAnotherView()
    {
        // The top view already gives the length 1000: the restored overall is not placed again.
        var answer = FullLengthFinPlate().Query("chain,repeats",
            readSheet: () => [new DimensionChainRepeats.Placed(900, 99, "TopView", [1000], true)]);
        var overall = SideChains(answer).Single(chain => chain.GetProperty("key").GetString() == "Bottom-overall");
        Assert.True(overall.GetProperty("incomplete").GetBoolean());
        Assert.Equal("overallOnSheet", overall.GetProperty("repeat").GetString());
    }

    private static BoltGroupGeometry Sized(BoltGroupGeometry group, double size)
    {
        group.BoltSize = size;
        return group;
    }

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
