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
