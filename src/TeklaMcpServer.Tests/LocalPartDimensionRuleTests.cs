using System.Collections.Generic;
using System.Linq;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

/// <summary>
/// A girder 1000 long and 800 high with flange faces at +-367, as on M.46, where the engineer
/// placed a short chain next to each gusset, rib and cover plate instead of one chain at the
/// end of the view.
/// </summary>
public sealed class LocalPartDimensionRuleTests
{
    private static readonly (double, double, double, double) Girder = (0, 1000, -400, 400);
    private static readonly double[] FaceLevels = [-400, -367, 367, 400];

    private static IReadOnlyList<DimensionRuleResult> Calculate(
        params (int Id, double X0, double X1, double Y0, double Y1)[] parts) =>
        new LocalPartDimensionRule(new LocalPartDimensionSettings()).Calculate(new DimensionRuleContext([], null,
            new LocalPartDimensionInput(Girder, [0, 1000], FaceLevels,
                parts.ToDictionary(part => part.Id, part => (part.X0, part.X1, part.Y0, part.Y1))))).Results;

    private static double[] Segments(DimensionRuleResult result) => result.Segments.ToArray();

    [Fact]
    public void AStiffenerAndAFinPlateWithASlantedEdgeAtOnePlaceShareOneChainToTheRealCorner()
    {
        // M.156 front view: a stiffener hanging under the girder (y -600..-594, x 500..700) and a fin
        // plate through it from the girder face down to its lowest corner at x 560 (box x 520..690).
        var results = new LocalPartDimensionRule(new LocalPartDimensionSettings()).Calculate(new DimensionRuleContext([], null,
            new LocalPartDimensionInput(Girder, [0, 1000], FaceLevels,
                new Dictionary<int, (double, double, double, double)> { [1] = (500, 700, -600, -594), [2] = (520, 690, -834, -400) },
                new Dictionary<int, (double LowAlong, double HighAlong)> { [2] = (560, 690) }))).Results;
        var chain = Assert.Single(results);
        Assert.Equal(new[] { 234d, 6, 194, 800 }, Segments(chain));
        // The witness line of the lowest point starts at the fin plate's own corner, not at its box.
        Assert.Equal(560, chain.Points[0].X);
        Assert.Equal(-834, chain.Points[0].Y);
    }

    [Fact]
    public void PartsThatTogetherLeaveATooShortSegmentKeepTheirOwnChainsOnTwoRows()
    {
        // At x 500..510: a rib on the flange (-367..-167.7) and a free plate just above it
        // (-166.7..-156.7). Together they would leave a 1 mm segment; each keeps its chain.
        var results = Calculate((1, 500, 510, -367, -167.7), (2, 500, 510, -166.7, -156.7));
        Assert.Equal(2, results.Count);
        Assert.Contains(results, result => Segments(result).SequenceEqual(new[] { 199.3 }));
        Assert.Contains(results, result => Segments(result).SequenceEqual(new[] { 233.3, 10, 556.7 }));
    }

    [Fact]
    public void LocalChainsThatCouldNotMergeAndANeighbourNeverShareALine()
    {
        // On the beam 0..1000 (y -76..76): a rib on the bottom face at x 500..510 up to -20, a free
        // plate just above it (-19..-9) that cannot join its chain, and a neighbour rib at 580..590
        // whose own line would fall where the plate's line is pushed to.
        var context = DimensionChainPreviewTests.Context(76, extraParts: [
            (20, 500, 510, -76, -20), (21, 500, 510, -19, -9), (22, 580, 590, -76, 0)]);
        var lines = context.LocalChains().Select(chain => {
            var normal = chain.Direction.Split(',').Select(value => double.Parse(value, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            var ys = Enumerable.Range(0, chain.Points.Length / 3).Select(i => chain.Points[i * 3 + 1]).ToArray();
            return (Line: chain.Points[0] * normal[0] + chain.Points[1] * normal[1] + chain.Distance, From: ys.Min(), To: ys.Max());
        }).ToArray();
        Assert.True(lines.Length >= 3);
        foreach (var a in lines)
            foreach (var b in lines.Where(b => b != a && b.From <= a.To && a.From <= b.To))
                Assert.True(Math.Abs(a.Line - b.Line) >= 80 - 1e-6, $"lines {a.Line} and {b.Line} overlap");
    }

    [Fact]
    public void RibStandingOnAFlangeIsMeasuredFromThatFlange()
    {
        var chain = Assert.Single(Calculate((1, 500, 510, -367, -167.7)));
        Assert.Equal(new[] { 199.3 }, Segments(chain));
        // The line stands past the rib; the points sit at the rib's far end.
        Assert.All(chain.Points, point => Assert.Equal(510, point.X));
    }

    [Fact]
    public void CoverPlateOnTheOuterEdgeIsMeasuredFromThatEdge()
    {
        Assert.Equal(new[] { 30d }, Segments(Assert.Single(Calculate((1, 100, 300, 400, 430)))));
    }

    [Fact]
    public void GussetTouchingNoFaceIsClosedOnBothOuterEdges()
    {
        Assert.Equal(new[] { 400d, 15d, 385d }, Segments(Assert.Single(Calculate((1, 200, 540, 0, 15)))));
    }

    [Fact]
    public void PartFittedBetweenTwoFacesGetsNoChain()
    {
        Assert.Empty(Calculate((1, 700, 710, -367, 367)));
    }

    [Fact]
    public void PartsAtOnePlaceShareOneChain()
    {
        // A gusset with a rib standing on it: one line reads both.
        var chain = Assert.Single(Calculate((1, 200, 540, 0, 15), (2, 400, 410, 15, 215.2)));
        Assert.Equal(new[] { 400d, 15d, 200.2, 184.8 }, Segments(chain));
        Assert.Equal(new[] { 1, 2 }, (int[])chain.Evidence["partIds"]);
        // The rib's top is taken at the rib (410), the rest at the gusset's end (540).
        Assert.Equal(new[] { 540d, 540d, 540d, 410d, 540d }, chain.Points.Select(point => point.X));
    }

    [Fact]
    public void SameChainNextDoorIsGivenOnceButAgainFarAway()
    {
        var chains = Calculate((1, 500, 510, -367, -167.7), (2, 670, 680, -367, -167.7), (3, 760, 770, -367, -167.7));
        // 2 and 3 stand 170 and 260 past the rib that carries the chain: within the repeat gap of 300.
        Assert.Single(chains);
        var far = Calculate((1, 100, 110, -367, -167.7), (2, 900, 910, -367, -167.7));
        Assert.Equal(2, far.Count);
    }

    [Fact]
    public void PartReachingPastTheEndIsLeftToTheEndPartRule()
    {
        Assert.Empty(Calculate((1, -20, 0, -430, 430)));
    }

    [Fact]
    public void PreviewAddsALocalRowAndTheBatchWritesItsChain()
    {
        var context = DimensionChainPreviewTests.Context(rightHalfHeight: 68, upperPlate: true);
        var rows = context.Query("chain").GetProperty("chainPreview").EnumerateArray().ToArray();
        var local = rows.Single(row => row.GetProperty("side").GetString() == "Local")
            .GetProperty("chains").EnumerateArray().ToArray();
        // The plate 20..120 on a beam -76..76 touches no face: the beam's two edges close the chain.
        var plate = local.Single(chain => chain.GetProperty("key").GetString() == "Local-16");
        Assert.Equal(new[] { 96d, 56d, 44d }, plate.GetProperty("segments").EnumerateArray().Select(v => v.GetDouble()));
        Assert.Equal("local", plate.GetProperty("kind").GetString());

        var writes = new List<CreateDimensionRequest>();
        var provider = new ViewDimensionContextProvider(() => "drawing-a", (_, _) => context, () => { },
            (request, _) => { writes.Add(request); return new() { Created = true, DimensionId = 101 }; },
            readDimensions: _ => new GetDimensionsResult(), validateAttributes: _ => { });
        provider.Get(7);
        provider.CreateBatch(new CreateDimensionsBatchRequest {
            ViewId = 7, ContextId = context.ContextId, RuleSet = "steel", Chains = [new() { Preview = "Local-16" }]
        });
        var write = Assert.Single(writes);
        Assert.Equal(plate.GetProperty("points").GetArrayLength() * 3, write.Points.Length);
        Assert.Equal(510, write.Points[0], 3);
    }
}
